//! Detection & Remediation — the reporting/run face of proactive remediations
//! (device health scripts). Left: the deployed remediation scripts. Right: the
//! selected script's run-summary tiles + a per-device detection/remediation state
//! table, with a gated "Run now" that fires an on-demand remediation on one device.
//!
//! Deploy/CRUD stays on the Remediation Scripts list; this screen is reporting + run.
//! The on-demand run is server-gated like an M14 destructive action (kill switch +
//! DESTRUCTIVE opt-in + confirm) — the client surfaces the server's 403 verbatim.

use api_types::{DeviceRunState, ListItem, RemediationScriptContent};
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

fn state_color(s: &Option<String>) -> Color {
    match s.as_deref().unwrap_or("").to_lowercase().as_str() {
        x if x.contains("success") || x.contains("noissue") || x.contains("remediated") => theme::OK,
        x if x.contains("fail") || x.contains("error") => theme::ERROR,
        x if x.contains("pending") || x.contains("issue") => theme::WARN,
        _ => theme::TEXT_4,
    }
}

fn stat_card(label: &str, value: &str) -> Element {
    border(
        vstack((
            Element::from(body_strong(value.to_string()).font_size(20.0).font_family(theme::FONT_DISPLAY).foreground(theme::TEXT)),
            Element::from(caption(label.to_string()).foreground(theme::TEXT_4).font_family(theme::FONT_UI)),
        ))
        .spacing(1.0),
    )
    .background(theme::SURFACE_2)
    .border_brush(theme::LINE)
    .corner_radius(8.0)
    .padding(Thickness::xy(10.0, 6.0))
    .into()
}

// A read-only code pane: title + monospace, independently-scrolling script text (or a
// placeholder when the body is absent — detection-only or a global/Microsoft-managed script).
fn code_pane(title: &str, body: &Option<String>, h: f64) -> Element {
    let inner: Element = match body {
        Some(t) if !t.trim().is_empty() => scroll_viewer(
            caption(t.clone()).font_family(theme::FONT_MONO).font_size(12.0).foreground(theme::TEXT),
        )
        .height(h)
        .into(),
        _ => caption("(none — detection-only, or a global script whose body Graph doesn't return)")
            .opacity(0.6)
            .wrap()
            .into(),
    };
    border(vstack((Element::from(body_strong(title.to_string()).foreground(theme::TEXT_3)), inner)).spacing(6.0))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(8.0)
        .padding(Thickness::uniform(10.0))
        .into()
}

fn script_row(it: &ListItem) -> Element {
    vstack((
        Element::from(body_strong(it.title.clone()).foreground(theme::TEXT).wrap()),
        Element::from(caption(it.subtitle.clone()).foreground(theme::TEXT_4).font_family(theme::FONT_UI).wrap()),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

fn device_row(d: &DeviceRunState) -> Element {
    let name = d.device_name.clone().unwrap_or_else(|| d.device_id.clone());
    vstack((
        Element::from(body_strong(name).foreground(theme::TEXT).wrap()),
        Element::from(
            hstack((
                pill(&format!("detect: {}", d.detection_state.clone().unwrap_or_else(|| "—".into())), state_color(&d.detection_state)),
                pill(&format!("remediate: {}", d.remediation_state.clone().unwrap_or_else(|| "—".into())), state_color(&d.remediation_state)),
            ))
            .spacing(6.0),
        ),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

pub fn detection_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (selected_script, set_selected_script) = cx.use_state(String::new());
    let (selected_device, set_selected_device) = cx.use_state(String::new());
    let (confirming, set_confirming) = cx.use_state(false);
    let (show_code, set_show_code) = cx.use_state(false); // right pane: run states vs script code
    let (run_target, set_run_target) = cx.use_state::<Option<(String, String)>>(None);
    let (run_tick, bump_run) = cx.use_reducer(0_u64);
    let (data_tick, bump_data) = cx.use_reducer(0_u64);

    let scripts = cx.use_resource(
        |_: u64| api().get_list("/remediation-scripts").map_err(service_err),
        data_tick,
    );
    let summary = cx.use_resource(
        |k: (String, u64)| -> std::result::Result<Vec<ListItem>, String> {
            if k.0.is_empty() {
                return Ok(Vec::new());
            }
            api().remediation_run_summary(&k.0).map_err(service_err)
        },
        (selected_script.clone(), data_tick),
    );
    let states = cx.use_resource(
        |k: (String, u64)| -> std::result::Result<Vec<DeviceRunState>, String> {
            if k.0.is_empty() {
                return Ok(Vec::new());
            }
            api().remediation_device_states(&k.0).map_err(service_err)
        },
        (selected_script.clone(), data_tick),
    );
    let content = cx.use_resource(
        |k: (String, u64)| -> std::result::Result<Option<RemediationScriptContent>, String> {
            if k.0.is_empty() {
                return Ok(None);
            }
            api().remediation_script_content(&k.0).map_err(service_err)
        },
        (selected_script.clone(), data_tick),
    );
    let run = cx.use_resource(
        |k: (Option<(String, String)>, u64)| -> std::result::Result<Option<String>, String> {
            match k.0 {
                None => Ok(None),
                Some((sid, did)) => api().run_remediation(&sid, &did).map(|_| Some("On-demand remediation initiated.".to_string())),
            }
        },
        (run_target.clone(), run_tick),
    );

    // After a successful run, refresh the device states + drop the confirm panel.
    let ran = matches!(run.data(), Some(Some(_)));
    {
        let bump_data = bump_data.clone();
        let set_confirming = set_confirming.clone();
        cx.use_effect(ran, move || {
            if ran {
                bump_data.call(|n| n + 1);
                set_confirming.call(false);
            }
        });
    }

    let list_height = crate::list_height(cx);
    let running = run.is_loading();

    // ── Left: remediation scripts ──────────────────────────────────────────
    let sel_s = selected_script.clone();
    let set_dev_reset = set_selected_device.clone(); // clone for this closure; original is used by the device list
    let script_list: Element = scripts
        .view(move |rows: &Vec<ListItem>| -> Element {
            if rows.is_empty() {
                return body("No remediation scripts (sign in). Deploy/CRUD is on the Remediation Scripts screen.")
                    .opacity(0.6)
                    .wrap()
                    .into();
            }
            let rows = rows.clone();
            let ids: Vec<String> = rows.iter().map(|r| r.id.clone()).collect();
            let idx = ids.iter().position(|i| *i == sel_s).map(|i| i as i32).unwrap_or(-1);
            let set_selected_script = set_selected_script.clone();
            let set_selected_device = set_dev_reset.clone();
            list_view(rows, |r: &ListItem, _| script_row(r))
                .with_key_selector(|r: &ListItem| r.id.clone())
                .selected_index(idx)
                .on_selection_changed(move |i| {
                    if let Some(id) = ids.get(i as usize) {
                        set_selected_script.call(id.clone());
                        set_selected_device.call(String::new());
                    }
                })
                .height((list_height - 40.0).max(140.0))
                .into()
        })
        .loading(caption("loading remediation scripts…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();
    let left = crate::fluid_fill(
        10.0,
        vec![Element::from(body_strong("Remediation scripts").foreground(theme::TEXT_3))],
        script_list,
    );

    // ── Right: tiles + run panel + device-state table ──────────────────────
    let tiles: Element = summary
        .view(|rows: &Vec<ListItem>| -> Element {
            if rows.is_empty() {
                return caption("Select a script to see its run summary.").opacity(0.6).into();
            }
            let cards: Vec<Element> = rows.iter().map(|t| stat_card(&t.title, &t.subtitle)).collect();
            scroll_viewer(hstack(cards).spacing(8.0)).height(70.0).into()
        })
        .loading(caption("loading run summary…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    // Resolve the selected device's name for the confirm prompt.
    let dev_name = states
        .data()
        .and_then(|v| v.iter().find(|d| d.device_id == selected_device))
        .and_then(|d| d.device_name.clone())
        .unwrap_or_else(|| selected_device.clone());

    let run_result: Element = run
        .view(|r: &Option<String>| -> Element {
            match r {
                None => Element::Empty,
                Some(m) => caption(m.clone()).foreground(theme::OK).font_family(theme::FONT_UI).wrap().into(),
            }
        })
        .loading(caption("initiating…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let run_panel: Element = if selected_device.is_empty() {
        caption("Select a device below to run an on-demand remediation.").opacity(0.6).wrap().into()
    } else if !confirming {
        let btn = button("Run remediation now…").accent().enabled(!running).on_click({
            let set_confirming = set_confirming.clone();
            move || set_confirming.call(true)
        });
        vstack((Element::from(btn), run_result)).spacing(6.0).into()
    } else {
        let confirm_btn = button(if running { "Running…" } else { "Confirm run" }).accent().enabled(!running).on_click({
            let set_run_target = set_run_target.clone();
            let bump_run = bump_run.clone();
            let sid = selected_script.clone();
            let did = selected_device.clone();
            move || {
                set_run_target.call(Some((sid.clone(), did.clone())));
                bump_run.call(|n| n + 1);
            }
        });
        let cancel_btn = button("Cancel").enabled(!running).on_click({
            let set_confirming = set_confirming.clone();
            move || set_confirming.call(false)
        });
        border(
            vstack((
                Element::from(
                    caption(format!("Run the remediation script on \"{dev_name}\" now? Server-gated (needs the destructive opt-in)."))
                        .foreground(theme::WARN)
                        .wrap(),
                ),
                Element::from(hstack((Element::from(confirm_btn), Element::from(cancel_btn))).spacing(8.0)),
                run_result,
            ))
            .spacing(8.0),
        )
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(8.0)
        .padding(Thickness::uniform(10.0))
        .into()
    };

    let sel_d = selected_device.clone();
    let device_list: Element = states
        .view(move |rows: &Vec<DeviceRunState>| -> Element {
            if rows.is_empty() {
                return caption("No device run states (select a script; the report bypasses the cache).")
                    .opacity(0.6)
                    .wrap()
                    .into();
            }
            let rows = rows.clone();
            let ids: Vec<String> = rows.iter().map(|r| r.device_id.clone()).collect();
            let idx = ids.iter().position(|i| *i == sel_d).map(|i| i as i32).unwrap_or(-1);
            let set_selected_device = set_selected_device.clone();
            list_view(rows, |d: &DeviceRunState, _| device_row(d))
                .with_key_selector(|d: &DeviceRunState| d.device_id.clone())
                .selected_index(idx)
                .on_selection_changed(move |i| {
                    if let Some(id) = ids.get(i as usize) {
                        set_selected_device.call(id.clone());
                    }
                })
                .height((list_height - 220.0).max(120.0))
                .into()
        })
        .loading(caption("loading device states…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    // View toggle: per-device run states vs the decoded detection/remediation code.
    let toggle_row: Element = {
        let mk = |label: &'static str, code: bool| -> Element {
            let active = show_code == code;
            let s = set_show_code.clone();
            let b = button(label).enabled(!active).on_click(move || s.call(code));
            if active { Element::from(b.accent()) } else { Element::from(b) }
        };
        hstack((mk("Run states", false), mk("Script code", true))).spacing(6.0).into()
    };

    let body_el: Element = if show_code {
        content
            .view(move |c: &Option<RemediationScriptContent>| -> Element {
                match c {
                    None => caption("Select a script to view its detection & remediation code.")
                        .opacity(0.6)
                        .wrap()
                        .into(),
                    Some(rc) => {
                        let ph = ((list_height - 300.0) / 2.0).max(120.0);
                        vstack((
                            code_pane("Detection script", &rc.detection, ph),
                            code_pane("Remediation script", &rc.remediation, ph),
                        ))
                        .spacing(10.0)
                        .into()
                    }
                }
            })
            .loading(caption("decoding script…").opacity(0.6))
            .error(|e| crate::error_box(e))
            .into()
    } else {
        device_list
    };

    let right = crate::fluid_fill(10.0, vec![tiles, run_panel, toggle_row], body_el);

    grid((left.grid_column(0), right.grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(1.4)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
