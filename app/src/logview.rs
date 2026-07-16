//! Log Explorer — the cmProjectX CMTrace-style log viewer.
//!
//! Track 1 of the "bring the full cmtraceopen log viewer here" plan, off the shared
//! upstream `cmtraceopen_parser` (we only consume it — see CLAUDE.md).
//!
//! Phase 1: parser-aware columnar grid, format badge + severity/parse stats, live
//! tail with pause/resume + stick-to-bottom follow (reactor `follow_tail`), open by
//! path + on-device known sources.
//!
//! Phase 2 (this file): **Find** (plain / regex / case, next / prev, "N of M",
//! match-row highlight, centre-on-match via the reactor `scroll_to` primitive),
//! **Filter** (contains-text + severity floor), a **details pane** (all fields of the
//! selected row), and **keyboard accelerators** (F3 / Shift+F3 next/prev, Ctrl+H
//! details). Still fixed-width columns — user resize/reorder is Track 0-B; the native
//! open dialog is Track 0-A.

use std::collections::HashSet;
use std::io::Write;
use std::os::windows::process::CommandExt;
use std::path::Path;
use std::sync::Arc;
use std::time::{Duration, SystemTime};

use cmtraceopen_parser::models::log_entry::{LogEntry, ParserKind, Severity};
use cmtraceopen_parser::parser;
use regex::RegexBuilder;
use windows_reactor::*;

use crate::known_sources;
use crate::theme;
use crate::{error_box, fluid_fill, list_height, truncate};

/// Hard ceiling on rows handed to the grid. The file is parsed in full; if it is
/// enormous we keep the most-recent `MAX_ROWS` (tail semantics) so memory and the
/// initial realisation pass stay bounded. WinUI virtualises the render itself.
const MAX_ROWS: usize = 200_000;

// ─── Column model ────────────────────────────────────────────────────────────

/// The columns the grid can show. The *visible set* is chosen per detected
/// `ParserKind` (see [`columns_for`]); each row projects its cell text by id.
#[derive(Clone, Copy, PartialEq, Eq)]
enum ColumnId {
    Line,
    Time,
    Severity,
    Component,
    Thread,
    SourceFile,
    Message,
    // IIS W3C
    HttpMethod,
    UriStem,
    StatusCode,
    TimeTaken,
    ClientIp,
    // DHCP
    IpAddress,
    HostName,
    MacAddress,
    // DNS
    QueryName,
    QueryType,
    ResponseCode,
    SourceIp,
    // Panther / Windows Setup
    SetupPhase,
    OperationName,
    ResultCode,
    GleCode,
}

impl ColumnId {
    fn label(self) -> &'static str {
        use ColumnId::*;
        match self {
            Line => "#",
            Time => "Time",
            Severity => "Sev",
            Component => "Component",
            Thread => "Thread",
            SourceFile => "Source",
            Message => "Message",
            HttpMethod => "Method",
            UriStem => "URI",
            StatusCode => "Status",
            TimeTaken => "ms",
            ClientIp => "Client IP",
            IpAddress => "IP",
            HostName => "Host",
            MacAddress => "MAC",
            QueryName => "Query",
            QueryType => "Type",
            ResponseCode => "RCode",
            SourceIp => "Source IP",
            SetupPhase => "Phase",
            OperationName => "Operation",
            ResultCode => "Result",
            GleCode => "GLE",
        }
    }

    fn width(self) -> GridLength {
        use ColumnId::*;
        match self {
            Message => GridLength::Star(1.0),
            Line => GridLength::Pixel(56.0),
            Time => GridLength::Pixel(150.0),
            Severity => GridLength::Pixel(48.0),
            Component => GridLength::Pixel(170.0),
            Thread => GridLength::Pixel(96.0),
            SourceFile => GridLength::Pixel(150.0),
            HttpMethod => GridLength::Pixel(68.0),
            UriStem => GridLength::Pixel(260.0),
            StatusCode => GridLength::Pixel(60.0),
            TimeTaken => GridLength::Pixel(56.0),
            ClientIp | IpAddress | SourceIp => GridLength::Pixel(120.0),
            HostName => GridLength::Pixel(140.0),
            MacAddress => GridLength::Pixel(140.0),
            QueryName => GridLength::Pixel(210.0),
            QueryType => GridLength::Pixel(64.0),
            ResponseCode => GridLength::Pixel(84.0),
            SetupPhase => GridLength::Pixel(150.0),
            OperationName => GridLength::Pixel(160.0),
            ResultCode | GleCode => GridLength::Pixel(96.0),
        }
    }
}

/// Default visible columns for the detected parser. Message is always last and
/// flex-sized. Falls back to a lean CCM-ish set for anything unrecognised.
fn columns_for(kind: ParserKind) -> Vec<ColumnId> {
    use ColumnId::*;
    use ParserKind as K;
    match kind {
        K::Ccm | K::CmtLog => vec![Line, Time, Severity, Component, Thread, SourceFile, Message],
        K::Simple | K::IntuneMacOs => vec![Line, Time, Severity, Component, Thread, Message],
        K::IisW3c => vec![
            Line, Time, Severity, ClientIp, HttpMethod, UriStem, StatusCode, TimeTaken, Message,
        ],
        K::Dhcp => vec![Line, Time, Severity, IpAddress, HostName, MacAddress, Message],
        K::DnsDebug | K::DnsAudit => {
            vec![Line, Time, Severity, QueryName, QueryType, ResponseCode, SourceIp, Message]
        }
        K::Panther => vec![
            Line, Time, Severity, SetupPhase, OperationName, ResultCode, GleCode, Message,
        ],
        _ => vec![Line, Time, Severity, Component, Message],
    }
}

// ─── Row model ───────────────────────────────────────────────────────────────

/// A trimmed, comparable projection of the parser's wide `LogEntry`. `use_resource`
/// requires `Clone + PartialEq`, which `LogEntry` does not derive. Core columns are
/// typed fields; format-specific cells ride in `extras` as `(id, text)` pairs so the
/// struct stays lean regardless of format.
#[derive(Clone, PartialEq)]
struct LogRow {
    id: u64,
    line: u32,
    time: String,
    severity: Severity,
    component: String,
    thread: String,
    source_file: String,
    message: String,
    extras: Vec<(ColumnId, String)>,
    /// Pre-resolved error-code descriptions from the parser's `error_code_spans`
    /// (e.g. "0x80070005 — E_ACCESSDENIED - Access is denied (Windows)"). Surfaced
    /// in the details pane; empty for most rows.
    error_codes: Vec<String>,
}

impl LogRow {
    fn cell(&self, col: ColumnId) -> String {
        use ColumnId::*;
        match col {
            Line => self.line.to_string(),
            Time => self.time.clone(),
            Severity => sev_label(self.severity).to_string(),
            Component => self.component.clone(),
            Thread => self.thread.clone(),
            SourceFile => self.source_file.clone(),
            Message => self.message.clone(),
            other => self
                .extras
                .iter()
                .find(|(k, _)| *k == other)
                .map(|(_, v)| v.clone())
                .unwrap_or_default(),
        }
    }
}

fn sev_label(s: Severity) -> &'static str {
    match s {
        Severity::Error => "ERR",
        Severity::Warning => "WRN",
        Severity::Info => "INF",
    }
}

fn to_row(e: LogEntry) -> LogRow {
    let mut extras: Vec<(ColumnId, String)> = Vec::new();
    let mut push = |id: ColumnId, v: &Option<String>| {
        if let Some(v) = v {
            extras.push((id, v.clone()));
        }
    };
    // Numerics stringified up front so every field flows through the one `push`
    // closure (mixing a direct `extras.push` with the closure would be a second
    // overlapping mutable borrow).
    let status = e.status_code.map(|n| n.to_string());
    let taken = e.time_taken_ms.map(|n| n.to_string());
    push(ColumnId::HttpMethod, &e.http_method);
    push(ColumnId::UriStem, &e.uri_stem);
    push(ColumnId::StatusCode, &status);
    push(ColumnId::TimeTaken, &taken);
    push(ColumnId::ClientIp, &e.client_ip);
    push(ColumnId::IpAddress, &e.ip_address);
    push(ColumnId::HostName, &e.host_name);
    push(ColumnId::MacAddress, &e.mac_address);
    push(ColumnId::QueryName, &e.query_name);
    push(ColumnId::QueryType, &e.query_type);
    push(ColumnId::ResponseCode, &e.response_code);
    push(ColumnId::SourceIp, &e.source_ip);
    push(ColumnId::SetupPhase, &e.setup_phase);
    push(ColumnId::OperationName, &e.operation_name);
    push(ColumnId::ResultCode, &e.result_code);
    push(ColumnId::GleCode, &e.gle_code);

    // Resolve recognised error codes (the parser annotates spans inline) into
    // human strings for the details pane.
    let error_codes: Vec<String> = e
        .error_code_spans
        .iter()
        .map(|s| {
            if s.description.is_empty() {
                s.code_hex.clone()
            } else {
                format!("{} — {} ({})", s.code_hex, s.description, s.category)
            }
        })
        .collect();

    LogRow {
        id: e.id,
        line: e.line_number,
        time: e.timestamp_display.unwrap_or_default(),
        severity: e.severity,
        component: e.component.unwrap_or_default(),
        thread: e.thread_display.unwrap_or_default(),
        source_file: e.source_file.unwrap_or_default(),
        message: e.message,
        extras,
        error_codes,
    }
}

/// Parsed-file payload for the entry resource. Everything here is `Clone + PartialEq`
/// so `use_resource` can memoise it and skip re-render when the file is unchanged.
#[derive(Clone, PartialEq)]
struct Parsed {
    // `Arc` so filtering + the list handoff clone pointers, not the String payloads —
    // the workspace re-renders every 2s tail tick, and a big log must stay cheap.
    // (`Arc` not `Rc`: the parse runs on a background thread, so `Parsed` must be `Send`.)
    rows: Vec<Arc<LogRow>>,
    kind: ParserKind,
    provenance: String,
    quality: String,
    total_lines: u32,
    parse_errors: u32,
    errors: usize,
    warnings: usize,
    truncated: bool,
}

impl Parsed {
    fn empty() -> Self {
        Parsed {
            rows: Vec::new(),
            kind: ParserKind::Plain,
            provenance: String::new(),
            quality: String::new(),
            total_lines: 0,
            parse_errors: 0,
            errors: 0,
            warnings: 0,
            truncated: false,
        }
    }
}

// ─── Source enumeration (known on-device logs) ───────────────────────────────

#[derive(Clone, PartialEq)]
struct LogSource {
    family: String,
    name: String,
    path: String,
    size: u64,
    modified: u64,
}

fn source_row(s: &LogSource) -> Element {
    let kb = (s.size as f64 / 1024.0).round() as u64;
    vstack((
        body_strong(truncate(&s.name, 30)),
        caption(format!("{} · {kb} KB", s.family)).opacity(0.7),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

// Scan the cmtrace known-source catalog and return every *.log that exists on this
// box, tagged with its family; folders enumerated non-recursively. Missing paths
// are skipped silently (most catalog entries won't exist on any given device).
fn list_log_sources() -> std::result::Result<Vec<LogSource>, String> {
    let mut out: Vec<LogSource> = Vec::new();
    for ks in known_sources::KNOWN_SOURCES {
        if ks.folder {
            let read = match std::fs::read_dir(ks.path) {
                Ok(r) => r,
                Err(_) => continue,
            };
            for entry in read.flatten() {
                let path = entry.path();
                let is_log = path
                    .extension()
                    .and_then(|x| x.to_str())
                    .is_some_and(|x| x.eq_ignore_ascii_case("log"));
                if !is_log {
                    continue;
                }
                if let Some(src) = file_source(ks.family, &path) {
                    out.push(src);
                }
            }
        } else if let Some(src) = file_source(ks.family, Path::new(ks.path)) {
            out.push(src);
        }
    }
    out.sort_by(|a, b| a.path.cmp(&b.path));
    out.dedup_by(|a, b| a.path == b.path);
    out.sort_by(|a, b| b.modified.cmp(&a.modified));
    Ok(out)
}

fn file_source(family: &str, path: &Path) -> Option<LogSource> {
    let meta = std::fs::metadata(path).ok()?;
    if !meta.is_file() {
        return None;
    }
    let modified = meta
        .modified()
        .ok()
        .and_then(|m| m.duration_since(SystemTime::UNIX_EPOCH).ok())
        .map(|d| d.as_secs())
        .unwrap_or(0);
    Some(LogSource {
        family: family.to_string(),
        name: path
            .file_name()
            .map(|s| s.to_string_lossy().into_owned())
            .unwrap_or_else(|| path.to_string_lossy().into_owned()),
        path: path.to_string_lossy().into_owned(),
        size: meta.len(),
        modified,
    })
}

// ─── Parse (background thread) ───────────────────────────────────────────────

fn read_log_full(path: &str) -> std::result::Result<Parsed, String> {
    if path.is_empty() {
        return Ok(Parsed::empty());
    }
    let bytes = std::fs::read(path).map_err(|e| format!("Couldn't read {path}: {e}"))?;
    let size = bytes.len() as u64;
    let encoding = parser::detect_encoding(&bytes);
    let text = parser::decode_bytes(&bytes, encoding)?;
    let (result, _selection) = parser::parse_content(&text, path, size);

    let kind = result.parser_selection.parser;
    let provenance = format!("{:?}", result.parser_selection.provenance);
    let quality = format!("{:?}", result.parser_selection.parse_quality);
    let total_lines = result.total_lines;
    let parse_errors = result.parse_errors;

    let mut entries = result.entries;
    let truncated = entries.len() > MAX_ROWS;
    if truncated {
        let drop = entries.len() - MAX_ROWS;
        entries.drain(0..drop);
    }
    let rows: Vec<Arc<LogRow>> = entries.into_iter().map(to_row).map(Arc::new).collect();
    let errors = rows.iter().filter(|r| r.severity == Severity::Error).count();
    let warnings = rows
        .iter()
        .filter(|r| r.severity == Severity::Warning)
        .count();

    Ok(Parsed {
        rows,
        kind,
        provenance,
        quality,
        total_lines,
        parse_errors,
        errors,
        warnings,
        truncated,
    })
}

// ─── Cell + row rendering ────────────────────────────────────────────────────

fn sev_color(s: Severity) -> Option<Color> {
    match s {
        Severity::Error => Some(theme::SEV_ERROR),
        Severity::Warning => Some(theme::SEV_WARN),
        Severity::Info => None,
    }
}

fn cell_element(row: &LogRow, col: ColumnId) -> Element {
    let text = if col == ColumnId::Message {
        row.message.replace('\n', " ⏎ ")
    } else {
        row.cell(col)
    };
    let mut tb = body(text).font_family(theme::FONT_MONO).font_size(12.0);
    // Colour the message + severity cells by level; mute the metadata cells.
    if col == ColumnId::Message || col == ColumnId::Severity {
        if let Some(c) = sev_color(row.severity) {
            tb = tb.foreground(c);
        }
    } else {
        tb = tb.opacity(0.82);
    }
    tb.into()
}

/// Find-match highlight state for a row.
#[derive(Clone, Copy, PartialEq)]
enum RowHl {
    None,
    Match,
    Current,
}

fn row_grid(row: &LogRow, cols: &[ColumnId], hl: RowHl) -> Element {
    let widths: Vec<GridLength> = cols.iter().map(|c| c.width()).collect();
    let cells: Vec<Element> = cols
        .iter()
        .enumerate()
        .map(|(i, &col)| cell_element(row, col).grid_column(i as i32))
        .collect();
    let g = grid(cells).columns(widths).column_spacing(8.0);
    match hl {
        RowHl::None => g.margin(Thickness::xy(8.0, 1.0)).into(),
        RowHl::Match => border(Element::from(g))
            .background(theme::SURFACE_3)
            .padding(Thickness::xy(8.0, 1.0))
            .into(),
        RowHl::Current => border(Element::from(g))
            .background(theme::TEAL_DEEP)
            .padding(Thickness::xy(8.0, 1.0))
            .into(),
    }
}

fn header_grid(cols: &[ColumnId]) -> Element {
    let widths: Vec<GridLength> = cols.iter().map(|c| c.width()).collect();
    let cells: Vec<Element> = cols
        .iter()
        .enumerate()
        .map(|(i, &col)| {
            Element::from(
                caption(col.label())
                    .opacity(0.6)
                    .font_family(theme::FONT_MONO)
                    .font_size(11.0),
            )
            .grid_column(i as i32)
        })
        .collect();
    grid(cells)
        .columns(widths)
        .column_spacing(8.0)
        .margin(Thickness::xy(8.0, 4.0))
        .into()
}

fn kind_label(kind: ParserKind) -> String {
    // Debug names are close enough for the badge (Ccm, IisW3c, DnsDebug, …).
    format!("{kind:?}")
}

// ─── Filter + details helpers ────────────────────────────────────────────────

/// AND of a severity floor (0 = all, 1 = warn+, 2 = error) and a case-insensitive
/// substring over message/component. `needle_lc` must already be lower-cased.
fn passes_filter(row: &LogRow, needle_lc: &str, sev_min: u8) -> bool {
    let sev_ok = match sev_min {
        1 => matches!(row.severity, Severity::Warning | Severity::Error),
        2 => matches!(row.severity, Severity::Error),
        _ => true,
    };
    if !sev_ok {
        return false;
    }
    if needle_lc.is_empty() {
        return true;
    }
    row.message.to_lowercase().contains(needle_lc)
        || row.component.to_lowercase().contains(needle_lc)
}

/// A button that renders `.accent()` while `active`, firing `on_toggle` on click.
fn toggle_button(label: &str, active: bool, on_toggle: impl Fn() + 'static) -> Element {
    let b = button(label).on_click(on_toggle);
    if active {
        b.accent().into()
    } else {
        b.into()
    }
}

fn detail_kv(k: &str, v: String) -> Element {
    grid((
        Element::from(caption(k.to_string()).opacity(0.55)).grid_column(0),
        Element::from(
            body(v)
                .font_family(theme::FONT_MONO)
                .font_size(12.0)
                .wrap(),
        )
        .grid_column(1),
    ))
    .columns([GridLength::Pixel(92.0), GridLength::Star(1.0)])
    .column_spacing(8.0)
    .margin(Thickness::xy(0.0, 3.0))
    .into()
}

/// Right-side panel listing every populated field of the selected row + the full
/// (wrapped) message. Scrolls within `height`.
fn details_panel(row: &LogRow, height: f64) -> Element {
    let mut kvs: Vec<(String, String)> = vec![
        ("Line".into(), row.line.to_string()),
        ("Time".into(), row.time.clone()),
        ("Severity".into(), sev_label(row.severity).to_string()),
        ("Component".into(), row.component.clone()),
        ("Thread".into(), row.thread.clone()),
        ("Source".into(), row.source_file.clone()),
    ];
    for (id, v) in &row.extras {
        kvs.push((id.label().to_string(), v.clone()));
    }
    let mut items: Vec<Element> = vec![Element::from(body_strong("Details".to_string()))];
    items.extend(
        kvs.into_iter()
            .filter(|(_, v)| !v.is_empty())
            .map(|(k, v)| detail_kv(&k, v)),
    );
    if !row.error_codes.is_empty() {
        items.push(Element::from(
            caption("Error codes")
                .opacity(0.55)
                .margin(Thickness::xy(0.0, 6.0)),
        ));
        for ec in &row.error_codes {
            items.push(Element::from(
                body(ec.clone())
                    .font_family(theme::FONT_MONO)
                    .font_size(12.0)
                    .foreground(theme::SEV_ERROR)
                    .wrap(),
            ));
        }
    }
    items.push(Element::from(
        caption("Message").opacity(0.55).margin(Thickness::xy(0.0, 6.0)),
    ));
    items.push(Element::from(
        body(row.message.clone())
            .font_family(theme::FONT_MONO)
            .font_size(12.0)
            .wrap(),
    ));
    border(scroll_viewer(vstack(items).spacing(2.0).margin(Thickness::uniform(12.0))).height(height))
        .background(theme::SURFACE)
        .corner_radius(4.0)
        .into()
}

/// Copy `text` to the Windows clipboard via `clip.exe` (mirrors the helper in
/// `config_view.rs`; no window flash).
fn copy_to_clipboard(text: &str) -> std::io::Result<()> {
    use std::process::{Command, Stdio};
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;
    let mut child = Command::new("clip.exe")
        .creation_flags(CREATE_NO_WINDOW)
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()?;
    child
        .stdin
        .take()
        .expect("piped stdin")
        .write_all(text.as_bytes())?;
    child.wait()?;
    Ok(())
}

// ─── Workspace ───────────────────────────────────────────────────────────────

pub(crate) fn logs_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // Source / tail state. `CMPX_START_LOG` seeds the initially-open file (a dev /
    // smoke-test deep-link; empty in normal use → freshest known source is shown).
    let (selected, set_selected) = cx.use_state(std::env::var("CMPX_START_LOG").unwrap_or_default());
    let (open_draft, set_open_draft) = cx.use_state(String::new());
    let (paused, set_paused) = cx.use_state(false);
    let (follow, set_follow) = cx.use_state(true);
    // Find state.
    let (find_draft, set_find_draft) = cx.use_state(String::new());
    let (find_query, set_find_query) = cx.use_state(String::new());
    let (find_regex, set_find_regex) = cx.use_state(false);
    let (find_case, set_find_case) = cx.use_state(false);
    let (match_cursor, set_match_cursor) = cx.use_state(0usize);
    // Filter state.
    let (filter_draft, set_filter_draft) = cx.use_state(String::new());
    let (filter_text, set_filter_text) = cx.use_state(String::new());
    let (sev_min, set_sev_min) = cx.use_state(0u8);
    // Details / selection.
    let (details_open, set_details_open) = cx.use_state(false);
    let (selected_id, set_selected_id) = cx.use_state(None::<u64>);
    // Bumped on every explicit find jump (submit / next / prev / case / regex) so the
    // list re-centres even when the target index repeats, and never scrolls on organic
    // index drift. Feeds the reactor `scroll_gen` primitive.
    let (scroll_gen, bump_scroll) = cx.use_reducer(0_u64);

    // Live tail: a 2s timer bumps `tick`, re-rendering so the file-size re-check
    // below re-keys the entry resource when the file grows.
    let (tick, bump) = cx.use_reducer(0_u64);
    let timer = cx.use_ref::<Option<DispatcherTimer>>(None);
    {
        let slot = timer.clone();
        let bump = bump.clone();
        cx.use_effect((), move || {
            let t = DispatcherTimer::new(Duration::from_secs(2), move || bump.call(|n| n + 1))
                .expect("DispatcherQueue.CreateTimer");
            slot.set(Some(t));
        });
    }

    let sources = cx.use_resource(|_: u64| list_log_sources(), tick / 5);

    let effective = if !selected.is_empty() {
        selected.clone()
    } else {
        sources
            .data()
            .and_then(|s| s.first())
            .map(|s| s.path.clone())
            .unwrap_or_default()
    };

    // When paused, freeze the resource key (size = 0) so file growth no longer
    // triggers a re-read; when live, key on the real size so a grown file refetches.
    let size_key = if paused {
        0
    } else {
        std::fs::metadata(&effective).map(|m| m.len()).unwrap_or(0)
    };
    let parsed = cx.use_resource(
        |key: (String, u64)| {
            let (path, _size) = key;
            read_log_full(&path)
        },
        (effective.clone(), size_key),
    );

    let list_h = list_height(cx);

    // ── Left pane: open-by-path box + known-source list ──────────────────────
    let open_box = crate::search_box(
        open_draft,
        "Paste a .log path, press Enter",
        set_open_draft,
        set_selected.clone(),
    );
    let open_btn = button("Open…").on_click({
        let s = set_selected.clone();
        move || {
            if let Some(p) = crate::dialogs::pick_open_file(
                "Open a log file",
                &[("Logs", &["log", "txt", "cmtrace"]), ("All files", &["*"])],
            ) {
                s.call(p);
            }
        }
    });

    let eff_for_list = effective.clone();
    let set_selected_l = set_selected.clone();
    let left_list: Element = sources
        .view(move |srcs: &Vec<LogSource>| -> Element {
            if srcs.is_empty() {
                return body("No known log sources on this device — open a file by path above.")
                    .opacity(0.6)
                    .wrap()
                    .into();
            }
            let sel_index = srcs
                .iter()
                .position(|s| s.path == eff_for_list)
                .map(|i| i as i32)
                .unwrap_or(-1);
            let paths: Vec<String> = srcs.iter().map(|s| s.path.clone()).collect();
            let set_sel = set_selected_l.clone();
            list_view(srcs.clone(), |s: &LogSource, _| source_row(s))
                .with_key_selector(|s: &LogSource| s.path.clone())
                .selected_index(sel_index)
                .on_selection_changed(move |i| {
                    if let Some(p) = paths.get(i as usize) {
                        set_sel.call(p.clone());
                    }
                })
                .height((list_h - 60.0).max(120.0))
                .into()
        })
        .error(|e| error_box(e))
        .into();

    let open_row = hstack((Element::from(open_box), Element::from(open_btn))).spacing(6.0);
    let left = fluid_fill(8.0, vec![Element::from(open_row)], left_list);

    // ── Right pane: find + filter bars, stats, column header, grid, details ──
    let file_label = Path::new(&effective)
        .file_name()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_else(|| "no file selected".into());

    let stream: Element = parsed
        .view(move |p: &Parsed| -> Element {
            if p.rows.is_empty() {
                return body("No log entries.").opacity(0.6).into();
            }
            let cols = columns_for(p.kind);

            // Filter (severity floor + contains).
            let filter_lc = filter_text.to_lowercase();
            let filtered: Vec<Arc<LogRow>> = p
                .rows
                .iter()
                .filter(|r| passes_filter(r, &filter_lc, sev_min))
                .cloned()
                .collect();

            // Find over the filtered set.
            let query = find_query.clone();
            let find_active = !query.is_empty();
            // `case_insensitive` makes the Aa toggle the default rather than a
            // prependable `(?i)` that an inline `(?-i)` in the query could override.
            let re = if find_active && find_regex {
                RegexBuilder::new(&query)
                    .case_insensitive(!find_case)
                    .build()
                    .ok()
            } else {
                None
            };
            let regex_invalid = find_active && find_regex && re.is_none();
            let needle = if find_case {
                query.clone()
            } else {
                query.to_lowercase()
            };
            let match_positions: Vec<usize> = if find_active && !regex_invalid {
                filtered
                    .iter()
                    .enumerate()
                    .filter(|(_, r)| {
                        if let Some(re) = &re {
                            re.is_match(&r.message) || re.is_match(&r.component)
                        } else if find_case {
                            r.message.contains(&needle) || r.component.contains(&needle)
                        } else {
                            r.message.to_lowercase().contains(&needle)
                                || r.component.to_lowercase().contains(&needle)
                        }
                    })
                    .map(|(i, _)| i)
                    .collect()
            } else {
                Vec::new()
            };
            let match_count = match_positions.len();
            let cursor = if match_count > 0 {
                match_cursor % match_count
            } else {
                0
            };
            let current_pos = match_positions.get(cursor).copied();
            let current_id = current_pos.and_then(|i| filtered.get(i)).map(|r| r.id);
            let match_set: HashSet<u64> = match_positions
                .iter()
                .filter_map(|&i| filtered.get(i))
                .map(|r| r.id)
                .collect();
            let mc = match_count;
            let cur = cursor;

            let sel_index = selected_id
                .and_then(|id| filtered.iter().position(|r| r.id == id))
                .map(|i| i as i32)
                .unwrap_or(-1);
            let sel_row = selected_id.and_then(|id| filtered.iter().find(|r| r.id == id).cloned());
            let inner_h = (list_h - 150.0).max(140.0);

            // The virtualized grid.
            let cols_c = cols.clone();
            let ids: Vec<u64> = filtered.iter().map(|r| r.id).collect();
            let ssel = set_selected_id.clone();
            let list_el: Element = list_view(filtered.clone(), move |r: &Arc<LogRow>, _| {
                let hl = if Some(r.id) == current_id {
                    RowHl::Current
                } else if match_set.contains(&r.id) {
                    RowHl::Match
                } else {
                    RowHl::None
                };
                row_grid(r, &cols_c, hl)
            })
            .with_key_selector(|r: &Arc<LogRow>| r.id.to_string())
            .selected_index(sel_index)
            .on_selection_changed(move |i| {
                if let Some(id) = ids.get(i as usize) {
                    ssel.call(Some(*id));
                }
            })
            .follow_tail(follow && !paused && !find_active)
            .scroll_to(current_pos.map(|i| i as i32))
            .scroll_gen(scroll_gen)
            .height(inner_h)
            .into();

            // List, plus an optional details pane.
            let body_area: Element = if details_open {
                let detail_el = match &sel_row {
                    Some(r) => details_panel(r, inner_h),
                    None => Element::from(
                        border(Element::from(
                            caption("Select a row to see its full detail.").opacity(0.5),
                        ))
                        .background(theme::SURFACE)
                        .corner_radius(4.0)
                        .padding(Thickness::uniform(12.0)),
                    ),
                };
                grid((list_el.grid_column(0), detail_el.grid_column(1)))
                    .columns([GridLength::Star(1.0), GridLength::Pixel(340.0)])
                    .column_spacing(12.0)
                    .into()
            } else {
                list_el
            };

            // Find bar.
            let find_input: Element = auto_suggest_box(find_draft.clone())
                .placeholder_text("Find (message / component)".to_string())
                .on_text_changed({
                    let s = set_find_draft.clone();
                    move |t| s.call(t)
                })
                .on_query_submitted({
                    let sq = set_find_query.clone();
                    let sc = set_match_cursor.clone();
                    let sg = bump_scroll.clone();
                    move |t| {
                        sq.call(t);
                        sc.call(0);
                        sg.call(|n| n + 1);
                    }
                })
                .into();
            let count_txt = if regex_invalid {
                "invalid regex".to_string()
            } else if !find_active {
                String::new()
            } else if match_count == 0 {
                "no matches".to_string()
            } else {
                format!("{} / {}", cursor + 1, match_count)
            };
            let count_el: Element = if regex_invalid {
                Element::from(caption(count_txt).foreground(theme::SEV_ERROR))
            } else {
                Element::from(caption(count_txt).opacity(0.6))
            };
            let find_bar: Element = hstack(vec![
                Element::from(caption("Find").opacity(0.6)),
                find_input,
                toggle_button("Aa", find_case, {
                    let s = set_find_case.clone();
                    let sc = set_match_cursor.clone();
                    let sg = bump_scroll.clone();
                    move || {
                        s.call(!find_case);
                        sc.call(0);
                        sg.call(|n| n + 1);
                    }
                }),
                toggle_button(".*", find_regex, {
                    let s = set_find_regex.clone();
                    let sc = set_match_cursor.clone();
                    let sg = bump_scroll.clone();
                    move || {
                        s.call(!find_regex);
                        sc.call(0);
                        sg.call(|n| n + 1);
                    }
                }),
                Element::from(button("◀").on_click({
                    let s = set_match_cursor.clone();
                    let sg = bump_scroll.clone();
                    move || {
                        if mc > 0 {
                            s.call((cur + mc - 1) % mc);
                            sg.call(|n| n + 1);
                        }
                    }
                })),
                Element::from(button("▶").on_click({
                    let s = set_match_cursor.clone();
                    let sg = bump_scroll.clone();
                    move || {
                        if mc > 0 {
                            s.call((cur + 1) % mc);
                            sg.call(|n| n + 1);
                        }
                    }
                })),
                count_el,
            ])
            .spacing(6.0)
            .into();

            // Filter bar.
            let filter_input: Element = auto_suggest_box(filter_draft.clone())
                .placeholder_text("Filter rows containing…".to_string())
                .on_text_changed({
                    let s = set_filter_draft.clone();
                    move |t| s.call(t)
                })
                .on_query_submitted({
                    let s = set_filter_text.clone();
                    let sc = set_match_cursor.clone();
                    move |t| {
                        s.call(t);
                        sc.call(0);
                    }
                })
                .into();
            let sev_labels = ["All", "Warn+", "Error"];
            let mut fbar: Vec<Element> = vec![
                Element::from(caption("Filter").opacity(0.6)),
                filter_input,
            ];
            for lvl in 0u8..3 {
                let s = set_sev_min.clone();
                let sc = set_match_cursor.clone();
                fbar.push(toggle_button(sev_labels[lvl as usize], sev_min == lvl, move || {
                    s.call(lvl);
                    sc.call(0);
                }));
            }
            fbar.push(toggle_button("Details", details_open, {
                let s = set_details_open.clone();
                move || s.call(!details_open)
            }));
            fbar.push(Element::from(
                caption(format!("{} shown", filtered.len())).opacity(0.5),
            ));
            let filter_bar: Element = hstack(fbar).spacing(6.0).into();

            // Stats + tail controls.
            let mut summary = format!(
                "{file_label}  ·  {}  ·  {} lines  ·  {} err  ·  {} warn",
                kind_label(p.kind),
                p.total_lines,
                p.errors,
                p.warnings,
            );
            if p.parse_errors > 0 {
                summary.push_str(&format!("  ·  {} unparsed", p.parse_errors));
            }
            if p.truncated {
                summary.push_str(&format!("  ·  showing last {MAX_ROWS}"));
            }
            let tail_state = if paused { "paused" } else { "live · 2s" };
            let top_bar: Element = hstack(vec![
                Element::from(caption(summary).opacity(0.75)),
                Element::from(
                    caption(format!("· {} · {} · {tail_state}", p.provenance, p.quality))
                        .opacity(0.5),
                ),
                Element::from(button(if paused { "Resume" } else { "Pause" }).on_click({
                    let s = set_paused.clone();
                    move || s.call(!paused)
                })),
                toggle_button("Follow", follow, {
                    let s = set_follow.clone();
                    move || s.call(!follow)
                }),
                Element::from(button("Copy row").on_click({
                    let line = sel_row.as_ref().map(|r| {
                        format!(
                            "{}\t{}\t{}\t{}\t{}",
                            r.line,
                            r.time,
                            sev_label(r.severity),
                            r.component,
                            r.message
                        )
                    });
                    move || {
                        if let Some(l) = &line {
                            let _ = copy_to_clipboard(l);
                        }
                    }
                })),
            ])
            .spacing(8.0)
            .into();

            Element::from(fluid_fill(
                6.0,
                vec![find_bar, filter_bar, top_bar, header_grid(&cols)],
                body_area,
            ))
            .keyboard_accelerator(KeyboardAccelerator::new(
                VirtualKey::F3,
                VirtualKeyModifiers::None,
                {
                    let s = set_match_cursor.clone();
                    let sg = bump_scroll.clone();
                    move || {
                        if mc > 0 {
                            s.call((cur + 1) % mc);
                            sg.call(|n| n + 1);
                        }
                    }
                },
            ))
            .keyboard_accelerator(KeyboardAccelerator::new(
                VirtualKey::F3,
                VirtualKeyModifiers::Shift,
                {
                    let s = set_match_cursor.clone();
                    let sg = bump_scroll.clone();
                    move || {
                        if mc > 0 {
                            s.call((cur + mc - 1) % mc);
                            sg.call(|n| n + 1);
                        }
                    }
                },
            ))
            .keyboard_accelerator(KeyboardAccelerator::new(
                VirtualKey::H,
                VirtualKeyModifiers::Control,
                {
                    let s = set_details_open.clone();
                    move || s.call(!details_open)
                },
            ))
        })
        .loading(caption("reading log…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    grid((left.grid_column(0), stream.grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(3.0)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
