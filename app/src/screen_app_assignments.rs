//! Application Assignments — a dedicated, **assignments-first** Applications
//! workspace. The existing `/apps` screen edits assignments behind an
//! object-detail/edit pane; this screen drops that chrome: pick an app on the left
//! and you land straight in the intent-aware `assignment_editor` on the right.
//!
//! The value-add vs the Applications screen is the workflow — pick app → editor,
//! no JSON view/edit step. The backend, DTOs, and editor all already ship; this is
//! pure client wiring.
//!
//! The `assignment_editor` is a render helper, **not** a nested `component()`, so
//! its `working` set + save mutation MUST live in *this* workspace's top-level hook
//! context and be threaded in by reference (a nested component wouldn't re-render on
//! its own state — the documented reactor gotcha). This replicates the exact
//! seed/save boilerplate from `list_workspace`'s "assign" mode.

use api_types::{Assignment, ListItem};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::assignments::assignment_editor;
use crate::theme;

// One app picker row: title + optional "assigned" badge, subtitle underneath.
fn app_row(r: &ListItem) -> Element {
    let title_el = body_strong(r.title.clone())
        .font_family(theme::FONT_UI)
        .foreground(theme::TEXT);
    let heading: Element = if let Some(b) = &r.badge {
        let pill = border(
            caption(b.clone())
                .foreground(theme::BRAND_BRIGHT)
                .font_family(theme::FONT_UI),
        )
        .background(theme::SURFACE_2)
        .corner_radius(9.0)
        .padding(Thickness::xy(8.0, 1.0));
        hstack((Element::from(title_el), Element::from(pill)))
            .spacing(8.0)
            .into()
    } else {
        Element::from(title_el)
    };
    vstack((
        heading,
        Element::from(caption(r.subtitle.clone()).foreground(theme::TEXT_3)),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

pub fn app_assignments_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (filter, set_filter) = cx.use_state(String::new());
    let (selected, set_selected) = cx.use_state(String::new()); // selected app id
    let (refresh, bump) = cx.use_reducer(0_u64);

    // Assignment editor state. The editor is a render helper (not a nested
    // component), so its working set + save mutation live in this — the parent's —
    // hook context; nested `component()` children don't re-render on their own
    // state changes in the reactor. See assignments.rs.
    let (working, set_working) = cx.use_state::<Vec<Assignment>>(Vec::new());
    let (asg_save, do_asg_save) = cx.use_mutation::<()>();

    // When a save completes, refresh the app list + current assignments (so the
    // editor re-seeds from the freshly-written set) and reset the mutation so the
    // next save can fire.
    let write_done = asg_save.data().is_some();
    {
        let bump = bump.clone();
        let do_asg_save = do_asg_save.clone();
        cx.use_effect(write_done, move || {
            if write_done {
                bump.call(|n| n + 1);
                do_asg_save.reset();
            }
        });
    }

    let apps = cx.use_resource(
        |_: u64| api().get_list("/apps").map_err(service_err),
        refresh,
    );

    // Current assignments for the selected app, fetched here (the proven resource
    // context) and handed to the editor as data. No selection → empty (no network).
    let assignments = cx.use_resource(
        |key: (String, u64)| {
            let (id, _) = key;
            if id.is_empty() {
                return Ok(Vec::<Assignment>::new());
            }
            api().get_assignments("/apps", &id).map_err(service_err)
        },
        (selected.clone(), refresh),
    );

    // Seed the editor's working set from the freshly-loaded current assignments —
    // re-seeding whenever the selection changes or the set reloads (e.g. after a
    // save bumps `refresh`), so each app's editor starts from its own current
    // state. Gated on `!is_loading` so a selection change doesn't seed from the
    // previous app's still-cached (Reloading) data.
    {
        let set_working = set_working.clone();
        let loaded = assignments.data().cloned();
        let is_loading = assignments.is_loading();
        cx.use_effect((selected.clone(), is_loading), move || {
            if !is_loading {
                if let Some(rows) = loaded {
                    set_working.call(rows);
                }
            }
        });
    }

    let list_height = crate::list_height(cx);

    let filter_box = auto_suggest_box(filter.clone())
        .placeholder_text("Filter applications…".to_string())
        .on_text_changed({
            let s = set_filter.clone();
            move |t| s.call(t)
        });

    let filter_lc = filter.to_lowercase();
    let sel = selected.clone();
    let set_sel_list = set_selected.clone();
    let list_el: Element = apps
        .view(move |all: &Vec<ListItem>| -> Element {
            let matches: Vec<ListItem> = all
                .iter()
                .filter(|r| {
                    filter_lc.is_empty()
                        || r.title.to_lowercase().contains(&filter_lc)
                        || r.subtitle.to_lowercase().contains(&filter_lc)
                })
                .cloned()
                .collect();
            if matches.is_empty() {
                return body("No applications. Sign in (top right) if this needs Graph data.")
                    .opacity(0.6)
                    .wrap()
                    .into();
            }
            let ids: Vec<String> = matches.iter().map(|r| r.id.clone()).collect();
            let sel_index = matches
                .iter()
                .position(|r| r.id == sel)
                .map(|i| i as i32)
                .unwrap_or(-1);
            let set_sel = set_sel_list.clone();
            list_view(matches, |r: &ListItem, _| app_row(r))
                .with_key_selector(|r: &ListItem| r.id.clone())
                .selected_index(sel_index)
                .on_selection_changed(move |i| {
                    if let Some(id) = ids.get(i as usize) {
                        set_sel.call(id.clone());
                    }
                })
                .height(list_height - 60.0)
                .into()
        })
        .loading(caption("loading applications…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let left = crate::fluid_fill(
        10.0,
        vec![
            Element::from(
                body_strong("Application Assignments")
                    .font_family(theme::FONT_DISPLAY)
                    .font_size(16.0)
                    .foreground(theme::TEXT),
            ),
            Element::from(filter_box),
        ],
        list_el,
    );

    let right: Element = if selected.is_empty() {
        body("Select an application to edit its assignments. Save replaces all assignments.")
            .opacity(0.6)
            .wrap()
            .into()
    } else {
        let cur = assignments.data().cloned().unwrap_or_default();
        let editor = assignment_editor(
            "/apps",
            &selected,
            /*with_intent=*/ true,
            &cur,
            assignments.is_loading(),
            &working,
            &set_working,
            &asg_save,
            &do_asg_save,
        );
        scroll_viewer(editor)
            .height((list_height - 20.0).max(160.0))
            .into()
    };

    // Key the editor panel on the selected app so it fully remounts per app — the
    // reactor's in-place reconcile otherwise leaves deep sub-elements stale from the
    // previously-selected app. See the nested-component re-render gotcha.
    grid((
        left.grid_column(0),
        right.with_key(selected.clone()).grid_column(1),
    ))
    .columns([GridLength::Star(1.0), GridLength::Star(1.4)])
    .column_spacing(16.0)
    .margin(Thickness::uniform(16.0))
    .into()
}
