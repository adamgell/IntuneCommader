//! Diagnostics: Timeline Correlation.
//!
//! Merges Windows Event Log (System + Application) and Sysmon operational events
//! into ONE time-sorted view, so you can see what happened across sources around
//! an incident. Pure Rust, NO sidecar / NO auth — the same `Get-WinEvent` idiom as
//! the Event Log and Sysmon viewers, just fanned across channels and interleaved.

use windows_reactor::*;

// One merged row, tagged with the source it came from. `use_resource` requires
// `Clone + PartialEq`, so we flatten each source's event into this shape.
#[derive(Clone, PartialEq)]
struct Row {
    key: u64,        // stable list key, assigned after the merge sort
    time: String,    // ISO 8601 ('s') — lexically sortable
    source: String,  // "System" | "Application" | "Sysmon"
    level: String,   // Error / Warning / Information (event log); "" for Sysmon
    label: String,   // provider name
    message: String, // first/most-useful line of the event message
}

// Read one channel and tag every row with `source`. Returns Ok(empty) when the
// channel is missing (Sysmon often is) instead of erroring, so one absent source
// never blanks the whole timeline.
fn read_channel(log_name: &str, source: &str) -> std::result::Result<Vec<Row>, String> {
    let script = format!(
        "Get-WinEvent -LogName '{log_name}' -MaxEvents 120 -ErrorAction SilentlyContinue | \
         Select-Object @{{n='t';e={{$_.TimeCreated.ToString('s')}}}}, \
         @{{n='lvl';e={{$_.LevelDisplayName}}}}, @{{n='prov';e={{$_.ProviderName}}}}, \
         @{{n='msg';e={{$_.Message}}}} | ConvertTo-Json -Depth 3"
    );

    let out = std::process::Command::new("powershell")
        .args(["-NoProfile", "-Command", &script])
        .output()
        .map_err(|e| format!("couldn't run powershell: {e}"))?;

    if !out.status.success() {
        return Ok(Vec::new());
    }
    let text = String::from_utf8_lossy(&out.stdout);
    let trimmed = text.trim();
    if trimmed.is_empty() {
        return Ok(Vec::new());
    }

    let parsed: serde_json::Value = match serde_json::from_str(trimmed) {
        Ok(v) => v,
        Err(_) => return Ok(Vec::new()),
    };
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

    let rows = items
        .iter()
        .map(|v| {
            let msg = field(v, "msg");
            let first = msg.lines().find(|l| !l.trim().is_empty()).unwrap_or("").trim();
            Row {
                key: 0,
                time: field(v, "t"),
                source: source.to_string(),
                level: field(v, "lvl"),
                label: field(v, "prov"),
                message: crate::truncate(first, 200),
            }
        })
        .collect();
    Ok(rows)
}

// Fan out across the three channels, interleave newest-first, and key the result.
fn read_timeline() -> std::result::Result<Vec<Row>, String> {
    let mut all: Vec<Row> = Vec::new();
    all.extend(read_channel("System", "System")?);
    all.extend(read_channel("Application", "Application")?);
    all.extend(read_channel("Microsoft-Windows-Sysmon/Operational", "Sysmon")?);
    // Newest first across every source — the correlation payoff.
    all.sort_by(|a, b| b.time.cmp(&a.time));
    for (i, r) in all.iter_mut().enumerate() {
        r.key = i as u64;
    }
    Ok(all)
}

fn row_view(r: &Row) -> Element {
    let head = format!("{}  [{}]  {}", r.time, r.source, r.label);
    let head_tb = body_strong(head).font_family("Consolas");
    let head_colored = match r.level.as_str() {
        "Error" | "Critical" => head_tb.foreground(crate::theme::SEV_ERROR),
        "Warning" => head_tb.foreground(crate::theme::SEV_WARN),
        _ => head_tb,
    };
    border(
        vstack((
            head_colored,
            caption(r.message.clone()).opacity(0.85).wrap(),
        ))
        .spacing(2.0),
    )
    .corner_radius(4.0)
    .padding(Thickness::uniform(8.0))
    .margin(Thickness::xy(0.0, 2.0))
    .into()
}

pub fn correlation_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (tick, bump) = cx.use_reducer(0_u64);
    let result = cx.use_resource(|_: u64| read_timeline(), tick);
    let list_height = crate::list_height(cx);

    let intro = caption(
        "Merged, time-sorted events from System + Application + Sysmon — correlate what \
         happened across sources around an incident.",
    )
    .opacity(0.7)
    .wrap();

    let list: Element = result
        .view(move |rows: &Vec<Row>| -> Element {
            if rows.is_empty() {
                return body("No events found across the correlated sources.")
                    .opacity(0.6)
                    .wrap()
                    .into();
            }
            let count = caption(format!("{} correlated events", rows.len())).opacity(0.7);
            let lv = list_view(rows.clone(), |r: &Row, _| row_view(r))
                .with_key_selector(|r: &Row| r.key.to_string())
                .height(list_height - 84.0);
            crate::fluid_fill(8.0, vec![Element::from(count)], Element::from(lv)).into()
        })
        .loading(caption("correlating event sources…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    crate::fluid_fill(
        12.0,
        vec![
            Element::from(button("Refresh").on_click(move || bump.call(|n| n + 1))),
            Element::from(intro),
        ],
        list,
    )
    .margin(Thickness::uniform(16.0))
    .into()
}
