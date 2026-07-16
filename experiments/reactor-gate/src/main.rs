#![windows_subsystem = "windows"]

// Phase 0b — the real risk-gate. Two things must hold up before we commit to
// Windows Reactor for the cmProjectX client:
//   1. A *virtualized* LogGrid renders tens of thousands of parsed CMTrace rows
//      without realizing every row up front (scroll has to stay smooth).
//   2. *Streaming appends* (a live tail) can grow that list incrementally while
//      it's on screen, marshalled onto the UI thread.
//
// Both are driven through the local cmtraceopen parser (the OSS seam) so we're
// exercising the actual data path, not a toy Vec<String>. The parser turns
// CCM-format text into LogEntry rows; we format those for display and feed the
// growing Vec into Reactor's `list_view` (its templated/virtualized list).
//
// Live appends use a DispatcherTimer rather than a background thread: in Reactor
// the timer callback runs on the UI thread, which is exactly how a real file
// tail would marshal new lines into the view (the honest pattern to validate).

use std::time::Duration;

use cmtraceopen_parser::models::log_entry::Severity;
use cmtraceopen_parser::parser::parse_content;
use windows_reactor::*;

const INITIAL_ROWS: usize = 20_000;
const TAIL_BATCH: usize = 25;
const TAIL_INTERVAL_MS: u64 = 300;

// One synthetic CCM log line. `seq` makes the message (and therefore the list
// key) unique so the virtualized list never collapses distinct rows.
fn ccm_line(seq: usize) -> String {
    let severity_type = match seq % 7 {
        0 => 3, // error
        1 | 2 => 2, // warning
        _ => 1, // info
    };
    let component = match seq % 4 {
        0 => "PolicyAgent",
        1 => "ContentTransferManager",
        2 => "DataTransferService",
        _ => "ConfigMgrAgent",
    };
    let secs = seq % 60;
    let mins = (seq / 60) % 60;
    let hours = (seq / 3600) % 24;
    format!(
        "<![LOG[seq {seq}: {component} processed item batch, state nominal]LOG]!>\
<time=\"{hours:02}:{mins:02}:{secs:02}.000+000\" date=\"06-07-2026\" \
component=\"{component}\" context=\"\" type=\"{severity_type}\" thread=\"{tid}\" file=\"gate.cpp:1\">",
        tid = 1000 + (seq % 64)
    )
}

// Parse `count` CCM lines starting at `start_seq` through the real parser and
// return formatted display rows. This keeps the parser on the hot path for both
// the initial fill and every streamed batch.
fn parse_rows(start_seq: usize, count: usize) -> Vec<String> {
    let mut content = String::with_capacity(count * 180);
    for i in 0..count {
        content.push_str(&ccm_line(start_seq + i));
        content.push_str("\r\n");
    }
    let (result, _resolved) = parse_content(&content, "gate.log", content.len() as u64);
    result
        .entries
        .iter()
        .map(|e| {
            let sev = match e.severity {
                Severity::Error => "ERR ",
                Severity::Warning => "WARN",
                Severity::Info => "INFO",
            };
            let comp = e.component.as_deref().unwrap_or("-");
            let ts = e.timestamp_display.as_deref().unwrap_or("--:--:--");
            format!("#{:<7} {ts} [{sev}] {comp:<24} {}", start_seq + e.line_number as usize, e.message)
        })
        .collect()
}

fn app(cx: &mut RenderCx) -> Element {
    // Source of truth for the rows lives in a ref so the tail timer can push
    // onto it without cloning the whole vector through a setter every tick.
    let rows = cx.use_ref::<Vec<String>>(Vec::new());
    // A version/len counter in real state drives re-render when the tail grows.
    let (len, set_len) = cx.use_state(0_usize);
    let (selected, set_selected) = cx.use_state(-1_i32);

    // One-time initial fill: parse 20k rows up front to stress virtualization.
    {
        let mut guard = rows.borrow_mut();
        if guard.is_empty() {
            *guard = parse_rows(0, INITIAL_ROWS);
            let n = guard.len();
            drop(guard);
            set_len.call(n);
        }
    }

    // Install the live-tail timer exactly once (empty deps). The callback runs
    // on the UI thread and appends a freshly-parsed batch each interval.
    let timer_ref = cx.use_ref::<Option<DispatcherTimer>>(None);
    let timer_slot = timer_ref.clone();
    let rows_for_timer = rows.clone();
    let set_len_for_timer = set_len.clone();
    cx.use_effect((), move || {
        let timer = DispatcherTimer::new(Duration::from_millis(TAIL_INTERVAL_MS), move || {
            let start = rows_for_timer.borrow().len();
            let mut batch = parse_rows(start, TAIL_BATCH);
            let mut guard = rows_for_timer.borrow_mut();
            guard.append(&mut batch);
            let n = guard.len();
            drop(guard);
            set_len_for_timer.call(n);
        })
        .expect("DispatcherQueue.CreateTimer");
        timer_slot.set(Some(timer));
    });

    let items = rows.borrow().clone();

    vstack((
        text_block("cmProjectX reactor-gate — virtualized LogGrid + live tail")
            .font_size(20.0)
            .bold(),
        text_block(format!(
            "{len} rows (started at {INITIAL_ROWS}, tailing +{TAIL_BATCH} every {TAIL_INTERVAL_MS}ms) — scroll to validate virtualization"
        ))
        .opacity(0.7),
        list_view(items, |line, _idx| {
            text_block(line.clone()).padding(Thickness::xy(10.0, 2.0))
        })
        .with_key_selector(|line| line.clone())
        .selected_index(selected)
        .selection_mode(SelectionMode::Single)
        .on_selection_changed(move |i| set_selected.call(i))
        .height(620.0),
    ))
    .spacing(8.0)
    .padding(Thickness::uniform(16.0))
    .into()
}

fn main() -> Result<()> {
    let _bootstrap_handle = bootstrap::initialize()?;
    App::new()
        .title("cmProjectX Reactor gate")
        .eager_templated_realization(true)
        .render(app)
}
