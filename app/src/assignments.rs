//! Intune assignment editors — two variants from one parameterized component:
//! the **policy** editor (include/exclude groups + assignment filters) and the
//! **app** editor (adds per-row install intent). Save replaces all assignments
//! via the normalized `/{resource}/{id}/assignments` endpoint.

use crate::api_client::{api, service_err};
use api_types::Assignment;
use windows_reactor::*;

/// Surfaces whose Core service exposes an assignment *set* method (editor enabled).
pub fn is_assignable(path: &str) -> bool {
    matches!(
        path,
        "/compliance-policies"
            | "/settings-catalog"
            | "/admin-templates"
            | "/endpoint-security"
            | "/platform-scripts"
            | "/shell-scripts"
            | "/apps"
    )
}

/// Surfaces whose detail panel *displays* a resolved assignment set — every
/// editable surface plus read-only-assigned ones (e.g. `/device-configs`, whose
/// Core service exposes a GET but no set). Drives the read-mode assignment table;
/// the editor button stays gated on [`is_assignable`].
pub fn shows_assignments(path: &str) -> bool {
    is_assignable(path) || matches!(path, "/device-configs")
}

fn assign_kind_label(a: &Assignment) -> String {
    let base = match a.kind.as_str() {
        "exclusionGroup" => "exclude group",
        "allDevices" => "all devices",
        "allUsers" => "all users",
        _ => "include group",
    };
    match &a.intent {
        Some(i) if !i.is_empty() => format!("{base} - {i}"),
        _ => base.to_string(),
    }
}

fn mk_add(
    working: &[Assignment],
    set_working: &SetState<Vec<Assignment>>,
    kind: &'static str,
    intent: Option<&'static str>,
    label: &'static str,
) -> Element {
    let working = working.to_vec();
    let set_working = set_working.clone();
    Element::from(button(label).on_click(move || {
        let mut w = working.clone();
        w.push(Assignment {
            kind: kind.to_string(),
            group_id: Some(String::new()),
            group_name: None,
            filter_id: None,
            filter_mode: None,
            intent: intent.map(|s| s.to_string()),
        });
        set_working.call(w);
    }))
}

/// Render the assignment editor. This is a plain render helper, **not** a
/// `component()` — its mutable state (`working`, `save`) is owned by the parent
/// `list_workspace` and threaded in. A nested, conditionally-mounted `component()`
/// doesn't get its own re-render loop wired up in the reactor (state set from
/// inside it never re-renders it), so the editor must live in the parent's hook
/// context. The current server-side set is fetched by the parent and the working
/// set seeded from it there (Save REPLACES all, so the editor starts from the
/// current state). `with_intent=true` -> app variant (per-row install intent);
/// `false` -> policy variant (include/exclude + filter).
#[allow(clippy::too_many_arguments)]
pub fn assignment_editor(
    path: &'static str,
    id: &str,
    with_intent: bool,
    current: &[Assignment],
    loading: bool,
    working: &[Assignment],
    set_working: &SetState<Vec<Assignment>>,
    save: &MutationState<()>,
    do_save: &MutationTrigger<()>,
) -> Element {
    let cur_box: Element = if loading {
        caption("loading current assignments…").opacity(0.6).into()
    } else {
        let reload = {
            let rows = current.to_vec();
            let set_working = set_working.clone();
            button("Reset to current").on_click(move || set_working.call(rows.clone()))
        };
        let summary = if current.is_empty() {
            "No assignments on the server.".to_string()
        } else {
            format!("{} assignment(s) on the server.", current.len())
        };
        hstack((caption(summary).opacity(0.6), Element::from(reload)))
            .spacing(8.0)
            .into()
    };

    let mut editor_rows: Vec<Element> = Vec::new();
    for (i, a) in working.iter().enumerate() {
        let needs_group = a.kind == "group" || a.kind == "exclusionGroup";
        let gbox: Element = if needs_group {
            let working = working.to_vec();
            let set_working = set_working.clone();
            Element::from(
                text_box(a.group_id.clone().unwrap_or_default())
                    .placeholder_text("group id".to_string())
                    .on_text_changed(move |t| {
                        let mut w = working.clone();
                        w[i].group_id = Some(t);
                        set_working.call(w);
                    }),
            )
        } else {
            Element::Empty
        };
        let fbox: Element = {
            let working = working.to_vec();
            let set_working = set_working.clone();
            Element::from(
                text_box(a.filter_id.clone().unwrap_or_default())
                    .placeholder_text("filter id (optional)".to_string())
                    .on_text_changed(move |t: String| {
                        let mut w = working.clone();
                        w[i].filter_id = if t.is_empty() { None } else { Some(t) };
                        set_working.call(w);
                    }),
            )
        };
        let rm = {
            let working = working.to_vec();
            let set_working = set_working.clone();
            button("Remove").on_click(move || {
                let mut w = working.clone();
                w.remove(i);
                set_working.call(w);
            })
        };
        editor_rows.push(
            hstack((
                caption(assign_kind_label(a)).opacity(0.7),
                gbox,
                fbox,
                Element::from(rm),
            ))
            .spacing(8.0)
            .into(),
        );
    }

    let adds: Element = if with_intent {
        hstack((
            mk_add(working, set_working, "group", Some("required"), "+ Required"),
            mk_add(working, set_working, "group", Some("available"), "+ Available"),
            mk_add(working, set_working, "group", Some("uninstall"), "+ Uninstall"),
        ))
        .spacing(8.0)
        .into()
    } else {
        hstack((
            mk_add(working, set_working, "group", None, "+ Include"),
            mk_add(working, set_working, "exclusionGroup", None, "+ Exclude"),
            mk_add(working, set_working, "allDevices", None, "+ All devices"),
            mk_add(working, set_working, "allUsers", None, "+ All users"),
        ))
        .spacing(8.0)
        .into()
    };

    let save_btn = {
        let do_save = do_save.clone();
        let id = id.to_string();
        let working = working.to_vec();
        button(if save.is_loading() { "Saving..." } else { "Save assignments" })
            .accent()
            .enabled(!save.is_loading())
            .on_click(move || {
                let w = working.clone();
                let id = id.clone();
                do_save.fire(move || api().set_assignments(path, &id, w).map_err(service_err));
            })
    };
    let err: Element = save
        .error()
        .map(|e| caption(format!("save failed: {e}")).opacity(0.85).wrap().into())
        .unwrap_or(Element::Empty);

    vstack((
        cur_box,
        body_strong("Editor - Save replaces all assignments"),
        adds,
        vstack(editor_rows).spacing(6.0),
        Element::from(save_btn),
        err,
    ))
    .spacing(10.0)
    .into()
}
