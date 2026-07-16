//! Maester security-controls dashboard. Surfaces the sidecar's Maester integration
//! (maester365/maester): a per-category roll-up plus a sortable / filterable / searchable
//! table of controls, a live scanning view while a run is in progress, and a "Run checks
//! now" action that runs the suite against the signed-in tenant (sharing the session token)
//! and snapshots the run into the time-machine. Dispatched with a category filter
//! ("" = all; "Entra"/"Exchange"/…).
//!
//! Data from GET /maester/results; the run fires POST /maester/run (async) and the screen
//! polls GET /maester/run/status, auto-refreshing results when a pass completes.

use std::time::{Duration, SystemTime};

use api_types::{MaesterCategory, MaesterControl, MaesterRunResult, MaesterRunState};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;
use crate::{error_box, fluid_fill, list_height};

// Result buckets the filter chips toggle between.
const RESULT_FILTERS: [&str; 4] = ["Failed", "Passed", "Skipped", "All"];
// Sort keys, aligned with the sort dropdown order below.
const SORT_KEYS: [&str; 5] = ["", "severity", "service", "id", "title"];

// ── Table columns ────────────────────────────────────────────────────────────
#[derive(Clone, Copy, PartialEq)]
enum Col {
    Result,
    Severity,
    Service,
    Id,
    Title,
}
const COLS: [Col; 5] = [Col::Result, Col::Severity, Col::Service, Col::Id, Col::Title];

impl Col {
    fn label(self) -> &'static str {
        match self {
            Col::Result => "RESULT",
            Col::Severity => "SEVERITY",
            Col::Service => "SERVICE",
            Col::Id => "ID",
            Col::Title => "CHECK",
        }
    }
    fn width(self) -> GridLength {
        match self {
            Col::Result => GridLength::Pixel(82.0),
            Col::Severity => GridLength::Pixel(84.0),
            Col::Service => GridLength::Pixel(92.0),
            Col::Id => GridLength::Pixel(104.0),
            Col::Title => GridLength::Star(1.0),
        }
    }
}

fn result_color(result: &str) -> Color {
    match result {
        "Passed" => theme::OK,
        "Failed" | "Error" => theme::ERROR,
        _ => theme::TEXT_3, // Skipped / NotRun
    }
}

fn score_color(score: i32) -> Color {
    if score >= 70 {
        theme::OK
    } else if score >= 40 {
        theme::WARN
    } else {
        theme::ERROR
    }
}

// Sort order for the default view: failures first, then errors, skips, passes.
fn result_rank(r: &str) -> u8 {
    match r {
        "Failed" => 0,
        "Error" => 1,
        "Skipped" => 2,
        "NotRun" => 3,
        "Passed" => 4,
        _ => 5,
    }
}

// Within a bucket, most-severe first.
fn severity_rank(sev: Option<&str>) -> u8 {
    match sev.map(|s| s.to_ascii_lowercase()).as_deref() {
        Some("critical") => 0,
        Some("high") => 1,
        Some("medium") => 2,
        Some("low") => 3,
        Some("informational") | Some("info") => 4,
        _ => 5,
    }
}

// Distinct color per severity level (warm ramp): Critical red, High orange,
// Medium amber, Low muted, Informational/unknown faint. Mirrors severity_rank.
fn severity_color(sev: &str) -> Color {
    match sev.to_ascii_lowercase().as_str() {
        "critical" => theme::SEV_CRITICAL,
        "high" => theme::SEV_HIGH,
        "medium" => theme::SEV_MEDIUM,
        "low" => theme::SEV_LOW,
        _ => theme::TEXT_4, // informational / none
    }
}

// Does a control's result belong to the selected result bucket?
fn result_matches(result: &str, filter: &str) -> bool {
    match filter {
        "Failed" => result == "Failed" || result == "Error",
        "Passed" => result == "Passed",
        "Skipped" => result == "Skipped" || result == "NotRun",
        _ => true, // "All"
    }
}

fn card(child: impl Into<Element>) -> Element {
    border(child)
        .background(theme::SURFACE)
        .border_brush(theme::LINE)
        .corner_radius(12.0)
        .padding(Thickness::uniform(16.0))
        .into()
}

// One category roll-up card: name, score, and a passed/failed/skipped line.
fn category_card(c: &MaesterCategory) -> Element {
    let color = score_color(c.score);
    border(
        vstack((
            Element::from(
                caption(c.name.to_uppercase())
                    .font_family(theme::FONT_UI)
                    .foreground(theme::TEXT_4),
            ),
            Element::from(
                body_strong(format!("{}%", c.score))
                    .font_size(26.0)
                    .font_family(theme::FONT_DISPLAY)
                    .foreground(color),
            ),
            Element::from(
                caption(format!("{} pass · {} fail · {} skip", c.passed, c.failed, c.skipped))
                    .font_family(theme::FONT_UI)
                    .foreground(theme::TEXT_3)
                    .wrap(),
            ),
        ))
        .spacing(2.0),
    )
    .background(theme::SURFACE_2)
    .border_brush(theme::LINE)
    .corner_radius(10.0)
    .padding(Thickness::uniform(14.0))
    .into()
}

// One table cell for a control × column.
fn cell(c: &MaesterControl, col: Col) -> Element {
    match col {
        Col::Result => body(c.result.clone())
            .font_family(theme::FONT_UI)
            .font_size(12.0)
            .foreground(result_color(&c.result))
            .into(),
        Col::Severity => {
            let sev = c.severity.clone().unwrap_or_default();
            let (text, color) = if sev.is_empty() {
                ("—".to_string(), theme::TEXT_4)
            } else {
                (sev.clone(), severity_color(&sev))
            };
            body(text).font_family(theme::FONT_UI).font_size(12.0).foreground(color).into()
        }
        Col::Service => body(c.category.clone())
            .font_family(theme::FONT_UI)
            .font_size(12.0)
            .foreground(theme::TEXT_3)
            .into(),
        Col::Id => body(c.id.clone())
            .font_family(theme::FONT_MONO)
            .font_size(12.0)
            .foreground(theme::TEXT_3)
            .into(),
        Col::Title => body(c.title.clone()).font_size(12.0).foreground(theme::TEXT).into(),
    }
}

fn table_header() -> Element {
    let widths: Vec<GridLength> = COLS.iter().map(|c| c.width()).collect();
    let cells: Vec<Element> = COLS
        .iter()
        .enumerate()
        .map(|(i, &col)| {
            Element::from(
                caption(col.label())
                    .opacity(0.6)
                    .font_family(theme::FONT_MONO)
                    .font_size(10.0),
            )
            .grid_column(i as i32)
        })
        .collect();
    border(Element::from(grid(cells).columns(widths).column_spacing(8.0)))
        .border_brush(theme::LINE)
        .padding(Thickness::xy(8.0, 5.0))
        .into()
}

fn table_row(c: &MaesterControl) -> Element {
    let widths: Vec<GridLength> = COLS.iter().map(|c| c.width()).collect();
    let cells: Vec<Element> = COLS
        .iter()
        .enumerate()
        .map(|(i, &col)| cell(c, col).grid_column(i as i32))
        .collect();
    grid(cells)
        .columns(widths)
        .column_spacing(8.0)
        .margin(Thickness::xy(8.0, 3.0))
        .into()
}

// The live "scanning" card shown while a run is in progress: an animated ring, the current
// phase + elapsed timer, and — once the run reaches the checks phase — a determinate progress
// bar of completed / total checks. The table below stays visible (last run) and auto-refreshes
// the moment the scan completes.
fn scanning_card(elapsed_secs: u64, phase: &str, completed: i32, total: i32) -> Element {
    let mins = elapsed_secs / 60;
    let secs = elapsed_secs % 60;
    let elapsed = if mins > 0 { format!("{mins}m {secs:02}s") } else { format!("{secs}s") };
    let phase_label = if phase.is_empty() { "Scanning".to_string() } else { phase.to_string() };

    let header = hstack(vec![
        Element::from(ProgressRing::indeterminate().width(26.0).height(26.0)),
        Element::from(
            vstack(vec![
                Element::from(
                    body_strong(format!("{phase_label} · {elapsed}"))
                        .font_family(theme::FONT_UI)
                        .foreground(theme::TEXT),
                ),
                Element::from(
                    caption(
                        "Running the Maester security suite against the signed-in tenant. \
                         The table below refreshes automatically when the scan finishes.",
                    )
                    .foreground(theme::TEXT_3)
                    .wrap(),
                ),
            ])
            .spacing(1.0),
        ),
    ])
    .spacing(14.0);

    let mut items: Vec<Element> = vec![Element::from(header)];
    if total > 0 {
        let done = completed.min(total);
        let pct = (100.0 * done as f64 / total as f64).round() as i32;
        items.push(Element::from(ProgressBar::new(done as f64).range(0.0, total as f64)));
        items.push(Element::from(
            caption(format!("{done} / {total} checks · {pct}%"))
                .font_family(theme::FONT_UI)
                .foreground(theme::TEXT_2),
        ));
    }
    border(vstack(items).spacing(10.0))
        .background(theme::SURFACE)
        .border_brush(theme::BRAND)
        .corner_radius(12.0)
        .padding(Thickness::uniform(16.0))
        .into()
}

// Category-scoped counts (pass, fail, skip, total).
fn scope_counts(r: &MaesterRunResult, cat: &str) -> (i32, i32, i32, i32) {
    if cat.is_empty() {
        (r.passed, r.failed, r.skipped, r.total)
    } else if let Some(c) = r.categories.iter().find(|c| c.name == cat) {
        (c.passed, c.failed, c.skipped, c.total)
    } else {
        (0, 0, 0, 0)
    }
}

// Render a loaded run: summary + category roll-up + the filtered/sorted/searched control table.
fn render_results(r: &MaesterRunResult, cat: &str, res: &str, search: &str, sort: &str) -> Element {
    if r.total == 0 {
        return card(vstack((
            Element::from(body_strong("No checks run yet").foreground(theme::TEXT)),
            Element::from(
                caption(
                    "Click \u{201c}Run checks now\u{201d} to run the Maester security suite \
                     against the signed-in tenant. Results are categorized by service and \
                     stored in the time-machine so posture drift is tracked over time.",
                )
                .foreground(theme::TEXT_3)
                .wrap(),
            ),
        ))
        .spacing(6.0));
    }

    let all_cat = cat.is_empty();
    let cats: Vec<&MaesterCategory> = r.categories.iter().filter(|c| all_cat || c.name == cat).collect();
    let needle = search.to_lowercase();

    let mut controls: Vec<&MaesterControl> = r
        .controls
        .iter()
        .filter(|c| {
            (all_cat || c.category == cat)
                && result_matches(&c.result, res)
                && (needle.is_empty()
                    || c.id.to_lowercase().contains(&needle)
                    || c.title.to_lowercase().contains(&needle))
        })
        .collect();
    controls.sort_by(|a, b| match sort {
        "severity" => severity_rank(a.severity.as_deref())
            .cmp(&severity_rank(b.severity.as_deref()))
            .then_with(|| a.id.cmp(&b.id)),
        "service" => a.category.cmp(&b.category).then_with(|| a.id.cmp(&b.id)),
        "id" => a.id.cmp(&b.id),
        "title" => a.title.to_lowercase().cmp(&b.title.to_lowercase()),
        // default: failed-first, then severity, then id.
        _ => result_rank(&a.result)
            .cmp(&result_rank(&b.result))
            .then_with(|| severity_rank(a.severity.as_deref()).cmp(&severity_rank(b.severity.as_deref())))
            .then_with(|| a.id.cmp(&b.id)),
    });

    // Summary — headline pass rate for the current category scope.
    let (cp, cf, cs, ct) = scope_counts(r, cat);
    let denom = cp + cf;
    let rate = if denom == 0 { 100 } else { (100.0 * cp as f64 / denom as f64).round() as i32 };
    let last_run = r
        .executed_at
        .clone()
        .map(|t| format!("last run {t}"))
        .unwrap_or_else(|| "not yet run".to_string());
    let summary = card(vstack((
        Element::from(
            hstack((
                Element::from(
                    body_strong(format!("{rate}%"))
                        .font_size(30.0)
                        .font_family(theme::FONT_DISPLAY)
                        .foreground(score_color(rate)),
                ),
                Element::from(
                    vstack((
                        Element::from(
                            body_strong(if all_cat {
                                "All security controls".to_string()
                            } else {
                                format!("{cat} controls")
                            })
                            .font_family(theme::FONT_UI)
                            .foreground(theme::TEXT),
                        ),
                        Element::from(
                            caption(format!("{cp} passed · {cf} failed · {cs} skipped · {ct} total"))
                                .font_family(theme::FONT_UI)
                                .foreground(theme::TEXT_2),
                        ),
                    ))
                    .spacing(1.0),
                ),
            ))
            .spacing(12.0),
        ),
        Element::from(caption(last_run).foreground(theme::TEXT_4)),
    ))
    .spacing(6.0));

    // Category roll-up grid (rows of 3).
    let cat_rows: Vec<Element> = cats
        .chunks(3)
        .map(|chunk| {
            let cells: Vec<Element> = chunk.iter().map(|c| category_card(c)).collect();
            hstack(cells).spacing(12.0).into()
        })
        .collect();
    let cat_block = if cat_rows.is_empty() {
        Element::Empty
    } else {
        Element::from(vstack(cat_rows).spacing(12.0))
    };

    // Control table (header + rows; cap to keep the view light).
    const CAP: usize = 400;
    let total_matched = controls.len();
    let mut rows: Vec<Element> = vec![
        Element::from(
            body_strong(format!("{res} controls ({total_matched})"))
                .font_family(theme::FONT_UI)
                .foreground(theme::TEXT),
        ),
        table_header(),
    ];
    if controls.is_empty() {
        rows.push(Element::from(
            caption("No controls match the current filters/search.").foreground(theme::TEXT_4),
        ));
    }
    rows.extend(controls.iter().take(CAP).map(|c| table_row(c)));
    if total_matched > CAP {
        rows.push(Element::from(
            caption(format!("… and {} more (narrow with search/filter)", total_matched - CAP))
                .foreground(theme::TEXT_4),
        ));
    }
    let table = card(vstack(rows).spacing(3.0));

    vstack((Element::from(summary), cat_block, Element::from(table)))
        .spacing(16.0)
        .into()
}

pub fn maester_workspace(filter_prop: &&'static str, cx: &mut RenderCx) -> Element {
    let filter: &'static str = *filter_prop;

    // View state: result bucket, free-text search, sort key.
    let (sel_res, set_sel_res) = cx.use_state::<&'static str>("Failed");
    let (search, set_search) = cx.use_state(String::new());
    let (sort, set_sort) = cx.use_state(String::new());
    // Per-service filter chip ("" = all services). Only used in the All-Controls view —
    // service-specific nav items already scope by service.
    let (sel_service, set_sel_service) = cx.use_state(String::new());
    // Client-tracked start of the current run, for the live scanning elapsed timer.
    let (run_start, set_run_start) = cx.use_state::<Option<SystemTime>>(None);

    // Manual-refresh trigger for the stored results.
    let (tick, bump) = cx.use_reducer(0_u64);

    // Poll the cheap in-memory run status every 2s — drives the scanning state + auto-refresh.
    let (poll, bump_poll) = cx.use_reducer(0_u64);
    let timer = cx.use_ref::<Option<DispatcherTimer>>(None);
    {
        let slot = timer.clone();
        let bump_poll = bump_poll.clone();
        cx.use_effect((), move || {
            let t = DispatcherTimer::new(Duration::from_secs(2), move || bump_poll.call(|n| n + 1))
                .expect("DispatcherQueue.CreateTimer");
            slot.set(Some(t));
        });
    }
    let run_state =
        cx.use_resource(|_: u64| api().maester_run_status().map_err(service_err), poll);
    let status_str = run_state.data().map(|s| s.status.clone()).unwrap_or_default();
    let finished_at = run_state
        .data()
        .and_then(|s| s.finished_at.clone())
        .unwrap_or_default();

    let results = cx.use_resource(
        |_: (u64, String)| api().maester_results().map_err(service_err),
        (tick, finished_at),
    );

    // Fire the run off the UI thread (returns fast — 202); the status poll drives the rest.
    let (fire, do_fire) = cx.use_mutation::<MaesterRunState>();
    let fired = fire.data().is_some();
    {
        let bump_poll = bump_poll.clone();
        let do_fire = do_fire.clone();
        cx.use_effect(fired, move || {
            if fired {
                bump_poll.call(|n| n + 1);
                do_fire.reset();
            }
        });
    }

    let busy = status_str == "Running" || fire.is_loading();
    let lh = list_height(cx);

    // Stamp the run start when a scan begins, clear it when it ends — drives the elapsed timer.
    {
        let set_run_start = set_run_start.clone();
        cx.use_effect(busy, move || {
            set_run_start.call(if busy { Some(SystemTime::now()) } else { None });
        });
    }
    let elapsed_secs = run_start
        .and_then(|t| t.elapsed().ok())
        .map(|d| d.as_secs())
        .unwrap_or(0);
    // Live per-check progress from the run status (phase + completed/total counts).
    let (phase, completed, total) = run_state
        .data()
        .map(|s| (s.phase.clone().unwrap_or_default(), s.completed, s.total))
        .unwrap_or_default();

    let run_btn = button(if busy {
        "Running checks…"
    } else {
        "Run checks now"
    })
    .accent()
    .enabled(!busy)
    .on_click({
        let m = do_fire.clone();
        move || {
            m.fire(|| api().maester_run(None).map_err(service_err));
        }
    });

    let refresh_btn = button("Refresh").enabled(!busy).on_click({
        let bump = bump.clone();
        move || bump.call(|n| n + 1)
    });

    // Search box (filters the table by id / title, live).
    let search_box = text_box(search.clone())
        .placeholder_text("Search checks (id or title)…".to_string())
        .width(240.0)
        .on_text_changed({
            let set = set_search.clone();
            move |t: String| set.call(t)
        });

    // Sort dropdown.
    let sort_idx = SORT_KEYS.iter().position(|k| *k == sort.as_str()).unwrap_or(0) as i32;
    let sort_combo = ComboBox::new(vec![
        "Sort: failed first".to_string(),
        "Severity".to_string(),
        "Service".to_string(),
        "ID".to_string(),
        "Check A–Z".to_string(),
    ])
    .selected_index(sort_idx)
    .on_selection_changed({
        let set_sort = set_sort.clone();
        move |i: i32| {
            let k = SORT_KEYS.get(i as usize).copied().unwrap_or("");
            set_sort.call(k.to_string());
        }
    });

    let action_bar = hstack(vec![
        Element::from(run_btn),
        Element::from(refresh_btn),
        Element::from(search_box),
        Element::from(sort_combo),
    ])
    .spacing(8.0);

    // Result-filter chips (Failed / Passed / Skipped / All); active one is accented.
    let chip_row: Vec<Element> = RESULT_FILTERS
        .iter()
        .map(|&opt| {
            let mut b = button(opt);
            if sel_res == opt {
                b = b.accent();
            }
            let set = set_sel_res.clone();
            Element::from(b.on_click(move || set.call(opt)))
        })
        .collect();
    let filter_bar = hstack(chip_row).spacing(6.0);

    // Per-service filter chips — derived from the loaded run's categories. Only shown in the
    // All-Controls view (nav-scoped service views already fix the service). Selecting one scopes
    // the summary, roll-up cards and table to that service (same effect as its nav item); "All
    // services" clears it.
    let services: Vec<String> = if filter.is_empty() {
        results
            .data()
            .map(|r| r.categories.iter().map(|c| c.name.clone()).collect())
            .unwrap_or_default()
    } else {
        Vec::new()
    };
    let effective_cat: String =
        if filter.is_empty() { sel_service.as_str().to_string() } else { filter.to_string() };
    let service_bar: Element = if services.is_empty() {
        Element::Empty
    } else {
        let mut chips: Vec<Element> = Vec::with_capacity(services.len() + 1);
        let mut all = button("All services");
        if sel_service.as_str().is_empty() {
            all = all.accent();
        }
        {
            let set = set_sel_service.clone();
            chips.push(Element::from(all.on_click(move || set.call(String::new()))));
        }
        for svc in &services {
            let mut b = button(svc.clone());
            if sel_service.as_str() == svc.as_str() {
                b = b.accent();
            }
            let set = set_sel_service.clone();
            let svc_owned = svc.clone();
            chips.push(Element::from(b.on_click(move || set.call(svc_owned.clone()))));
        }
        Element::from(hstack(chips).spacing(6.0))
    };

    // Surface a failed *start* (e.g. 501 prerequisites) and a failed *run* (from status).
    let start_err: Element = fire
        .error()
        .map(|e| {
            Element::from(
                caption(format!("couldn’t start run: {e}"))
                    .foreground(theme::ERROR)
                    .wrap(),
            )
        })
        .unwrap_or(Element::Empty);
    let run_err: Element = run_state
        .data()
        .filter(|s| s.status == "Failed")
        .and_then(|s| s.error.clone())
        .map(|e| {
            Element::from(
                caption(format!("last run failed: {e}"))
                    .foreground(theme::ERROR)
                    .wrap(),
            )
        })
        .unwrap_or(Element::Empty);

    // Live scanning card while a run is in progress (table below stays visible + auto-refreshes).
    let scanning: Element = if busy {
        Element::from(scanning_card(elapsed_secs, &phase, completed, total))
    } else {
        Element::Empty
    };

    let search_c = search.clone();
    let sort_c = sort.clone();
    let cat_c = effective_cat.clone();
    let body_el: Element = results
        .view(move |r: &MaesterRunResult| -> Element {
            render_results(r, &cat_c, sel_res, &search_c, &sort_c)
        })
        .loading(caption("loading results…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    fluid_fill(
        12.0,
        vec![
            Element::from(action_bar),
            Element::from(filter_bar),
            service_bar,
            start_err,
            run_err,
        ],
        scroll_viewer(vstack(vec![scanning, body_el]).spacing(16.0))
            .height(lh)
            .into(),
    )
    .margin(Thickness::uniform(16.0))
    .into()
}
