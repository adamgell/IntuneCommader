//! Bulk App Assignment — apply the same assignment set to many apps at once. A
//! multi-select app picker (left) + an intent-aware assignment builder (right) over
//! POST /apps/assign. Dry-run (the default) previews per-app without writing; a live
//! apply replaces each selected app's assignment set (replace-all per app) and reports
//! per-app success/error.
//!
//! There is no multi-select list primitive in the reactor fork, so selection is a
//! toggle-button list backed by a Vec<String> of app ids in the workspace's own state.

use api_types::{Assignment, BulkAssignItemResult, BulkAssignRequest, BulkAssignResult, ListItem};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

fn pill(text: &str, color: Color) -> Element {
    border(caption(text.to_string()).font_size(11.0).font_family(theme::FONT_UI).foreground(color))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(4.0)
        .padding(Thickness::xy(6.0, 2.0))
        .into()
}

// A group-intent add button appending a fresh Assignment{kind:group, intent}.
fn add_btn(label: &str, intent: &'static str, working: &[Assignment], set_working: &SetState<Vec<Assignment>>) -> Element {
    let cur = working.to_vec();
    let set = set_working.clone();
    button(label.to_string())
        .on_click(move || {
            let mut w = cur.clone();
            w.push(Assignment {
                kind: "group".to_string(),
                group_id: None,
                group_name: None,
                filter_id: None,
                filter_mode: None,
                intent: Some(intent.to_string()),
            });
            set.call(w);
        })
        .into()
}

fn outcome_row(r: &BulkAssignItemResult) -> Element {
    let color = if r.ok { theme::OK } else { theme::ERROR };
    let msg = if r.ok {
        "ok".to_string()
    } else {
        r.error.clone().unwrap_or_else(|| "failed".to_string())
    };
    caption(format!("{} · {}", r.app_id, msg)).foreground(color).font_family(theme::FONT_MONO).wrap().into()
}

pub fn bulk_assign_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (filter, set_filter) = cx.use_state(String::new());
    let (selected, set_selected) = cx.use_state::<Vec<String>>(Vec::new());
    let (working, set_working) = cx.use_state::<Vec<Assignment>>(Vec::new());
    let (req, set_req) = cx.use_state::<Option<BulkAssignRequest>>(None);
    let (tick, bump) = cx.use_reducer(0_u64);

    let apps = cx.use_resource(|_: ()| api().get_list("/apps").map_err(service_err), ());
    let result = cx.use_resource(
        |k: (Option<BulkAssignRequest>, u64)| -> std::result::Result<Option<BulkAssignResult>, String> {
            match k.0 {
                None => Ok(None),
                Some(r) => api().bulk_assign(&r).map(Some).map_err(service_err),
            }
        },
        (req.clone(), tick),
    );

    let list_height = crate::list_height(cx);
    let busy = result.is_loading();
    let filter_lc = filter.to_lowercase();

    // ── Left: multi-select app list ────────────────────────────────────────
    let filter_box = text_box(filter.clone())
        .placeholder_text("Filter apps…".to_string())
        .on_text_changed({
            let s = set_filter.clone();
            move |t| s.call(t)
        });
    let sel_count = caption(format!("{} app(s) selected", selected.len())).foreground(theme::TEXT_3);

    let sel_for_list = selected.clone();
    let app_list: Element = apps
        .view(move |rows: &Vec<ListItem>| -> Element {
            let matches: Vec<ListItem> = rows
                .iter()
                .filter(|a| filter_lc.is_empty() || a.title.to_lowercase().contains(&filter_lc))
                .take(300)
                .cloned()
                .collect();
            if matches.is_empty() {
                return body("No apps (sign in, or clear the filter).").opacity(0.6).wrap().into();
            }
            let toggles: Vec<Element> = matches
                .iter()
                .map(|a| {
                    let on = sel_for_list.contains(&a.id);
                    let cur = sel_for_list.clone();
                    let set = set_selected.clone();
                    let id = a.id.clone();
                    let b = button(format!("{} {}", if on { "[x]" } else { "[ ]" }, a.title))
                        .on_click(move || {
                            let mut v = cur.clone();
                            if let Some(p) = v.iter().position(|x| *x == id) {
                                v.remove(p);
                            } else {
                                v.push(id.clone());
                            }
                            set.call(v);
                        });
                    if on { b.accent().into() } else { b.into() }
                })
                .collect();
            scroll_viewer(vstack(toggles).spacing(3.0)).height((list_height - 80.0).max(140.0)).into()
        })
        .loading(caption("loading apps…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let left = crate::fluid_fill(
        10.0,
        vec![Element::from(filter_box), Element::from(sel_count)],
        app_list,
    );

    // ── Right: assignment builder + actions + results ──────────────────────
    let adds = hstack((
        add_btn("+ Required group", "required", &working, &set_working),
        add_btn("+ Available group", "available", &working, &set_working),
        add_btn("+ Uninstall group", "uninstall", &working, &set_working),
    ))
    .spacing(8.0);

    let mut editor_rows: Vec<Element> = Vec::new();
    for (i, a) in working.iter().enumerate() {
        let intent = a.intent.clone().unwrap_or_default();
        let gbox = {
            let cur = working.to_vec();
            let set = set_working.clone();
            text_box(a.group_id.clone().unwrap_or_default())
                .placeholder_text("group id".to_string())
                .on_text_changed(move |t: String| {
                    let mut w = cur.clone();
                    w[i].group_id = if t.is_empty() { None } else { Some(t) };
                    set.call(w);
                })
        };
        let rm = {
            let cur = working.to_vec();
            let set = set_working.clone();
            button("Remove").on_click(move || {
                let mut w = cur.clone();
                w.remove(i);
                set.call(w);
            })
        };
        editor_rows.push(Element::from(
            hstack((
                pill(&intent, theme::BRAND_BRIGHT),
                Element::from(gbox),
                Element::from(rm),
            ))
            .spacing(8.0),
        ));
    }
    let editor = vstack(editor_rows).spacing(6.0);

    let can_run = !busy && !selected.is_empty() && !working.is_empty();
    let mk_req = |selected: &[String], working: &[Assignment], dry: bool| BulkAssignRequest {
        app_ids: selected.to_vec(),
        assignments: working.to_vec(),
        dry_run: Some(dry),
    };
    let dry_btn = button("Dry-run").enabled(can_run).on_click({
        let set_req = set_req.clone();
        let bump = bump.clone();
        let selected = selected.clone();
        let working = working.clone();
        move || {
            set_req.call(Some(mk_req(&selected, &working, true)));
            bump.call(|n| n + 1);
        }
    });
    let apply_btn = button(if busy { "Applying…" } else { "Apply (replace-all)" })
        .accent()
        .enabled(can_run)
        .on_click({
            let set_req = set_req.clone();
            let bump = bump.clone();
            let selected = selected.clone();
            let working = working.clone();
            move || {
                set_req.call(Some(mk_req(&selected, &working, false)));
                bump.call(|n| n + 1);
            }
        });
    let note = caption(
        "Replace-all per app — a live apply overwrites each selected app's whole assignment set. \
         Dry-run first; then apply. Sign in required.",
    )
    .foreground(theme::TEXT_4)
    .wrap();

    let results_el: Element = result
        .view(|r: &Option<BulkAssignResult>| -> Element {
            match r {
                None => caption("Select apps + build assignments, then Dry-run.").opacity(0.6).wrap().into(),
                Some(res) => {
                    let ok = res.results.iter().filter(|x| x.ok).count();
                    let bad = res.results.len() - ok;
                    let head = caption(format!("{} ok · {} failed", ok, bad))
                        .foreground(if bad > 0 { theme::WARN } else { theme::OK })
                        .font_family(theme::FONT_UI);
                    let rows: Vec<Element> = res.results.iter().take(200).map(outcome_row).collect();
                    let mut items = vec![Element::from(head)];
                    items.extend(rows);
                    vstack(items).spacing(3.0).into()
                }
            }
        })
        .loading(caption("assigning…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let right_chrome = vstack((
        Element::from(body_strong("Assignments").font_family(theme::FONT_UI).foreground(theme::TEXT_3)),
        Element::from(adds),
        Element::from(editor),
        Element::from(hstack((Element::from(dry_btn), Element::from(apply_btn))).spacing(8.0)),
        Element::from(note),
    ))
    .spacing(8.0);
    let right = crate::fluid_fill(
        10.0,
        vec![Element::from(right_chrome)],
        scroll_viewer(results_el).height((list_height - 220.0).max(120.0)).into(),
    );

    grid((left.grid_column(0), right.grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(1.1)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
