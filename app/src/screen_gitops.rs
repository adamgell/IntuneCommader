//! M15 Policy-as-Code (GitOps) — pull → plan → (gate) → apply over the sidecar's
//! /gitops/* routes (ApiClient::gitops_*). Two panes: a repo picker + action bar on
//! the left over the plan's change-set list; the selected object's field-level diff
//! on the right (reusing the M6 `drift_row` panel), with an inline apply-confirm gate.
//!
//!   Pull   POST /gitops/pull   — export the live tenant into a normalized repo tree.
//!   Plan   POST /gitops/plan   — diff the repo tree against live → a change-set.
//!   Apply  POST /gitops/apply  — gated tree-scope write (confirm + reviewed planId).
//!
//! Writes still land on the locked M6 rail: apply requires an explicit confirm, and
//! deletes (destroy) need a second, separate confirmation. There is no modal in the
//! reactor fork, so the confirm is an inline panel (see docs/part-ii/UNBUILT-SCREENS).

use api_types::{
    GitOpsApplyRequest, GitOpsApplyResult, GitOpsManifest, GitOpsPlan, GitOpsPlanObject,
    GitOpsPlanRequest, GitOpsPullRequest,
};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

// Stable list key for a plan object (surface + type + name — names can repeat across
// surfaces, so all three disambiguate the selection highlight).
fn obj_key(o: &GitOpsPlanObject) -> String {
    format!("{}|{}|{}", o.surface, o.object_type, o.object_name)
}

// Terraform-style verdict → color. Added=create (green), Modified=update (amber),
// Removed=destroy (red); anything else falls back to muted.
fn verdict_color(verdict: &str) -> Color {
    match verdict {
        "Added" => theme::OK,
        "Modified" => theme::WARN,
        "Removed" => theme::ERROR,
        _ => theme::TEXT_4,
    }
}

// A small pill badge with the given text + color (mirrors screen_cache::badge).
fn pill(text: &str, color: Color) -> Element {
    border(caption(text.to_string()).font_size(11.0).font_family(theme::FONT_UI).foreground(color))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(4.0)
        .padding(Thickness::xy(6.0, 2.0))
        .into()
}

// One change-set row: verdict pill + object name + surface/type/change-count meta.
fn plan_obj_row(o: &GitOpsPlanObject) -> Element {
    let heading = hstack((
        pill(&o.verdict, verdict_color(&o.verdict)),
        Element::from(body_strong(o.object_name.clone()).foreground(theme::TEXT).wrap()),
    ))
    .spacing(8.0);
    let meta = caption(format!(
        "{} · {} · {} field change(s)",
        o.surface,
        o.object_type,
        o.changes.len()
    ))
    .foreground(theme::TEXT_4)
    .font_family(theme::FONT_UI);
    vstack((Element::from(heading), Element::from(meta))).spacing(2.0).margin(Thickness::xy(8.0, 6.0)).into()
}

// Plan summary line: add / change / destroy / noop pills + the plan id.
fn plan_summary(p: &GitOpsPlan) -> Element {
    hstack((
        pill(&format!("+{} add", p.summary.add), theme::OK),
        pill(&format!("~{} change", p.summary.change), theme::WARN),
        pill(&format!("-{} destroy", p.summary.destroy), theme::ERROR),
        pill(&format!("{} noop", p.summary.noop), theme::TEXT_4),
        Element::from(
            caption(format!("plan {}", p.plan_id))
                .foreground(theme::TEXT_4)
                .font_family(theme::FONT_MONO),
        ),
    ))
    .spacing(8.0)
    .into()
}

pub fn gitops_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (repo, set_repo) = cx.use_state(String::new());
    let (selected, set_selected) = cx.use_state(String::new()); // plan object key
    let (confirming, set_confirming) = cx.use_state(false); // apply-confirm panel visible

    // Pull is fire-and-forget (writes the repo tree); plan is the central artifact
    // (drives both panes); apply is the gated write. Each is a bulk.rs busy-flag
    // resource keyed on (input, tick) — bumping the tick re-runs it.
    let (pull_key, set_pull_key) = cx.use_state(String::new());
    let (pull_tick, bump_pull) = cx.use_reducer(0_u64);
    let (plan_key, set_plan_key) = cx.use_state(String::new());
    let (plan_tick, bump_plan) = cx.use_reducer(0_u64);
    let (apply_req, set_apply_req) = cx.use_state::<Option<(String, String, bool)>>(None);
    let (apply_tick, bump_apply) = cx.use_reducer(0_u64);

    let pull = cx.use_resource(
        |k: (String, u64)| -> std::result::Result<Option<GitOpsManifest>, String> {
            let (repo, _) = k;
            if repo.is_empty() {
                return Ok(None);
            }
            api()
                .gitops_pull(&GitOpsPullRequest { output_path: repo, surfaces: None })
                .map(Some)
                .map_err(service_err)
        },
        (pull_key.clone(), pull_tick),
    );

    let plan = cx.use_resource(
        |k: (String, u64)| -> std::result::Result<Option<GitOpsPlan>, String> {
            let (repo, _) = k;
            if repo.is_empty() {
                return Ok(None);
            }
            api()
                .gitops_plan(&GitOpsPlanRequest { repo_path: repo, surfaces: None })
                .map(Some)
                .map_err(service_err)
        },
        (plan_key.clone(), plan_tick),
    );

    let apply = cx.use_resource(
        |k: (Option<(String, String, bool)>, u64)| -> std::result::Result<Option<GitOpsApplyResult>, String> {
            let (req, _) = k;
            match req {
                None => Ok(None),
                Some((repo, plan_id, confirm_destroy)) => api()
                    .gitops_apply(&GitOpsApplyRequest {
                        repo_path: repo,
                        plan_id,
                        confirm: true,
                        confirm_destroy,
                    })
                    .map(Some)
                    .map_err(service_err),
            }
        },
        (apply_req.clone(), apply_tick),
    );

    // After a successful apply the server consumed the plan (one-shot) — re-plan
    // against the same repo so the panes show the now-reduced drift, and drop the
    // confirm panel. Bump happens on the UI thread (never in the fetcher).
    let applied_ok = matches!(apply.data(), Some(Some(_)));
    {
        let bump_plan = bump_plan.clone();
        let set_confirming = set_confirming.clone();
        cx.use_effect(applied_ok, move || {
            if applied_ok {
                bump_plan.call(|n| n + 1);
                set_confirming.call(false);
            }
        });
    }

    let planning = plan.is_loading();
    let pulling = pull.is_loading();
    let applying = apply.is_loading();
    let list_height = crate::list_height(cx);

    // ── Action bar: repo path + Pull + Plan ────────────────────────────────────
    let repo_tb = text_box(repo.clone())
        .placeholder_text("Path to the Git-mirror repo folder…".to_string())
        .on_text_changed({
            let set_repo = set_repo.clone();
            move |t| set_repo.call(t)
        });
    let pull_btn = button(if pulling { "Pulling…" } else { "Pull" })
        .enabled(!repo.is_empty() && !pulling && !planning)
        .on_click({
            let set_pull_key = set_pull_key.clone();
            let bump_pull = bump_pull.clone();
            let repo = repo.clone();
            move || {
                set_pull_key.call(repo.clone());
                bump_pull.call(|n| n + 1);
            }
        });
    let plan_btn = button(if planning { "Planning…" } else { "Plan" })
        .accent()
        .enabled(!repo.is_empty() && !planning)
        .on_click({
            let set_plan_key = set_plan_key.clone();
            let bump_plan = bump_plan.clone();
            let set_selected = set_selected.clone();
            let set_confirming = set_confirming.clone();
            let repo = repo.clone();
            move || {
                set_plan_key.call(repo.clone());
                set_selected.call(String::new());
                set_confirming.call(false);
                bump_plan.call(|n| n + 1);
            }
        });
    let action_bar =
        hstack((Element::from(repo_tb), Element::from(pull_btn), Element::from(plan_btn))).spacing(8.0);

    // ── Status line: pull manifest / pull error (plan status shows in the summary) ─
    let status_line: Element = pull
        .view(|m: &Option<GitOpsManifest>| -> Element {
            match m {
                None => caption(
                    "Pull mirrors the signed-in tenant into the repo folder; Plan diffs it against live.",
                )
                .opacity(0.7)
                .wrap()
                .into(),
                Some(man) => caption(format!(
                    "Pulled {} object(s) across {} surface(s) — manifest {}",
                    man.object_count,
                    man.surfaces.len(),
                    man.content_hash
                ))
                .foreground(theme::OK)
                .font_family(theme::FONT_UI)
                .wrap()
                .into(),
            }
        })
        .loading(caption("exporting the tenant to the repo tree…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let plan_data: Option<GitOpsPlan> = plan.data().cloned().flatten();

    // ── LEFT: summary + change-set list ─────────────────────────────────────────
    let summary_el: Element = match &plan_data {
        Some(p) => plan_summary(p),
        None => Element::Empty,
    };

    let left_list: Element = if planning {
        caption("planning — diffing the repo tree against the live tenant…").opacity(0.6).into()
    } else if let Some(e) = plan.error() {
        crate::error_box(e)
    } else {
        match &plan_data {
            None => body("Enter a repo path, Pull to mirror the tenant, then Plan to see the change set.")
                .opacity(0.6)
                .wrap()
                .into(),
            Some(p) if p.objects.is_empty() => {
                body("In sync — the repo tree matches the live tenant.").opacity(0.7).wrap().into()
            }
            Some(p) => {
                let objs = p.objects.clone();
                let keys: Vec<String> = objs.iter().map(obj_key).collect();
                let sel_index = keys
                    .iter()
                    .position(|k| *k == selected)
                    .map(|i| i as i32)
                    .unwrap_or(-1);
                let set_selected = set_selected.clone();
                list_view(objs, |o: &GitOpsPlanObject, _| plan_obj_row(o))
                    .with_key_selector(obj_key)
                    .selected_index(sel_index)
                    .on_selection_changed(move |i| {
                        if let Some(k) = keys.get(i as usize) {
                            set_selected.call(k.clone());
                        }
                    })
                    .height(list_height - 130.0)
                    .into()
            }
        }
    };

    let left = crate::fluid_fill(12.0, vec![Element::from(action_bar), status_line, summary_el], left_list);

    // ── RIGHT: selected object diff (M6 drift_row) + inline apply gate ──────────
    let selected_obj: Option<GitOpsPlanObject> = plan_data
        .as_ref()
        .and_then(|p| p.objects.iter().find(|o| obj_key(o) == selected).cloned());

    let diff_area: Element = match &selected_obj {
        None => body("Select an object on the left to see its field-level changes.")
            .opacity(0.6)
            .wrap()
            .into(),
        Some(o) if o.changes.is_empty() => vstack((
            Element::from(body_strong(o.object_name.clone())),
            Element::from(
                caption(format!("{} — no field-level detail (whole-object {}).", o.verdict, o.verdict.to_lowercase()))
                    .opacity(0.7)
                    .wrap(),
            ),
        ))
        .spacing(6.0)
        .into(),
        Some(o) => {
            let summary = caption(format!("{} · {} change(s)", o.verdict, o.changes.len())).opacity(0.8);
            let list = list_view(o.changes.clone(), |c: &api_types::DriftChange, _| crate::drift_row(c))
                .with_key_selector(|c: &api_types::DriftChange| format!("{:?}:{}", c.kind, c.path))
                .height((list_height - 210.0).max(140.0));
            crate::fluid_fill(6.0, vec![summary.into()], list.into()).into()
        }
    };

    // Apply gate: only meaningful when the plan has writable changes.
    let has_writes = plan_data
        .as_ref()
        .map(|p| p.summary.add + p.summary.change + p.summary.destroy > 0)
        .unwrap_or(false);
    let destroys = plan_data.as_ref().map(|p| p.summary.destroy).unwrap_or(0);

    let apply_result: Element = apply
        .view(|r: &Option<GitOpsApplyResult>| -> Element {
            match r {
                None => Element::Empty,
                Some(res) => {
                    let head = caption(format!(
                        "Applied {} · skipped {} · conflict {}",
                        res.summary.applied, res.summary.skipped, res.summary.conflict
                    ))
                    .foreground(if res.summary.conflict > 0 { theme::WARN } else { theme::OK })
                    .font_family(theme::FONT_UI);
                    let rows: Vec<Element> = res
                        .objects
                        .iter()
                        .take(20)
                        .map(|o| {
                            let color = match o.outcome.as_str() {
                                "applied" => theme::OK,
                                "conflict" => theme::WARN,
                                _ => theme::TEXT_4,
                            };
                            let reason = o.reason.clone().map(|r| format!(" — {r}")).unwrap_or_default();
                            caption(format!("{} · {}{}", o.outcome, o.object_name, reason))
                                .foreground(color)
                                .font_family(theme::FONT_MONO)
                                .wrap()
                                .into()
                        })
                        .collect();
                    let mut items = vec![Element::from(head)];
                    items.extend(rows);
                    if res.summary.conflict > 0 {
                        items.push(
                            caption("Some objects changed since the plan — re-run Plan and apply again.")
                                .opacity(0.7)
                                .wrap()
                                .into(),
                        );
                    }
                    vstack(items).spacing(3.0).into()
                }
            }
        })
        .loading(caption("applying the reviewed plan to the tenant…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let apply_gate: Element = if !has_writes {
        apply_result
    } else if !confirming {
        let review_btn = button("Review & apply…").accent().enabled(!applying).on_click({
            let set_confirming = set_confirming.clone();
            move || set_confirming.call(true)
        });
        vstack((Element::from(review_btn), apply_result)).spacing(8.0).into()
    } else {
        let p = plan_data.as_ref().unwrap();
        let repo_now = repo.clone();
        let plan_id = p.plan_id.clone();
        let prompt = caption(format!(
            "Apply +{} / ~{} / -{} to the live tenant? Writes are snapshotted and audited; \
             deletes require the separate confirmation.",
            p.summary.add, p.summary.change, p.summary.destroy
        ))
        .foreground(theme::WARN)
        .wrap();

        let confirm_btn = button(if applying { "Applying…" } else { "Confirm apply (no deletes)" })
            .accent()
            .enabled(!applying)
            .on_click({
                let set_apply_req = set_apply_req.clone();
                let bump_apply = bump_apply.clone();
                let repo_now = repo_now.clone();
                let plan_id = plan_id.clone();
                move || {
                    set_apply_req.call(Some((repo_now.clone(), plan_id.clone(), false)));
                    bump_apply.call(|n| n + 1);
                }
            });

        let destroy_btn: Element = if destroys > 0 {
            button(if applying { "Applying…" } else { "Confirm apply + deletes" })
                .enabled(!applying)
                .on_click({
                    let set_apply_req = set_apply_req.clone();
                    let bump_apply = bump_apply.clone();
                    let repo_now = repo_now.clone();
                    let plan_id = plan_id.clone();
                    move || {
                        set_apply_req.call(Some((repo_now.clone(), plan_id.clone(), true)));
                        bump_apply.call(|n| n + 1);
                    }
                })
                .into()
        } else {
            Element::Empty
        };

        let cancel_btn = button("Cancel").enabled(!applying).on_click({
            let set_confirming = set_confirming.clone();
            move || set_confirming.call(false)
        });

        let panel = border(
            vstack((
                Element::from(prompt),
                Element::from(
                    hstack((Element::from(confirm_btn), destroy_btn, Element::from(cancel_btn)))
                        .spacing(8.0),
                ),
                apply_result,
            ))
            .spacing(8.0),
        )
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(8.0)
        .padding(Thickness::uniform(12.0));
        panel.into()
    };

    let right = grid((diff_area.grid_row(0), apply_gate.grid_row(1)))
        .rows([GridLength::Star(1.0), GridLength::Auto])
        .row_spacing(10.0);

    grid((left.grid_column(0), right.grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(1.4)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
