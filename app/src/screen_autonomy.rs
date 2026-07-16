//! M18 Autonomy — the closed-loop AI SRE surface: read/edit the per-tenant autonomy
//! policy, and browse the watch→detect→plan→simulate→propose→approve→apply→verify
//! run-log. Locked guarantee: autonomy only ENQUEUES proposals — a human approves
//! every diff in Pending AI Changes; Conditional Access stays read-only. Nothing on
//! this screen applies anything.
//!
//! Top: a compact policy editor (GET/PUT /autonomy/policy). Bottom: a run-log
//! master/detail (GET /autonomy/runs) — the selected run's detected changes reuse
//! the M6 `drift_row` panel; proposals show their blast-radius summary.

use api_types::{
    AutonomyDetection, AutonomyPolicy, AutonomyProposal, AutonomyRun, AutonomyScope,
    AutonomySignalConfig, AutonomySignals, AutonomyThrottle,
};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

const SEVERITIES: [&str; 6] = ["any", "info", "low", "medium", "high", "critical"];

fn sev_opt(s: &str) -> Option<String> {
    if s.is_empty() || s == "any" { None } else { Some(s.to_string()) }
}
fn split_csv(s: &str) -> Vec<String> {
    s.split(',').map(|t| t.trim().to_string()).filter(|t| !t.is_empty()).collect()
}

fn pill(text: &str, color: Color) -> Element {
    border(caption(text.to_string()).font_size(11.0).font_family(theme::FONT_UI).foreground(color))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(4.0)
        .padding(Thickness::xy(6.0, 2.0))
        .into()
}

// A boolean rendered as a toggle button (no checkbox primitive in the reactor fork).
fn toggle(label: String, on: bool, set: &SetState<bool>) -> Element {
    let set = set.clone();
    let b = button(format!("{label}: {}", if on { "ON" } else { "off" }))
        .on_click(move || set.call(!on));
    if on { b.accent().into() } else { b.into() }
}

fn blast_line(p: &AutonomyProposal) -> Element {
    let blast = match &p.blast_radius {
        Some(b) => format!(
            "blast {} · {} users / {} devices",
            b.severity, b.affected_user_count, b.affected_device_count
        ),
        None => "no blast estimate".to_string(),
    };
    vstack((
        Element::from(caption(format!("{} · {} · {}", p.kind, p.path, p.pending_change_id)).foreground(theme::TEXT_3).font_family(theme::FONT_MONO).wrap()),
        Element::from(caption(blast).foreground(theme::WARN)),
    ))
    .spacing(1.0)
    .into()
}

fn detection_block(d: &AutonomyDetection) -> Element {
    let head = hstack((
        pill(&d.severity, theme::WARN),
        Element::from(
            caption(format!("{} · {}", d.signal, d.object_name.clone().unwrap_or_else(|| d.object_id.clone())))
                .foreground(theme::TEXT_2)
                .wrap(),
        ),
    ))
    .spacing(8.0);
    let mut items = vec![head.into()];
    items.extend(d.changes.iter().take(20).map(|c| crate::drift_row(c)));
    vstack(items).spacing(4.0).into()
}

fn run_detail(r: &AutonomyRun) -> Element {
    let converged = r.verify.as_ref().map(|v| v.converged).unwrap_or(false);
    let head = hstack((
        pill(if converged { "converged" } else { "open" }, if converged { theme::OK } else { theme::TEXT_4 }),
        Element::from(body_strong(r.run_id.clone()).foreground(theme::TEXT).wrap()),
    ))
    .spacing(8.0);
    let times = caption(format!(
        "started {}{}",
        r.started_utc,
        r.finished_utc.clone().map(|f| format!(" · finished {f}")).unwrap_or_default()
    ))
    .foreground(theme::TEXT_4)
    .font_family(theme::FONT_MONO);
    let watched = caption(format!(
        "watched: {} audit event(s), {} snapshot(s) changed",
        r.watched.audit_events_pulled, r.watched.snapshots_changed
    ))
    .foreground(theme::TEXT_3);

    let mut sections: Vec<Element> = vec![head.into(), times.into(), watched.into()];

    if !r.detected.is_empty() {
        let mut items =
            vec![Element::from(caption(format!("Detected ({})", r.detected.len())).foreground(theme::TEXT_4))];
        items.extend(r.detected.iter().map(detection_block));
        sections.push(vstack(items).spacing(8.0).into());
    }
    if !r.proposed.is_empty() {
        let mut items =
            vec![Element::from(caption(format!("Proposed ({}) — awaiting approval in Pending AI Changes", r.proposed.len())).foreground(theme::TEXT_4))];
        items.extend(r.proposed.iter().map(blast_line));
        sections.push(vstack(items).spacing(6.0).into());
    }
    if let Some(dec) = &r.decision {
        sections.push(
            caption(format!(
                "Decision: {} on {}{}",
                dec.action,
                dec.pending_change_id,
                dec.operator.clone().map(|o| format!(" by {o}")).unwrap_or_default()
            ))
            .foreground(theme::TEXT_2)
            .wrap()
            .into(),
        );
    }
    if let Some(v) = &r.verify {
        sections.push(
            caption(format!(
                "Verify: {} — {} residual drift change(s)",
                if v.converged { "converged" } else { "not converged" },
                v.residual_drift_changes
            ))
            .foreground(if v.converged { theme::OK } else { theme::WARN })
            .into(),
        );
    }

    vstack(sections).spacing(12.0).into()
}

fn run_row(r: &AutonomyRun) -> Element {
    let converged = r.verify.as_ref().map(|v| v.converged).unwrap_or(false);
    let badge: Element = if r.proposed.is_empty() {
        pill(&format!("{} detected", r.detected.len()), theme::TEXT_4)
    } else {
        pill(&format!("{} proposed", r.proposed.len()), theme::BRAND_BRIGHT)
    };
    let conv: Element = if converged { pill("converged", theme::OK) } else { Element::Empty };
    vstack((
        Element::from(hstack((badge, conv)).spacing(6.0)),
        Element::from(caption(r.run_id.clone()).foreground(theme::TEXT_2).font_family(theme::FONT_MONO).wrap()),
        Element::from(caption(r.started_utc.clone()).foreground(theme::TEXT_4).font_family(theme::FONT_MONO)),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

pub fn autonomy_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // Policy working-copy fields (seeded from the fetched policy; re-seeded after save).
    let (enabled, set_enabled) = cx.use_state(false);
    let (cadence, set_cadence) = cx.use_state(String::new());
    let (drift_on, set_drift_on) = cx.use_state(false);
    let (drift_sev, set_drift_sev) = cx.use_state(String::from("any"));
    let (posture_on, set_posture_on) = cx.use_state(false);
    let (posture_drop, set_posture_drop) = cx.use_state(String::new());
    let (advisory_on, set_advisory_on) = cx.use_state(false);
    let (advisory_sev, set_advisory_sev) = cx.use_state(String::from("any"));
    let (max_prop, set_max_prop) = cx.use_state(String::new());
    let (max_inflight, set_max_inflight) = cx.use_state(String::new());
    let (obj_types, set_obj_types) = cx.use_state(String::new());
    let (allowlist, set_allowlist) = cx.use_state(String::new());
    let (note, set_note) = cx.use_state(String::new());

    let (selected_run, set_selected_run) = cx.use_state(String::new());
    let (tick, bump) = cx.use_reducer(0_u64);
    let (save, do_save) = cx.use_mutation::<()>();

    let policy = cx.use_resource(|_: u64| api().autonomy_policy().map_err(service_err), tick);
    let runs = cx.use_resource(|_: u64| api().autonomy_runs().map_err(service_err), tick);

    // Seed the editor states from the loaded (normalized) policy. Keyed on the
    // loaded value, so it re-seeds after a save reloads it — but not on each edit
    // (edits mutate the states, not the resource).
    {
        let loaded = policy.data().cloned();
        let is_loading = policy.is_loading();
        let setters = (
            set_enabled.clone(),
            set_cadence.clone(),
            set_drift_on.clone(),
            set_drift_sev.clone(),
            set_posture_on.clone(),
            set_posture_drop.clone(),
        );
        let setters2 = (
            set_advisory_on.clone(),
            set_advisory_sev.clone(),
            set_max_prop.clone(),
            set_max_inflight.clone(),
            set_obj_types.clone(),
            set_allowlist.clone(),
            set_note.clone(),
        );
        cx.use_effect(loaded.clone(), move || {
            if is_loading {
                return;
            }
            if let Some(p) = loaded {
                let (se, sc, sdo, sds, spo, spd) = &setters;
                se.call(p.enabled);
                sc.call(p.cadence_minutes.to_string());
                sdo.call(p.signals.drift.enabled);
                sds.call(p.signals.drift.min_severity.clone().unwrap_or_else(|| "any".into()));
                spo.call(p.signals.posture_regression.enabled);
                spd.call(
                    p.signals
                        .posture_regression
                        .min_score_drop
                        .map(|n| n.to_string())
                        .unwrap_or_default(),
                );
                let (sao, sas, smp, smi, sot, sal, sn) = &setters2;
                sao.call(p.signals.advisory.enabled);
                sas.call(p.signals.advisory.min_severity.clone().unwrap_or_else(|| "any".into()));
                smp.call(p.throttle.max_proposals_per_run.to_string());
                smi.call(p.throttle.max_inflight_pending.to_string());
                sot.call(p.scope.object_types.join(", "));
                sal.call(p.scope.assignment_group_allowlist.join(", "));
                sn.call(p.note.clone());
            }
        });
    }

    // Reload after a save so the editor adopts the server-normalized policy.
    let saved = save.data().is_some();
    {
        let bump = bump.clone();
        let do_save = do_save.clone();
        cx.use_effect(saved, move || {
            if saved {
                bump.call(|n| n + 1);
                do_save.reset();
            }
        });
    }

    let list_height = crate::list_height(cx);
    let saving = save.is_loading();

    // ── Policy editor ──────────────────────────────────────────────────────
    let lock_note = caption(
        "Autonomy only ENQUEUES proposals — a human approves every diff in Pending AI Changes. \
         Conditional Access stays read-only.",
    )
    .foreground(theme::TEXT_3)
    .wrap();

    let row1 = hstack((
        toggle("Autonomy".into(), enabled, &set_enabled),
        Element::from(
            text_box(cadence.clone())
                .placeholder_text("cadence min".to_string())
                .on_text_changed({ let s = set_cadence.clone(); move |t| s.call(t) }),
        ),
        Element::from(
            text_box(max_prop.clone())
                .placeholder_text("max proposals/run".to_string())
                .on_text_changed({ let s = set_max_prop.clone(); move |t| s.call(t) }),
        ),
        Element::from(
            text_box(max_inflight.clone())
                .placeholder_text("max inflight".to_string())
                .on_text_changed({ let s = set_max_inflight.clone(); move |t| s.call(t) }),
        ),
    ))
    .spacing(8.0);

    let drift_sev_combo = ComboBox::new(SEVERITIES.iter().map(|s| s.to_string()).collect::<Vec<_>>())
        .header("Drift min severity")
        .selected_index(SEVERITIES.iter().position(|x| *x == drift_sev).map(|i| i as i32).unwrap_or(0))
        .on_selection_changed({
            let s = set_drift_sev.clone();
            move |i: i32| {
                if let Some(v) = SEVERITIES.get(i as usize) {
                    s.call(v.to_string());
                }
            }
        });
    let advisory_sev_combo = ComboBox::new(SEVERITIES.iter().map(|s| s.to_string()).collect::<Vec<_>>())
        .header("Advisory min severity")
        .selected_index(SEVERITIES.iter().position(|x| *x == advisory_sev).map(|i| i as i32).unwrap_or(0))
        .on_selection_changed({
            let s = set_advisory_sev.clone();
            move |i: i32| {
                if let Some(v) = SEVERITIES.get(i as usize) {
                    s.call(v.to_string());
                }
            }
        });

    let signals_row = hstack((
        toggle("Drift".into(), drift_on, &set_drift_on),
        Element::from(drift_sev_combo),
        toggle("Posture".into(), posture_on, &set_posture_on),
        Element::from(
            text_box(posture_drop.clone())
                .placeholder_text("min score drop".to_string())
                .on_text_changed({ let s = set_posture_drop.clone(); move |t| s.call(t) }),
        ),
        toggle("Advisory".into(), advisory_on, &set_advisory_on),
        Element::from(advisory_sev_combo),
    ))
    .spacing(8.0);

    let scope_row = hstack((
        Element::from(
            text_box(obj_types.clone())
                .placeholder_text("scope object types (comma)".to_string())
                .on_text_changed({ let s = set_obj_types.clone(); move |t| s.call(t) }),
        ),
        Element::from(
            text_box(allowlist.clone())
                .placeholder_text("assignment-group allowlist (comma)".to_string())
                .on_text_changed({ let s = set_allowlist.clone(); move |t| s.call(t) }),
        ),
    ))
    .spacing(8.0);

    let note_tb = text_box(note.clone())
        .placeholder_text("note".to_string())
        .on_text_changed({ let s = set_note.clone(); move |t| s.call(t) });

    let save_btn = {
        let do_save = do_save.clone();
        let enabled = enabled;
        let cadence = cadence.clone();
        let drift_on = drift_on;
        let drift_sev = drift_sev.clone();
        let posture_on = posture_on;
        let posture_drop = posture_drop.clone();
        let advisory_on = advisory_on;
        let advisory_sev = advisory_sev.clone();
        let max_prop = max_prop.clone();
        let max_inflight = max_inflight.clone();
        let obj_types = obj_types.clone();
        let allowlist = allowlist.clone();
        let note = note.clone();
        button(if saving { "Saving…" } else { "Save policy" }).accent().enabled(!saving).on_click(move || {
            let p = AutonomyPolicy {
                tenant_id: None, // stamped server-side
                enabled,
                cadence_minutes: cadence.trim().parse().unwrap_or(60),
                scope: AutonomyScope {
                    object_types: split_csv(&obj_types),
                    assignment_group_allowlist: split_csv(&allowlist),
                },
                signals: AutonomySignals {
                    drift: AutonomySignalConfig {
                        enabled: drift_on,
                        min_severity: sev_opt(&drift_sev),
                        min_score_drop: None,
                    },
                    posture_regression: AutonomySignalConfig {
                        enabled: posture_on,
                        min_severity: None,
                        min_score_drop: posture_drop.trim().parse().ok(),
                    },
                    advisory: AutonomySignalConfig {
                        enabled: advisory_on,
                        min_severity: sev_opt(&advisory_sev),
                        min_score_drop: None,
                    },
                },
                throttle: AutonomyThrottle {
                    max_proposals_per_run: max_prop.trim().parse().unwrap_or(0),
                    max_inflight_pending: max_inflight.trim().parse().unwrap_or(0),
                },
                note: note.clone(),
            };
            do_save.fire(move || api().set_autonomy_policy(&p).map(|_| ()).map_err(service_err));
        })
    };
    let save_err: Element = save
        .error()
        .map(|e| caption(format!("save failed: {e}")).foreground(theme::ERROR).wrap().into())
        .unwrap_or(Element::Empty);
    let policy_err: Element = policy
        .error()
        .map(|e| caption(format!("policy: {e}")).foreground(theme::TEXT_4).wrap().into())
        .unwrap_or(Element::Empty);

    let policy_form = vstack((
        Element::from(body_strong("Autonomy policy").font_family(theme::FONT_UI).foreground(theme::TEXT)),
        Element::from(lock_note),
        Element::from(row1),
        Element::from(signals_row),
        Element::from(scope_row),
        Element::from(note_tb),
        Element::from(hstack((Element::from(save_btn), save_err, policy_err)).spacing(8.0)),
    ))
    .spacing(8.0);
    let policy_panel = border(scroll_viewer(policy_form).height(230.0))
        .corner_radius(8.0)
        .border_brush(theme::LINE)
        .padding(Thickness::uniform(10.0));

    // ── Run-log master/detail ──────────────────────────────────────────────
    let runs_data: Vec<AutonomyRun> = runs.data().cloned().unwrap_or_default();
    let sel = selected_run.clone();
    let run_list: Element = runs
        .view(move |rows: &Vec<AutonomyRun>| -> Element {
            if rows.is_empty() {
                return body("No autonomy runs yet. When the loop ticks, runs appear here.")
                    .opacity(0.6)
                    .wrap()
                    .into();
            }
            let rows = rows.clone();
            let ids: Vec<String> = rows.iter().map(|r| r.run_id.clone()).collect();
            let sel_index = ids.iter().position(|id| *id == sel).map(|i| i as i32).unwrap_or(-1);
            let set_selected_run = set_selected_run.clone();
            list_view(rows, |r: &AutonomyRun, _| run_row(r))
                .with_key_selector(|r: &AutonomyRun| r.run_id.clone())
                .selected_index(sel_index)
                .on_selection_changed(move |i| {
                    if let Some(id) = ids.get(i as usize) {
                        set_selected_run.call(id.clone());
                    }
                })
                .height((list_height - 300.0).max(120.0))
                .into()
        })
        .loading(caption("loading run log…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let detail: Element = match runs_data.iter().find(|r| r.run_id == selected_run) {
        Some(r) => scroll_viewer(run_detail(r)).height((list_height - 260.0).max(140.0)).into(),
        None => body("Select a run to see its watch → detect → propose → verify chain.")
            .opacity(0.6)
            .wrap()
            .into(),
    };

    let runlog = grid((
        crate::fluid_fill(8.0, vec![Element::from(body_strong("Run log").foreground(theme::TEXT_3))], run_list)
            .grid_column(0),
        detail.grid_column(1),
    ))
    .columns([GridLength::Star(1.0), GridLength::Star(1.5)])
    .column_spacing(16.0);

    grid((Element::from(policy_panel).grid_row(0), runlog.grid_row(1)))
        .rows([GridLength::Auto, GridLength::Star(1.0)])
        .row_spacing(12.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
