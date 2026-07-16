//! M16 Foresight — the blast-radius simulator. A form describing a proposed write
//! (verb + path + target object + proposed body) → POST /simulate → a report card:
//! WHO the change newly affects (users/devices), who's no longer targeted, a sample
//! of affected principals, plus conflicts / redundancies / cross-policy impacts.
//!
//! Pure read on the sidecar — no Graph write happens. The same BlastRadiusReport
//! rides on the M6 gate and the M13 inbox; this screen is the standalone pre-flight.

use api_types::{BlastRadiusReport, SimulateRequest};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

// Proposed write verbs the sidecar understands (BlastRadiusSimulator). `assign`
// (the common case) diffs a proposed Assignment[] against the live assignments.
const VERBS: [&str; 4] = ["assign", "update", "create", "delete"];

fn idx_of(v: &str) -> i32 {
    VERBS.iter().position(|x| *x == v).map(|i| i as i32).unwrap_or(0)
}

// severity ∈ info | low | medium | high | critical → pill color.
fn severity_color(sev: &str) -> Color {
    match sev {
        "critical" | "high" => theme::ERROR,
        "medium" => theme::WARN,
        "low" => theme::OK,
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

// One display-number stat card (mirrors screen_cache::stat_card).
fn stat_card(label: &str, value: i32, color: Color) -> Element {
    border(
        vstack((
            Element::from(
                caption(label.to_uppercase()).font_family(theme::FONT_UI).foreground(theme::TEXT_4),
            ),
            Element::from(
                body_strong(value.to_string())
                    .font_size(24.0)
                    .font_family(theme::FONT_DISPLAY)
                    .foreground(color),
            ),
        ))
        .spacing(2.0),
    )
    .background(theme::SURFACE_2)
    .border_brush(theme::LINE)
    .corner_radius(10.0)
    .padding(Thickness::uniform(12.0))
    .into()
}

// A titled block of caption rows; renders Empty when there's nothing to show.
fn block(title: &str, rows: Vec<Element>, color: Color) -> Element {
    if rows.is_empty() {
        return Element::Empty;
    }
    let mut items = vec![Element::from(
        caption(format!("{} ({})", title, rows.len()))
            .font_family(theme::FONT_UI)
            .foreground(color),
    )];
    items.extend(rows);
    vstack(items).spacing(3.0).into()
}

fn report_card(r: &BlastRadiusReport) -> Element {
    let head = hstack((
        pill(&r.severity.to_uppercase(), severity_color(&r.severity)),
        Element::from(body_strong("Blast radius").foreground(theme::TEXT)),
    ))
    .spacing(8.0);

    let summary = body(r.summary.clone()).foreground(theme::TEXT_2).wrap();

    let cards = grid((
        stat_card("Users affected", r.affected_user_count, theme::WARN).grid_column(0),
        stat_card("Devices affected", r.affected_device_count, theme::WARN).grid_column(1),
        stat_card("No longer targeted", r.no_longer_affected_count, theme::TEXT_2).grid_column(2),
    ))
    .columns([GridLength::Star(1.0), GridLength::Star(1.0), GridLength::Star(1.0)])
    .column_spacing(10.0);

    let sample = block(
        "Sample affected",
        r.sample_affected_principals
            .iter()
            .map(|p| {
                let upn = p.upn.clone().map(|u| format!(" · {u}")).unwrap_or_default();
                caption(format!("{} · {}{} — {}", p.r#type, p.display_name, upn, p.reason))
                    .foreground(theme::TEXT_3)
                    .font_family(theme::FONT_MONO)
                    .wrap()
                    .into()
            })
            .collect(),
        theme::TEXT_4,
    );
    let conflicts = block(
        "Conflicts",
        r.conflicts
            .iter()
            .map(|c| caption(format!("{}: {}", c.kind, c.detail)).foreground(theme::ERROR).wrap().into())
            .collect(),
        theme::ERROR,
    );
    let redundancies = block(
        "Redundancies",
        r.redundancies
            .iter()
            .map(|c| caption(format!("{}: {}", c.kind, c.detail)).foreground(theme::WARN).wrap().into())
            .collect(),
        theme::WARN,
    );
    let cross = block(
        "Cross-policy impacts",
        r.cross_policy_impacts
            .iter()
            .map(|c| {
                caption(format!("{}: {}", c.policy_name, c.detail))
                    .foreground(theme::TEXT_3)
                    .wrap()
                    .into()
            })
            .collect(),
        theme::TEXT_4,
    );

    vstack((
        Element::from(head),
        Element::from(summary),
        Element::from(cards),
        sample,
        conflicts,
        redundancies,
        cross,
    ))
    .spacing(12.0)
    .into()
}

pub fn simulator_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (verb, set_verb) = cx.use_state(String::from("assign"));
    let (path, set_path) = cx.use_state(String::new());
    let (object_id, set_object_id) = cx.use_state(String::new());
    let (body_json, set_body_json) = cx.use_state(String::new());
    let (req, set_req) = cx.use_state::<Option<SimulateRequest>>(None);
    let (tick, bump) = cx.use_reducer(0_u64);

    let report = cx.use_resource(
        |k: (Option<SimulateRequest>, u64)| -> std::result::Result<Option<BlastRadiusReport>, String> {
            let (req, _) = k;
            match req {
                None => Ok(None),
                Some(r) => api().simulate(&r).map(Some).map_err(service_err),
            }
        },
        (req.clone(), tick),
    );

    let busy = report.is_loading();
    let list_height = crate::list_height(cx);

    // ── Form ────────────────────────────────────────────────────────────────
    let verb_combo = ComboBox::new(VERBS.iter().map(|s| s.to_string()).collect::<Vec<_>>())
        .header("Verb")
        .selected_index(idx_of(&verb))
        .on_selection_changed({
            let s = set_verb.clone();
            move |i: i32| {
                if let Some(v) = VERBS.get(i as usize) {
                    s.call(v.to_string());
                }
            }
        });
    let path_tb = text_box(path.clone())
        .placeholder_text("Path — e.g. /conditional-access or /device-configs".to_string())
        .on_text_changed({
            let s = set_path.clone();
            move |t| s.call(t)
        });
    let id_tb = text_box(object_id.clone())
        .placeholder_text("Target object id (blank for create)".to_string())
        .on_text_changed({
            let s = set_object_id.clone();
            move |t| s.call(t)
        });
    let body_tb = text_box(body_json.clone())
        .placeholder_text("Proposed body JSON — an Assignment[] for verb=assign".to_string())
        .multiline()
        .height((list_height - 240.0).max(160.0))
        .on_text_changed({
            let s = set_body_json.clone();
            move |t| s.call(t)
        });

    let sim_btn = button(if busy { "Simulating…" } else { "Simulate" })
        .accent()
        .enabled(!busy && !path.is_empty())
        .on_click({
            let set_req = set_req.clone();
            let bump = bump.clone();
            let verb = verb.clone();
            let path = path.clone();
            let object_id = object_id.clone();
            let body_json = body_json.clone();
            move || {
                set_req.call(Some(SimulateRequest {
                    proposer: Some("simulator".to_string()),
                    verb: verb.clone(),
                    path: path.clone(),
                    object_id: if object_id.is_empty() { None } else { Some(object_id.clone()) },
                    body_json: if body_json.is_empty() { None } else { Some(body_json.clone()) },
                }));
                bump.call(|n| n + 1);
            }
        });

    let intro = caption(
        "Pre-flight a proposed write: who it newly affects, before any change. Pure read — \
         nothing is written. Sign in first.",
    )
    .opacity(0.7)
    .wrap();

    let form = vstack((
        Element::from(verb_combo),
        Element::from(path_tb),
        Element::from(id_tb),
        Element::from(body_strong("Proposed body").font_family(theme::FONT_UI).foreground(theme::TEXT_3)),
        Element::from(body_tb),
        Element::from(sim_btn),
        Element::from(intro),
    ))
    .spacing(8.0);

    // ── Report ──────────────────────────────────────────────────────────────
    let report_el: Element = report
        .view(|r: &Option<BlastRadiusReport>| -> Element {
            match r {
                None => caption("Fill in the form and Simulate to see the blast radius.")
                    .opacity(0.6)
                    .wrap()
                    .into(),
                Some(rep) => report_card(rep),
            }
        })
        .loading(caption("resolving the target set over the assignment graph…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();
    let report_scroll = scroll_viewer(report_el).height((list_height - 20.0).max(200.0));

    grid((
        form.grid_column(0),
        Element::from(report_scroll).grid_column(1),
    ))
    .columns([GridLength::Star(1.0), GridLength::Star(1.2)])
    .column_spacing(16.0)
    .margin(Thickness::uniform(16.0))
    .into()
}
