//! Assignment Explorer — the IntuneAssignmentChecker report in every mode the old
//! desktop app offered: all policies, "All Users" / "All Devices" targets,
//! unassigned, empty-group, failed deployments, and a single-group lookup — plus an
//! HTML / CSV report download. Backed by /assignment-explorer[/report] (Core
//! AssignmentCheckerService + AssignmentReportExporter).

use crate::api_client::{api, service_err};
use crate::dialogs::{open_with_default, pick_save_file};
use crate::{error_box, fluid_fill, list_height};
use api_types::ListItem;
use windows_reactor::*;

const MODES: &[(&str, &str)] = &[
    ("all", "All Policies"),
    ("all-users", "All Users"),
    ("all-devices", "All Devices"),
    ("unassigned", "Unassigned"),
    ("empty-groups", "Empty Groups"),
    ("failed", "Failed"),
];

fn row_el(it: &ListItem) -> Element {
    vstack((
        Element::from(body_strong(it.title.clone())),
        Element::from(caption(it.subtitle.clone()).opacity(0.7).wrap()),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

pub(crate) fn assignment_explorer_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (mode, set_mode) = cx.use_state(String::from("all"));
    let (group_draft, set_group_draft) = cx.use_state(String::new());
    let (group, set_group) = cx.use_state(String::new()); // applied group id (group mode)
    let (report, do_report) = cx.use_mutation::<String>(); // returns the written report path

    let rows = cx.use_resource(
        |key: (String, String)| -> std::result::Result<Vec<ListItem>, String> {
            let (m, grp) = key;
            api().assignment_explorer(&m, &grp).map_err(service_err)
        },
        (mode.clone(), group.clone()),
    );
    let list_h = list_height(cx);

    // ── Mode chips ───────────────────────────────────────────────────────────
    let chips: Vec<Element> = MODES
        .iter()
        .map(|(m, label)| {
            let active = mode == *m;
            let target = m.to_string();
            let sm = set_mode.clone();
            let sg = set_group.clone();
            let b = button(*label).enabled(!active).on_click(move || {
                sg.call(String::new()); // leaving any group lookup
                sm.call(target.clone());
            });
            if active {
                Element::from(b.accent())
            } else {
                Element::from(b)
            }
        })
        .collect();
    let chip_row: Element = hstack(chips).spacing(6.0).into();

    // ── Group lookup ─────────────────────────────────────────────────────────
    let group_active = mode == "group";
    let group_input: Element = auto_suggest_box(group_draft.clone())
        .placeholder_text("Group id — Enter to look up".to_string())
        .on_text_changed({
            let s = set_group_draft.clone();
            move |t: String| s.call(t)
        })
        .on_query_submitted({
            let sg = set_group.clone();
            let sm = set_mode.clone();
            move |t: String| {
                if !t.trim().is_empty() {
                    sg.call(t);
                    sm.call("group".to_string());
                }
            }
        })
        .into();
    let group_row: Element = hstack((
        Element::from(caption("Group lookup").opacity(0.6)),
        group_input,
        Element::from(if group_active {
            Element::from(caption(format!("group: {group}")).opacity(0.55))
        } else {
            Element::Empty
        }),
    ))
    .spacing(8.0)
    .into();

    // ── Report export (HTML / CSV) ───────────────────────────────────────────
    let mk_export = |label: &'static str, fmt: &'static str| -> Element {
        let dr = do_report.clone();
        let m = mode.clone();
        let grp = group.clone();
        Element::from(button(label).enabled(!report.is_loading()).on_click(move || {
            let default = format!("assignments-{m}.{fmt}");
            let filter_label = if fmt == "csv" { "CSV" } else { "HTML" };
            if let Some(path) = pick_save_file(
                "Save assignment report",
                &default,
                &[(filter_label, &[fmt]), ("All files", &["*"])],
            ) {
                let m = m.clone();
                let grp = grp.clone();
                let fmt = fmt.to_string();
                dr.fire(move || {
                    let bytes = api().assignment_report(&m, &grp, &fmt).map_err(service_err)?;
                    std::fs::write(&path, &bytes)
                        .map_err(|e| format!("couldn't write {path}: {e}"))?;
                    let _ = open_with_default(&path);
                    Ok(path.clone())
                });
            }
        }))
    };
    let export_row: Element = hstack((
        Element::from(caption("Report").opacity(0.6)),
        mk_export("Export HTML", "html"),
        mk_export("Export CSV", "csv"),
    ))
    .spacing(8.0)
    .into();

    let report_status: Element = if report.is_loading() {
        caption("generating report…").opacity(0.6).into()
    } else if let Some(p) = report.data() {
        caption(format!("Report saved: {p}"))
            .font_family("Consolas")
            .opacity(0.8)
            .wrap()
            .into()
    } else if let Some(e) = report.error() {
        error_box(&format!("report failed: {e}"))
    } else {
        Element::Empty
    };

    // ── The rows ─────────────────────────────────────────────────────────────
    let list_el: Element = rows
        .view(move |xs: &Vec<ListItem>| -> Element {
            if xs.is_empty() {
                return caption("No rows for this mode.").opacity(0.6).into();
            }
            list_view(xs.clone(), |it: &ListItem, _| row_el(it))
                .with_key_selector(|it: &ListItem| it.id.clone())
                .height((list_h - 150.0).max(120.0))
                .into()
        })
        .loading(caption("scanning assignments (this can take a moment)…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    fluid_fill(
        10.0,
        vec![chip_row, group_row, export_row, report_status],
        list_el,
    )
    .margin(Thickness::uniform(16.0))
    .into()
}
