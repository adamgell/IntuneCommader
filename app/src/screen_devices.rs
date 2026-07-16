//! Managed Devices workspace (M14 client) — a master/detail screen over the
//! managedDevice fleet, plus confirm-gated bulk. LEFT: a filterable device list
//! (`/managed-devices`) that in single mode drives the detail pane and in "Select
//! multiple" mode becomes a checkbox column feeding a bulk action bar. RIGHT (keyed on
//! the selected id, so it remounts per device): a STRUCTURED inventory pane — hardware
//! table, compliance/management badges, installed-apps list, and the action-HISTORY
//! table (`/managed-devices/{id}/actions`) — plus the per-device action panel.
//!
//! Non-destructive verbs (`destructive == false`) are one-click buttons that fire with
//! `confirm = None` / `params = None` (so the server's safe DefaultBody applies).
//! Destructive verbs open an INLINE typed-confirm panel (no modal primitive exists in
//! the reactor fork — modelled on the M6 review step): the Confirm button enables only
//! when the typed text matches — the device NAME (case-insensitive) for a single action,
//! the sweep phrase `"{action} {count}"` (case-SENSITIVE) for a bulk action. Server
//! 400/403 (bad confirm / gated / over-cap / permission-check gap) surface VERBATIM.
//!
//! Reactor re-render gotcha (CLAUDE.md): ALL action/confirm/bulk/refresh state lives in
//! THIS workspace's top-level hook context (use_state/use_reducer/use_mutation), never in
//! a nested `component()`, and the right panel is keyed on the selection so it remounts
//! cleanly per device. After a successful action the refresh reducer is bumped (in a
//! `use_effect` on the mutation's success), so the detail + action history — whose
//! server-side cache the write evicted / that read live — re-fetch.

use std::collections::HashSet;

use api_types::{BulkDeviceActionResult, DeviceActionInfo, DeviceActionRecord, ListItem};
use serde_json::Value;
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

// A small status pill (rounded chip). Local copy of the shared kit (each screen keeps
// its own — see screen_autonomy/screen_fleet).
fn pill(text: String, fg: Color, bg: Color) -> Element {
    border(caption(text).foreground(fg).font_family(theme::FONT_UI))
        .background(bg)
        .corner_radius(9.0)
        .padding(Thickness::xy(8.0, 2.0))
        .into()
}

// One device row for the single-select master list: name (bold) + a status pill (the
// list badge) over the subtitle. Mirrors `main.rs::row_view`.
fn device_row(r: &ListItem) -> Element {
    let title_el = body_strong(r.title.clone())
        .font_family(theme::FONT_UI)
        .foreground(theme::TEXT);
    let heading: Element = if let Some(b) = &r.badge {
        hstack((
            Element::from(title_el),
            pill(b.clone(), theme::BRAND_BRIGHT, theme::SURFACE_2),
        ))
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

// One toggle row for "Select multiple" mode: a full-width button prefixed with a
// checkbox glyph, accented when selected. No checkbox primitive exists in the reactor
// fork, so the button IS the checkbox (mirrors screen_autonomy's boolean toggle). The
// closure captures a snapshot of the current selection + the setter; each toggle bumps
// workspace state → re-render → fresh snapshot, so membership stays consistent.
fn bulk_row(
    r: &ListItem,
    checked: bool,
    current: &HashSet<String>,
    set_selected_ids: &SetState<HashSet<String>>,
) -> Element {
    let id = r.id.clone();
    let current = current.clone();
    let set = set_selected_ids.clone();
    let mark = if checked { "☑" } else { "☐" };
    let b = button(format!("{mark}  {}", r.title)).on_click(move || {
        let mut next = current.clone();
        if !next.remove(&id) {
            next.insert(id.clone());
        }
        set.call(next);
    });
    let b = if checked { b.accent() } else { b };
    Element::from(b)
}

// One label/value row for the hardware table. Missing values render as "—".
fn kv(label: &str, value: Option<String>) -> Element {
    let val = value.filter(|s| !s.is_empty()).unwrap_or_else(|| "—".to_string());
    grid((
        Element::from(
            caption(label.to_string())
                .foreground(theme::TEXT_4)
                .font_family(theme::FONT_UI),
        )
        .grid_column(0),
        Element::from(
            body(val)
                .foreground(theme::TEXT_2)
                .font_family(theme::FONT_UI)
                .wrap(),
        )
        .grid_column(1),
    ))
    .columns([GridLength::Star(1.0), GridLength::Star(2.0)])
    .column_spacing(10.0)
    .into()
}

pub fn devices_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (filter, set_filter) = cx.use_state(String::new());
    let (selected, set_selected) = cx.use_state(String::new());
    // Bumped after a successful action so the detail (cache evicted server-side) + the
    // live action history re-fetch.
    let (refresh, bump) = cx.use_reducer(0_u64);

    // Single-action inline typed-confirm (device name). Lifted to the workspace context.
    let (confirm_action, set_confirm_action) = cx.use_state(String::new());
    let (confirm_text, set_confirm_text) = cx.use_state(String::new());

    // Bulk mode + multi-selection + bulk inline typed-confirm (sweep phrase).
    let (bulk_mode, set_bulk_mode) = cx.use_state(false);
    let (selected_ids, set_selected_ids) = cx.use_state(HashSet::<String>::new());
    let (bulk_confirm_action, set_bulk_confirm_action) = cx.use_state(String::new());
    let (bulk_confirm_text, set_bulk_confirm_text) = cx.use_state(String::new());

    // The single-action mutation (one-click + confirmed-destructive) and the bulk-action
    // mutation (returns the per-device outcomes for the results table).
    let (action, do_action) = cx.use_mutation::<()>();
    let (bulk_action, do_bulk) = cx.use_mutation::<Vec<BulkDeviceActionResult>>();

    // On a successful single action: refresh, close any confirm panel, reset the mutation.
    let action_done = action.data().is_some();
    {
        let bump = bump.clone();
        let set_confirm_action = set_confirm_action.clone();
        let set_confirm_text = set_confirm_text.clone();
        let do_action = do_action.clone();
        cx.use_effect(action_done, move || {
            if action_done {
                bump.call(|n| n + 1);
                set_confirm_action.call(String::new());
                set_confirm_text.call(String::new());
                do_action.reset();
            }
        });
    }

    // On a successful bulk action: refresh + clear the selection + close the bulk confirm
    // panel. The mutation's data (per-device outcomes) is kept for the results table until
    // the operator dismisses it.
    let bulk_done = bulk_action.data().is_some();
    {
        let bump = bump.clone();
        let set_selected_ids = set_selected_ids.clone();
        let set_bulk_confirm_action = set_bulk_confirm_action.clone();
        let set_bulk_confirm_text = set_bulk_confirm_text.clone();
        cx.use_effect(bulk_done, move || {
            if bulk_done {
                bump.call(|n| n + 1);
                set_selected_ids.call(HashSet::new());
                set_bulk_confirm_action.call(String::new());
                set_bulk_confirm_text.call(String::new());
            }
        });
    }

    // Reset the single confirm panel + error when the selection changes (no stale leak).
    {
        let set_confirm_action = set_confirm_action.clone();
        let set_confirm_text = set_confirm_text.clone();
        let do_action = do_action.clone();
        cx.use_effect(selected.clone(), move || {
            set_confirm_action.call(String::new());
            set_confirm_text.call(String::new());
            do_action.reset();
        });
    }

    let devices = cx.use_resource(
        |_: u64| api().get_list("/managed-devices").map_err(service_err),
        refresh,
    );
    let detail = cx.use_resource(
        |key: (String, u64)| {
            let (id, _) = key;
            if id.is_empty() {
                return Ok(None);
            }
            api().get_detail("/managed-devices", &id).map(Some).map_err(service_err)
        },
        (selected.clone(), refresh),
    );
    // Live action history for the selected device (re-fetches on refresh bump).
    let history = cx.use_resource(
        |key: (String, u64)| {
            let (id, _) = key;
            if id.is_empty() {
                return Ok(Vec::new());
            }
            api().get_device_action_history(&id).map_err(service_err)
        },
        (selected.clone(), refresh),
    );
    // The verb catalog is tenant-agnostic; load it once.
    let actions = cx.use_resource(|_: u64| api().list_device_actions().map_err(service_err), 0_u64);

    let list_height = crate::list_height(cx);

    // Device name for the single typed-confirm token (server compares case-insensitively).
    let selected_name: String = devices
        .data()
        .and_then(|all| all.iter().find(|r| r.id == selected).map(|r| r.title.clone()))
        .unwrap_or_default();
    let total_rows = devices.data().map(|v| v.len()).unwrap_or(0);

    // ── LEFT: count + mode toggle + filter box + master list / bulk column ────
    let filter_box = auto_suggest_box(filter.clone())
        .placeholder_text("Filter devices…".to_string())
        .on_text_changed({
            let s = set_filter.clone();
            move |t| s.call(t)
        });

    let mode_btn = {
        let set_bulk_mode = set_bulk_mode.clone();
        let set_selected_ids = set_selected_ids.clone();
        let on = bulk_mode;
        let b = button(if on { "Done selecting" } else { "Select multiple" }).on_click(move || {
            set_selected_ids.call(HashSet::new());
            set_bulk_mode.call(!on);
        });
        if on { b.accent() } else { b }
    };
    let header_row = hstack((
        Element::from(
            caption(format!("{total_rows} devices"))
                .foreground(theme::TEXT_3)
                .font_family(theme::FONT_UI),
        ),
        Element::from(mode_btn),
    ))
    .spacing(10.0);

    let filter_lc = filter.to_lowercase();
    let list_el: Element = if bulk_mode {
        // "Select multiple" — a scrollable checkbox column of toggle rows.
        let current = selected_ids.clone();
        let set_ids = set_selected_ids.clone();
        devices
            .view(move |all: &Vec<ListItem>| -> Element {
                let matches: Vec<&ListItem> = all
                    .iter()
                    .filter(|r| {
                        filter_lc.is_empty()
                            || r.title.to_lowercase().contains(&filter_lc)
                            || r.subtitle.to_lowercase().contains(&filter_lc)
                    })
                    .collect();
                if matches.is_empty() {
                    return body("No devices match.").opacity(0.6).wrap().into();
                }
                let rows: Vec<Element> = matches
                    .into_iter()
                    .take(500)
                    .map(|r| bulk_row(r, current.contains(&r.id), &current, &set_ids))
                    .collect();
                scroll_viewer(vstack(rows).spacing(2.0))
                    .height(list_height - 110.0)
                    .into()
            })
            .loading(caption("loading devices…").opacity(0.6))
            .error(|e| crate::error_box(e))
            .into()
    } else {
        // Single-select master list driving the detail pane.
        let sel = selected.clone();
        let set_sel_list = set_selected.clone();
        devices
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
                    return body("No devices. Sign in (top right) if this needs Graph data.")
                        .opacity(0.6)
                        .wrap()
                        .into();
                }
                let ids: Vec<String> = matches.iter().map(|r| r.id.clone()).collect();
                let sel_index =
                    matches.iter().position(|r| r.id == sel).map(|i| i as i32).unwrap_or(-1);
                let set_sel = set_sel_list.clone();
                list_view(matches, |r: &ListItem, _| device_row(r))
                    .with_key_selector(|r: &ListItem| r.id.clone())
                    .selected_index(sel_index)
                    .on_selection_changed(move |i| {
                        if let Some(id) = ids.get(i as usize) {
                            set_sel.call(id.clone());
                        }
                    })
                    .height(list_height - 110.0)
                    .into()
            })
            .loading(caption("loading devices…").opacity(0.6))
            .error(|e| crate::error_box(e))
            .into()
    };

    let left = crate::fluid_fill(
        10.0,
        vec![Element::from(header_row), Element::from(filter_box)],
        list_el,
    );

    // ── RIGHT: bulk section (bulk mode) + structured detail (keyed on selection) ──
    let right: Element = {
        // Bulk section: the action bar + inline sweep confirm + per-device results table.
        let bulk_section: Element = if bulk_mode {
            let count = selected_ids.len();
            let ids: Vec<String> = {
                let mut v: Vec<String> = selected_ids.iter().cloned().collect();
                v.sort();
                v
            };
            let bar: Element = actions
                .view({
                    let do_bulk = do_bulk.clone();
                    let ids = ids.clone();
                    let running = bulk_action.is_loading();
                    let bulk_confirm_action = bulk_confirm_action.clone();
                    let bulk_confirm_text = bulk_confirm_text.clone();
                    let set_bulk_confirm_action = set_bulk_confirm_action.clone();
                    let set_bulk_confirm_text = set_bulk_confirm_text.clone();
                    move |verbs: &Vec<DeviceActionInfo>| -> Element {
                        bulk_bar(
                            verbs,
                            &ids,
                            running,
                            &do_bulk,
                            &bulk_confirm_action,
                            &bulk_confirm_text,
                            &set_bulk_confirm_action,
                            &set_bulk_confirm_text,
                        )
                    }
                })
                .loading(caption("loading actions…").opacity(0.6))
                .error(|e| crate::error_box(e))
                .into();

            let bulk_err: Element = bulk_action
                .error()
                .map(|e| crate::error_box(&format!("Bulk action failed: {e}")))
                .unwrap_or(Element::Empty);

            let results: Element = bulk_action
                .data()
                .map(|rows| results_table(rows, &do_bulk))
                .unwrap_or(Element::Empty);

            let hdr = body_strong(format!("Bulk — {count} selected"))
                .font_family(theme::FONT_UI)
                .font_size(15.0)
                .foreground(theme::TEXT);

            border(
                vstack((Element::from(hdr), bar, bulk_err, results)).spacing(10.0),
            )
            .border_brush(theme::LINE)
            .background(theme::SURFACE)
            .corner_radius(6.0)
            .padding(Thickness::uniform(12.0))
            .into()
        } else {
            Element::Empty
        };

        // Structured detail for the single-selected device (hardware + badges + apps +
        // action history + the per-device action panel).
        let detail_section: Element = if selected.is_empty() {
            if bulk_mode {
                Element::Empty
            } else {
                body("Select a device to see its inventory and available actions.")
                    .opacity(0.6)
                    .wrap()
                    .into()
            }
        } else {
            let inventory: Element = detail
                .view(move |json: &Option<String>| -> Element {
                    match json.as_deref().and_then(|j| serde_json::from_str::<Value>(j).ok()) {
                        Some(v) => detail_pane(&v),
                        None => caption("loading…").opacity(0.6).into(),
                    }
                })
                .loading(caption("loading inventory…").opacity(0.6))
                .error(|e| crate::error_box(e))
                .into();

            let history_el: Element = history
                .view(|rows: &Vec<DeviceActionRecord>| -> Element { history_table(rows) })
                .loading(caption("loading history…").opacity(0.6))
                .error(|e| crate::error_box(e))
                .into();

            let panel: Element = actions
                .view({
                    let do_action = do_action.clone();
                    let confirm_action = confirm_action.clone();
                    let confirm_text = confirm_text.clone();
                    let set_confirm_action = set_confirm_action.clone();
                    let set_confirm_text = set_confirm_text.clone();
                    let selected = selected.clone();
                    let selected_name = selected_name.clone();
                    let running = action.is_loading();
                    move |verbs: &Vec<DeviceActionInfo>| -> Element {
                        action_panel(
                            verbs,
                            &selected,
                            &selected_name,
                            running,
                            &do_action,
                            &confirm_action,
                            &confirm_text,
                            &set_confirm_action,
                            &set_confirm_text,
                        )
                    }
                })
                .loading(caption("loading actions…").opacity(0.6))
                .error(|e| crate::error_box(e))
                .into();

            let action_err: Element = action
                .error()
                .map(|e| crate::error_box(&format!("Action failed: {e}")))
                .unwrap_or(Element::Empty);

            let actions_header = body_strong("Actions")
                .font_family(theme::FONT_UI)
                .font_size(15.0)
                .foreground(theme::TEXT);
            let history_header = body_strong("Action history")
                .font_family(theme::FONT_UI)
                .font_size(15.0)
                .foreground(theme::TEXT);

            vstack(vec![
                inventory,
                Element::from(actions_header),
                panel,
                action_err,
                Element::from(history_header),
                history_el,
            ])
            .spacing(12.0)
            .into()
        };

        scroll_viewer(
            vstack((bulk_section, detail_section))
                .spacing(14.0)
                .margin(Thickness::uniform(4.0)),
        )
        .height(list_height - 20.0)
        .into()
    };

    // Key the right pane on the selection AND bulk mode so it remounts cleanly on either.
    let right_key = format!("{selected}|{bulk_mode}");
    grid((left.grid_column(0), right.with_key(right_key).grid_column(1)))
        .columns([GridLength::Star(1.4), GridLength::Star(1.0)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}

// The structured inventory pane: device name + status badges, then a hardware
// label/value table, then the installed-apps list (from Graph `detectedApps` when the
// detail projection carries it; a note otherwise).
fn detail_pane(v: &Value) -> Element {
    fn s(v: &Value, key: &str) -> Option<String> {
        v.get(key).and_then(|x| x.as_str()).map(|x| x.to_string())
    }
    fn b(v: &Value, key: &str) -> Option<bool> {
        v.get(key).and_then(|x| x.as_bool())
    }
    // Storage/size come back as either a JSON number or a numeric string.
    fn num(v: &Value, key: &str) -> Option<f64> {
        v.get(key).and_then(|x| x.as_f64().or_else(|| x.as_str().and_then(|s| s.parse().ok())))
    }
    fn gb(bytes: Option<f64>) -> Option<String> {
        bytes.filter(|n| *n > 0.0).map(|n| format!("{:.1} GB", n / 1_000_000_000.0))
    }

    let name = s(v, "deviceName").unwrap_or_else(|| "(unnamed device)".to_string());
    let title = body_strong(name)
        .font_family(theme::FONT_UI)
        .font_size(18.0)
        .foreground(theme::TEXT);

    // Badges: compliance / management / ownership + encryption/supervision flags.
    let mut badges: Vec<Element> = Vec::new();
    if let Some(c) = s(v, "complianceState") {
        let color = if c.eq_ignore_ascii_case("compliant") { theme::OK } else { theme::WARN };
        badges.push(pill(c, color, theme::SURFACE_2));
    }
    if let Some(m) = s(v, "managementState") {
        badges.push(pill(m, theme::TEXT_2, theme::SURFACE_2));
    }
    if let Some(o) = s(v, "managedDeviceOwnerType") {
        badges.push(pill(o, theme::TEXT_2, theme::SURFACE_2));
    }
    if b(v, "isEncrypted") == Some(true) {
        badges.push(pill("encrypted".to_string(), theme::OK, theme::SURFACE_2));
    }
    if b(v, "isSupervised") == Some(true) {
        badges.push(pill("supervised".to_string(), theme::TEXT_2, theme::SURFACE_2));
    }
    // jailBroken is a string ("True"/"False"/"Unknown") on managedDevice.
    if s(v, "jailBroken").as_deref().map(|j| j.eq_ignore_ascii_case("true")) == Some(true) {
        badges.push(pill("jailbroken".to_string(), theme::ERROR, theme::SURFACE_2));
    }
    let badge_row: Element = if badges.is_empty() {
        Element::Empty
    } else {
        hstack(badges).spacing(6.0).into()
    };

    // Hardware table (Vec form — well past tuple arity).
    let os_line = {
        let os = s(v, "operatingSystem");
        let ver = s(v, "osVersion");
        match (os, ver) {
            (Some(o), Some(x)) => Some(format!("{o} {x}")),
            (Some(o), None) => Some(o),
            (None, Some(x)) => Some(x),
            _ => None,
        }
    };
    let storage_line = match (gb(num(v, "freeStorageSpaceInBytes")), gb(num(v, "totalStorageSpaceInBytes"))) {
        (Some(f), Some(t)) => Some(format!("{f} of {t}")),
        (Some(f), None) => Some(f),
        _ => None,
    };
    let hardware = vstack(vec![
        kv("Manufacturer", s(v, "manufacturer")),
        kv("Model", s(v, "model")),
        kv("Serial", s(v, "serialNumber")),
        kv("OS", os_line),
        kv("Storage free", storage_line),
        kv("Wi-Fi MAC", s(v, "wiFiMacAddress")),
        kv("Ethernet MAC", s(v, "ethernetMacAddress")),
        kv("IMEI", s(v, "imei")),
        kv("Primary user", s(v, "userPrincipalName").or_else(|| s(v, "userDisplayName"))),
        kv("Category", s(v, "deviceCategoryDisplayName")),
        kv("Enrolled", s(v, "enrolledDateTime")),
        kv("Last sync", s(v, "lastSyncDateTime")),
    ])
    .spacing(4.0);

    // Installed apps (present only when the detail projection expands `detectedApps`).
    let apps_el: Element = match v.get("detectedApps").and_then(|a| a.as_array()) {
        Some(apps) if !apps.is_empty() => {
            let mut rows: Vec<Element> = apps
                .iter()
                .take(200)
                .map(|a| {
                    let dn = a.get("displayName").and_then(|x| x.as_str()).unwrap_or("(app)");
                    let ver = a.get("version").and_then(|x| x.as_str()).unwrap_or("");
                    Element::from(
                        caption(if ver.is_empty() {
                            dn.to_string()
                        } else {
                            format!("{dn}  ·  {ver}")
                        })
                        .foreground(theme::TEXT_2)
                        .font_family(theme::FONT_UI),
                    )
                })
                .collect();
            rows.insert(
                0,
                Element::from(
                    body_strong(format!("Installed apps ({})", apps.len()))
                        .font_family(theme::FONT_UI)
                        .font_size(15.0)
                        .foreground(theme::TEXT),
                ),
            );
            vstack(rows).spacing(3.0).into()
        }
        _ => Element::Empty,
    };

    let hw_header = body_strong("Hardware")
        .font_family(theme::FONT_UI)
        .font_size(15.0)
        .foreground(theme::TEXT);

    vstack(vec![
        Element::from(title),
        badge_row,
        Element::from(hw_header),
        Element::from(hardware),
        apps_el,
    ])
    .spacing(10.0)
    .into()
}

// The action-history table: one row per Graph deviceActionResult (verb · state ·
// requested → completed). Empty state when the device has no recorded actions.
fn history_table(rows: &[DeviceActionRecord]) -> Element {
    if rows.is_empty() {
        return caption("No recorded actions for this device.").opacity(0.6).into();
    }
    let els: Vec<Element> = rows
        .iter()
        .map(|r| {
            let state = r.state.clone().unwrap_or_else(|| "—".to_string());
            let state_color = match state.to_lowercase().as_str() {
                "done" | "succeeded" | "success" => theme::OK,
                "failed" | "error" => theme::ERROR,
                _ => theme::WARN,
            };
            let when = r
                .completed_utc
                .clone()
                .or_else(|| r.requested_utc.clone())
                .unwrap_or_default();
            grid((
                Element::from(
                    body(r.action.clone())
                        .foreground(theme::TEXT_2)
                        .font_family(theme::FONT_UI),
                )
                .grid_column(0),
                pill(state, state_color, theme::SURFACE_2).grid_column(1),
                Element::from(
                    caption(when)
                        .foreground(theme::TEXT_4)
                        .font_family(theme::FONT_UI),
                )
                .grid_column(2),
            ))
            .columns([GridLength::Star(1.2), GridLength::Auto, GridLength::Star(1.4)])
            .column_spacing(10.0)
            .into()
        })
        .collect();
    vstack(els).spacing(5.0).into()
}

// Per-device outcomes from a bulk action (POST result), plus a Dismiss button.
fn results_table(rows: &[BulkDeviceActionResult], do_bulk: &MutationTrigger<Vec<BulkDeviceActionResult>>) -> Element {
    let ok = rows.iter().filter(|r| r.ok).count();
    let head = caption(format!("Result: {ok}/{} succeeded", rows.len()))
        .foreground(theme::TEXT_3)
        .font_family(theme::FONT_UI);
    let mut els: Vec<Element> = vec![Element::from(head)];
    for r in rows {
        let (label, color) = if r.ok {
            ("ok".to_string(), theme::OK)
        } else {
            (r.error.clone().unwrap_or_else(|| "failed".to_string()), theme::ERROR)
        };
        els.push(
            grid((
                Element::from(
                    caption(r.device_id.clone())
                        .foreground(theme::TEXT_4)
                        .font_family(theme::FONT_MONO),
                )
                .grid_column(0),
                Element::from(
                    caption(label)
                        .foreground(color)
                        .font_family(theme::FONT_UI)
                        .wrap(),
                )
                .grid_column(1),
            ))
            .columns([GridLength::Star(1.0), GridLength::Star(1.4)])
            .column_spacing(10.0)
            .into(),
        );
    }
    let dismiss = {
        let do_bulk = do_bulk.clone();
        button("Dismiss").on_click(move || do_bulk.reset())
    };
    els.push(Element::from(dismiss));
    border(vstack(els).spacing(4.0))
        .border_brush(theme::LINE)
        .background(theme::SURFACE_2)
        .corner_radius(4.0)
        .padding(Thickness::uniform(10.0))
        .into()
}

// The single-device action panel: one-click buttons for reversible verbs, plus an inline
// typed-confirm panel for whichever destructive verb is currently open. A plain render
// helper (state lives in the parent's hook context — see the re-render note above),
// threaded the parent's mutation + setters.
#[allow(clippy::too_many_arguments)]
fn action_panel(
    verbs: &[DeviceActionInfo],
    device_id: &str,
    device_name: &str,
    running: bool,
    do_action: &MutationTrigger<()>,
    confirm_action: &str,
    confirm_text: &str,
    set_confirm_action: &SetState<String>,
    set_confirm_text: &SetState<String>,
) -> Element {
    if verbs.is_empty() {
        return caption("No device actions available.").opacity(0.6).into();
    }

    // Non-destructive: one-click buttons. confirm=None / params=None → server DefaultBody.
    let safe_btns: Vec<Element> = verbs
        .iter()
        .filter(|v| !v.destructive)
        .map(|v| {
            let id = device_id.to_string();
            let action_id = v.id.clone();
            let do_action = do_action.clone();
            Element::from(
                button(v.display_name.clone()).enabled(!running).on_click(move || {
                    let id = id.clone();
                    let action_id = action_id.clone();
                    do_action.fire(move || api().run_device_action(&id, &action_id, None, None));
                }),
            )
        })
        .collect();
    let safe_row: Element = if safe_btns.is_empty() {
        Element::Empty
    } else {
        hstack(safe_btns).spacing(8.0).into()
    };

    // Destructive: a button that opens its inline typed-confirm panel.
    let dest_btns: Vec<Element> = verbs
        .iter()
        .filter(|v| v.destructive)
        .map(|v| {
            let action_id = v.id.clone();
            let set_confirm_action = set_confirm_action.clone();
            let set_confirm_text = set_confirm_text.clone();
            let open = confirm_action == v.id;
            let mut b = button(v.display_name.clone()).enabled(!running).on_click(move || {
                set_confirm_text.call(String::new());
                set_confirm_action.call(action_id.clone());
            });
            if open {
                b = b.accent();
            }
            Element::from(b)
        })
        .collect();
    let dest_row: Element = if dest_btns.is_empty() {
        Element::Empty
    } else {
        vstack((
            Element::from(
                caption("Destructive")
                    .foreground(theme::TEXT_4)
                    .font_family(theme::FONT_UI),
            ),
            Element::from(hstack(dest_btns).spacing(8.0)),
        ))
        .spacing(4.0)
        .into()
    };

    // The inline typed-confirm panel for the currently-open destructive verb (modelled on
    // the M6 review step — inline, since there's no modal primitive).
    let confirm_panel: Element = if confirm_action.is_empty() {
        Element::Empty
    } else {
        let verb_label = verbs
            .iter()
            .find(|v| v.id == confirm_action)
            .map(|v| v.display_name.clone())
            .unwrap_or_else(|| confirm_action.to_string());
        // Server compares the device-name token case-insensitively.
        let matched = !device_name.is_empty() && confirm_text.eq_ignore_ascii_case(device_name);

        let prompt = caption(format!(
            "{verb_label} is irreversible. Type the device name \"{device_name}\" to confirm."
        ))
        .foreground(theme::WARN)
        .wrap();

        let input = text_box(confirm_text.to_string()).on_text_changed({
            let set_confirm_text = set_confirm_text.clone();
            move |t| set_confirm_text.call(t)
        });

        let confirm_btn = button(if running { "Working…" } else { "Confirm" })
            .accent()
            .enabled(matched && !running)
            .on_click({
                let id = device_id.to_string();
                let action_id = confirm_action.to_string();
                let name = device_name.to_string();
                let do_action = do_action.clone();
                move || {
                    let id = id.clone();
                    let action_id = action_id.clone();
                    let name = name.clone();
                    do_action.fire(move || api().run_device_action(&id, &action_id, Some(&name), None));
                }
            });
        let cancel_btn = button("Cancel").enabled(!running).on_click({
            let set_confirm_action = set_confirm_action.clone();
            let set_confirm_text = set_confirm_text.clone();
            move || {
                set_confirm_action.call(String::new());
                set_confirm_text.call(String::new());
            }
        });

        border(
            vstack((
                Element::from(prompt),
                Element::from(input),
                Element::from(
                    hstack((Element::from(confirm_btn), Element::from(cancel_btn))).spacing(8.0),
                ),
            ))
            .spacing(8.0),
        )
        .border_brush(theme::LINE)
        .background(theme::SURFACE_2)
        .corner_radius(4.0)
        .padding(Thickness::uniform(12.0))
        .into()
    };

    vstack((safe_row, dest_row, confirm_panel)).spacing(10.0).into()
}

// The bulk action bar: reversible verbs fire immediately across the selection; destructive
// verbs open an inline confirm requiring the case-SENSITIVE sweep phrase "{verb} {count}".
#[allow(clippy::too_many_arguments)]
fn bulk_bar(
    verbs: &[DeviceActionInfo],
    ids: &[String],
    running: bool,
    do_bulk: &MutationTrigger<Vec<BulkDeviceActionResult>>,
    confirm_action: &str,
    confirm_text: &str,
    set_confirm_action: &SetState<String>,
    set_confirm_text: &SetState<String>,
) -> Element {
    if ids.is_empty() {
        return caption("Select one or more devices to act on.").opacity(0.6).into();
    }
    let count = ids.len();

    let safe_btns: Vec<Element> = verbs
        .iter()
        .filter(|v| !v.destructive)
        .map(|v| {
            let action_id = v.id.clone();
            let ids = ids.to_vec();
            let do_bulk = do_bulk.clone();
            Element::from(
                button(v.display_name.clone()).enabled(!running).on_click(move || {
                    let action_id = action_id.clone();
                    let ids = ids.clone();
                    do_bulk.fire(move || api().post_bulk_device_action(&action_id, &ids, None));
                }),
            )
        })
        .collect();
    let safe_row: Element = if safe_btns.is_empty() {
        Element::Empty
    } else {
        hstack(safe_btns).spacing(8.0).into()
    };

    let dest_btns: Vec<Element> = verbs
        .iter()
        .filter(|v| v.destructive)
        .map(|v| {
            let action_id = v.id.clone();
            let set_confirm_action = set_confirm_action.clone();
            let set_confirm_text = set_confirm_text.clone();
            let open = confirm_action == v.id;
            let mut b = button(v.display_name.clone()).enabled(!running).on_click(move || {
                set_confirm_text.call(String::new());
                set_confirm_action.call(action_id.clone());
            });
            if open {
                b = b.accent();
            }
            Element::from(b)
        })
        .collect();
    let dest_row: Element = if dest_btns.is_empty() {
        Element::Empty
    } else {
        vstack((
            Element::from(
                caption("Destructive (bulk)")
                    .foreground(theme::TEXT_4)
                    .font_family(theme::FONT_UI),
            ),
            Element::from(hstack(dest_btns).spacing(8.0)),
        ))
        .spacing(4.0)
        .into()
    };

    let confirm_panel: Element = if confirm_action.is_empty() {
        Element::Empty
    } else {
        // Case-SENSITIVE sweep phrase — distinct from the single-action device-name token.
        let token = format!("{confirm_action} {count}");
        let matched = confirm_text == token;
        let verb_label = verbs
            .iter()
            .find(|v| v.id == confirm_action)
            .map(|v| v.display_name.clone())
            .unwrap_or_else(|| confirm_action.to_string());

        let prompt = caption(format!(
            "{verb_label} on {count} device(s) is irreversible. Type \"{token}\" to confirm."
        ))
        .foreground(theme::WARN)
        .wrap();

        let input = text_box(confirm_text.to_string()).on_text_changed({
            let set_confirm_text = set_confirm_text.clone();
            move |t| set_confirm_text.call(t)
        });

        let confirm_btn = button(if running { "Working…" } else { "Confirm bulk" })
            .accent()
            .enabled(matched && !running)
            .on_click({
                let action_id = confirm_action.to_string();
                let ids = ids.to_vec();
                let token = token.clone();
                let do_bulk = do_bulk.clone();
                move || {
                    let action_id = action_id.clone();
                    let ids = ids.clone();
                    let token = token.clone();
                    do_bulk.fire(move || {
                        api().post_bulk_device_action(&action_id, &ids, Some(&token))
                    });
                }
            });
        let cancel_btn = button("Cancel").enabled(!running).on_click({
            let set_confirm_action = set_confirm_action.clone();
            let set_confirm_text = set_confirm_text.clone();
            move || {
                set_confirm_action.call(String::new());
                set_confirm_text.call(String::new());
            }
        });

        border(
            vstack((
                Element::from(prompt),
                Element::from(input),
                Element::from(
                    hstack((Element::from(confirm_btn), Element::from(cancel_btn))).spacing(8.0),
                ),
            ))
            .spacing(8.0),
        )
        .border_brush(theme::LINE)
        .background(theme::SURFACE_2)
        .corner_radius(4.0)
        .padding(Thickness::uniform(12.0))
        .into()
    };

    vstack((safe_row, dest_row, confirm_panel)).spacing(10.0).into()
}
