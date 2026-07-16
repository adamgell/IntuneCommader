//! Local Windows diagnostics: "Event Log Viewer".
//!
//! Reads the Windows Event Log entirely in-process by shelling out to PowerShell's
//! `Get-WinEvent` (pure Rust, no sidecar, no auth — exactly like the dsregcmd /
//! registry diagnostics). A channel picker (System / Application / Setup) selects
//! which log to read; the most recent 200 events are projected into a small
//! comparable row and shown as a virtualized list. Error rows render red and
//! Warning rows amber. A "Refresh" reducer re-keys the resource to re-query on
//! demand, and switching channels re-keys it as well.

use windows_reactor::*;

// The three event-log channels we expose. Kept as a slice so the picker and the
// fetcher agree on the exact `Get-WinEvent -LogName` argument.
const CHANNELS: &[&str] = &["System", "Application", "Setup"];

// A lean, Clone + PartialEq projection of one event. `use_resource` requires the
// row type to be comparable, so we flatten the PowerShell JSON into this.
#[derive(Clone, PartialEq)]
struct Row {
    id: u64, // list index — stable key within a single query result
    time: String,
    level: String,
    provider: String,
    message: String,
}

// Run `Get-WinEvent` for the selected channel on the background fetcher thread and
// parse the JSON it emits. A single event serializes as a JSON object (not an
// array), so we handle both shapes.
fn read_events(channel: &str) -> std::result::Result<Vec<Row>, String> {
    let script = format!(
        "Get-WinEvent -LogName {channel} -MaxEvents 200 -ErrorAction SilentlyContinue | \
         Select-Object @{{n='t';e={{$_.TimeCreated.ToString('s')}}}}, Id, \
         @{{n='lvl';e={{$_.LevelDisplayName}}}}, @{{n='prov';e={{$_.ProviderName}}}}, \
         @{{n='msg';e={{$_.Message}}}} | ConvertTo-Json -Depth 3"
    );

    let out = std::process::Command::new("powershell")
        .args(["-NoProfile", "-Command", &script])
        .output()
        .map_err(|e| format!("couldn't run powershell: {e}"))?;

    let text = String::from_utf8_lossy(&out.stdout);
    let trimmed = text.trim();
    if trimmed.is_empty() {
        // No events (or no access) — surface as an empty list, not an error.
        return Ok(Vec::new());
    }

    let parsed: serde_json::Value = serde_json::from_str(trimmed)
        .map_err(|e| format!("couldn't parse Get-WinEvent output: {e}"))?;

    // ConvertTo-Json emits a bare object for a single event, an array otherwise.
    let items: Vec<serde_json::Value> = match parsed {
        serde_json::Value::Array(a) => a,
        other => vec![other],
    };

    let field = |v: &serde_json::Value, key: &str| -> String {
        match v.get(key) {
            Some(serde_json::Value::String(s)) => s.clone(),
            Some(serde_json::Value::Null) | None => String::new(),
            Some(other) => other.to_string(),
        }
    };

    let rows: Vec<Row> = items
        .iter()
        .enumerate()
        .map(|(i, v)| Row {
            id: i as u64,
            time: field(v, "t"),
            level: field(v, "lvl"),
            provider: field(v, "prov"),
            message: field(v, "msg"),
        })
        .collect();

    Ok(rows)
}

fn event_row(r: &Row) -> Element {
    // Header: "2026-06-09T11:02:13  Error  Microsoft-Windows-Kernel-Power"
    let head = format!("{}  {}  {}", r.time, r.level, r.provider);
    let head_tb = body_strong(head).font_family("Consolas");
    let head_colored = match r.level.as_str() {
        "Error" | "Critical" => head_tb.foreground(crate::theme::SEV_ERROR),
        "Warning" => head_tb.foreground(crate::theme::SEV_WARN),
        _ => head_tb,
    };
    let msg = r.message.replace('\n', " ⏎ ");
    border(
        vstack((
            head_colored,
            caption(crate::truncate(&msg, 240)).opacity(0.85).wrap(),
        ))
        .spacing(2.0),
    )
    .corner_radius(4.0)
    .padding(Thickness::uniform(8.0))
    .into()
}

pub fn eventlog_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // Selected channel (defaults to System) and a Refresh reducer; the query
    // resource is keyed on both so picking a channel or clicking Refresh refetches.
    let (channel, set_channel) = cx.use_state(String::from("System"));
    let (tick, bump) = cx.use_reducer(0_u64);

    let events = cx.use_resource(
        |key: (String, u64)| -> std::result::Result<Vec<Row>, String> {
            let (channel, _tick) = key;
            read_events(&channel)
        },
        (channel.clone(), tick),
    );

    let list_height = crate::list_height(cx);

    // Channel picker: one button per channel; the active channel's button is
    // accented and disabled so the current selection reads clearly.
    let pickers: Vec<Element> = CHANNELS
        .iter()
        .map(|&ch| {
            let is_active = channel == ch;
            let set_channel = set_channel.clone();
            let target = ch.to_string();
            let mut b = button(ch).on_click(move || set_channel.call(target.clone()));
            if is_active {
                b = b.accent();
            }
            Element::from(b.enabled(!is_active))
        })
        .collect();

    let refresh = button("Refresh").on_click(move || bump.call(|n| n + 1));

    let mut controls_children: Vec<Element> = pickers;
    controls_children.push(Element::from(refresh));
    let controls = hstack(controls_children).spacing(8.0);

    let list: Element = events
        .view(move |rows: &Vec<Row>| -> Element {
            if rows.is_empty() {
                return body("No events found in this channel.")
                    .opacity(0.6)
                    .wrap()
                    .into();
            }
            let count = caption(format!("{} events", rows.len())).opacity(0.7);
            let lv = list_view(rows.clone(), |r: &Row, _| event_row(r))
                .with_key_selector(|r: &Row| r.id.to_string())
                .height(list_height - 56.0);
            crate::fluid_fill(8.0, vec![Element::from(count)], Element::from(lv)).into()
        })
        .loading(caption("reading event log…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    crate::fluid_fill(12.0, vec![Element::from(controls)], list)
        .margin(Thickness::uniform(16.0))
        .into()
}
