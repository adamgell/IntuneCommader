//! Diagnostics workspace: Sysmon (System Monitor) event viewer.
//!
//! LOCAL Windows diagnostics — pure Rust, NO sidecar / NO auth. Mirrors the
//! `dsregcmd` workspace idiom: shell a process on Reactor's background fetcher
//! thread (`use_resource`), project the output into a comparable row type, and
//! render a refreshable virtualized list.
//!
//! Data source: the Sysmon operational channel
//! `Microsoft-Windows-Sysmon/Operational`, read via PowerShell's `Get-WinEvent`.
//! Sysmon is an optional Sysinternals tool, so on most boxes the channel is
//! absent — that is the NORMAL empty state (a clear caption), not an error.

use serde_json::Value;
use windows_reactor::*;

// A lean, comparable projection of one Sysmon event. `use_resource` requires
// `PartialEq`, and we only carry the few fields the row renders.
#[derive(Clone, PartialEq)]
struct Row {
    id: u64, // list index — stable key
    time: String,
    event_id: i64,
    event_type: String,
    detail: String,
}

// Map the common Sysmon event IDs to a human label; unknown IDs fall back to a
// generic "Event N". See the Sysmon schema for the full catalog.
fn event_type(id: i64) -> String {
    match id {
        1 => "Process Create".to_string(),
        2 => "File creation time changed".to_string(),
        3 => "Network Connection".to_string(),
        5 => "Process Terminated".to_string(),
        7 => "Image Loaded".to_string(),
        8 => "CreateRemoteThread".to_string(),
        11 => "File Create".to_string(),
        12 | 13 | 14 => "Registry".to_string(),
        22 => "DNS Query".to_string(),
        other => format!("Event {other}"),
    }
}

// Shell `Get-WinEvent` for the Sysmon operational channel on the background
// fetcher thread. Returns Ok(empty) for the "not installed / channel missing"
// case (non-zero exit, empty stdout, or unparseable JSON) so the view renders a
// clear caption rather than an error box. Genuine spawn failures still surface
// as Err.
fn run_sysmon() -> std::result::Result<Vec<Row>, String> {
    let out = std::process::Command::new("powershell")
        .args([
            "-NoProfile",
            "-Command",
            "Get-WinEvent -LogName 'Microsoft-Windows-Sysmon/Operational' -MaxEvents 200 \
             -ErrorAction Stop | Select-Object @{n='t';e={$_.TimeCreated.ToString('s')}}, \
             Id, @{n='msg';e={$_.Message}} | ConvertTo-Json -Depth 3",
        ])
        .output()
        .map_err(|e| format!("couldn't run powershell: {e}"))?;

    // Sysmon not installed / channel unavailable: Get-WinEvent throws, so the
    // command exits non-zero and emits no JSON. Treat as the empty state.
    if !out.status.success() {
        return Ok(Vec::new());
    }
    let text = String::from_utf8_lossy(&out.stdout);
    let text = text.trim();
    if text.is_empty() {
        return Ok(Vec::new());
    }

    // ConvertTo-Json emits a bare object for a single event and an array for
    // many; an unparseable payload also means "no usable events" → empty state.
    let parsed: Value = match serde_json::from_str(text) {
        Ok(v) => v,
        Err(_) => return Ok(Vec::new()),
    };
    let items: Vec<Value> = match parsed {
        Value::Array(a) => a,
        Value::Object(_) => vec![parsed],
        _ => return Ok(Vec::new()),
    };

    let rows = items
        .into_iter()
        .enumerate()
        .map(|(i, ev)| {
            let time = ev.get("t").and_then(|v| v.as_str()).unwrap_or("").to_string();
            let event_id = ev.get("Id").and_then(|v| v.as_i64()).unwrap_or(0);
            let msg = ev.get("msg").and_then(|v| v.as_str()).unwrap_or("");
            // First non-empty line of the Message is the most useful summary.
            let first = msg.lines().find(|l| !l.trim().is_empty()).unwrap_or("").trim();
            Row {
                id: i as u64,
                time,
                event_id,
                event_type: event_type(event_id),
                detail: crate::truncate(first, 120),
            }
        })
        .collect();
    Ok(rows)
}

fn event_row(r: &Row) -> Element {
    border(
        vstack((
            hstack((
                body_strong(r.event_type.clone()),
                caption(format!("#{}", r.event_id)).opacity(0.6).font_family("Consolas"),
                caption(r.time.clone()).opacity(0.7).font_family("Consolas"),
            ))
            .spacing(8.0),
            caption(r.detail.clone()).opacity(0.85).wrap(),
        ))
        .spacing(2.0),
    )
    .corner_radius(4.0)
    .padding(Thickness::uniform(8.0))
    .margin(Thickness::xy(0.0, 2.0))
    .into()
}

pub fn sysmon_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (tick, bump) = cx.use_reducer(0_u64);
    let result = cx.use_resource(|_: u64| run_sysmon(), tick);
    let list_height = crate::list_height(cx);

    let body_el: Element = result
        .view(move |rows: &Vec<Row>| -> Element {
            if rows.is_empty() {
                return caption(
                    "Sysmon is not installed, or its operational log is unavailable on this device.",
                )
                .opacity(0.7)
                .wrap()
                .into();
            }
            list_view(rows.clone(), |r: &Row, _| event_row(r))
                .with_key_selector(|r: &Row| r.id.to_string())
                .height(list_height - 28.0)
                .into()
        })
        .loading(caption("reading Sysmon events…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    crate::fluid_fill(
        12.0,
        vec![Element::from(
            button("Refresh").on_click(move || bump.call(|n| n + 1)),
        )],
        body_el,
    )
    .margin(Thickness::uniform(16.0))
    .into()
}
