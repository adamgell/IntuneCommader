//! M12.1 cache-dev (Cache Sync) — the dev/admin face of the LiteDB read-through
//! blob cache (docs/CACHE-M12.1.md). A header (summary + Warm + Evict-all buttons)
//! over an inspect grid: one row per cacheable LIST key with its age, item count and
//! a warm-ahead badge.
//!
//! Data comes from the sidecar's GET /cache (per-key status) + GET /cache/summary
//! (header), with POST /cache/warm and POST /cache/evict for the actions. DETAIL keys
//! ({key}/{id}) are lazy-only and not enumerable, so the grid shows LIST-key coverage
//! only — it does not claim per-object cache coverage.

use api_types::{CacheEntryStatus, CacheSummary};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

// Pending cache action, dispatched by the action resource below. `None` = idle.
#[derive(Clone, Copy, PartialEq, Eq, Hash)]
enum Action {
    None,
    Warm,
    ForceWarm,
    EvictAll,
}

fn run_action(a: Action) -> std::result::Result<Option<String>, String> {
    match a {
        Action::None => Ok(None),
        Action::Warm => api().cache_warm(false).map(|_| Some("Warm started.".to_string())).map_err(service_err),
        Action::ForceWarm => api()
            .cache_warm(true)
            .map(|_| Some("Forced warm started — every key re-fetched.".to_string()))
            .map_err(service_err),
        Action::EvictAll => api()
            .cache_evict(None)
            .map(|_| Some("Evicted all cached keys for this tenant.".to_string()))
            .map_err(service_err),
    }
}

// Relative age string for a cachedAtUtc ISO timestamp (e.g. "3m", "2h", "1d").
fn age(iso: &str) -> String {
    let parsed = chrono_lite_parse(iso);
    match parsed {
        Some(secs) if secs < 60 => format!("{secs}s"),
        Some(secs) if secs < 3600 => format!("{}m", secs / 60),
        Some(secs) if secs < 86400 => format!("{}h", secs / 3600),
        Some(secs) => format!("{}d", secs / 86400),
        None => iso.split('T').next().unwrap_or(iso).to_string(),
    }
}

// Seconds since the given ISO-8601 UTC instant, or None if unparseable. Kept
// dependency-free (no chrono in this crate) — parses the "o"/round-trip format the
// sidecar emits (yyyy-MM-ddTHH:mm:ss[.fffffff]Z or +00:00).
fn chrono_lite_parse(iso: &str) -> Option<i64> {
    // Split date and time on 'T'.
    let (date, rest) = iso.split_once('T')?;
    let mut dp = date.split('-');
    let y: i64 = dp.next()?.parse().ok()?;
    let mo: i64 = dp.next()?.parse().ok()?;
    let d: i64 = dp.next()?.parse().ok()?;
    // Strip a trailing zone designator (Z or +hh:mm / -hh:mm) and fractional seconds.
    let timepart = rest
        .trim_end_matches('Z')
        .split(['+', '-'])
        .next()
        .unwrap_or(rest);
    let timepart = timepart.split('.').next().unwrap_or(timepart);
    let mut tp = timepart.split(':');
    let hh: i64 = tp.next()?.parse().ok()?;
    let mm: i64 = tp.next()?.parse().ok()?;
    let ss: i64 = tp.next().unwrap_or("0").parse().ok()?;
    // Days since epoch via a civil-from-days algorithm (Howard Hinnant's).
    let yy = if mo <= 2 { y - 1 } else { y };
    let era = (if yy >= 0 { yy } else { yy - 399 }) / 400;
    let yoe = yy - era * 400;
    let doy = (153 * (if mo > 2 { mo - 3 } else { mo + 9 }) + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    let days = era * 146097 + doe - 719468;
    let captured = days * 86400 + hh * 3600 + mm * 60 + ss;
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .ok()?
        .as_secs() as i64;
    Some((now - captured).max(0))
}

// A small pill badge with the given text + color.
fn badge(text: &str, color: Color) -> Element {
    border(
        caption(text.to_string()).font_size(11.0).font_family(theme::FONT_UI).foreground(color),
    )
    .background(theme::SURFACE_2)
    .border_brush(theme::LINE)
    .corner_radius(4.0)
    .padding(Thickness::xy(6.0, 2.0))
    .into()
}

// One status card: small uppercase label over a large display number.
fn stat_card(label: &str, value: String) -> Element {
    border(
        vstack((
            Element::from(
                caption(label.to_uppercase()).font_family(theme::FONT_UI).foreground(theme::TEXT_4),
            ),
            Element::from(
                body_strong(value)
                    .font_size(24.0)
                    .font_family(theme::FONT_DISPLAY)
                    .foreground(theme::TEXT),
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

// Four-column header for the inspect grid (shared by header row + each entry).
fn entry_row_grid(key: Element, age: Element, count: Element, warm: Element) -> Element {
    grid((
        key.grid_column(0),
        age.grid_column(1),
        count.grid_column(2),
        warm.grid_column(3),
    ))
    .columns([
        GridLength::Star(2.0),
        GridLength::Pixel(90.0),
        GridLength::Pixel(90.0),
        GridLength::Pixel(110.0),
    ])
    .column_spacing(8.0)
    .margin(Thickness::xy(0.0, 3.0))
    .into()
}

fn entry_row(e: &CacheEntryStatus) -> Element {
    let (age_el, count_el) = match &e.cached_at_utc {
        Some(iso) => (
            caption(age(iso)).foreground(theme::OK).font_family(theme::FONT_MONO).into(),
            caption(e.item_count.to_string()).foreground(theme::TEXT_2).font_family(theme::FONT_MONO).into(),
        ),
        None => (
            caption("—").foreground(theme::TEXT_4).font_family(theme::FONT_MONO).into(),
            caption("—").foreground(theme::TEXT_4).font_family(theme::FONT_MONO).into(),
        ),
    };
    let warm_el: Element = if e.warm_ahead {
        badge("warm-ahead", theme::BRAND_BRIGHT)
    } else {
        badge("lazy", theme::TEXT_3)
    };
    let name = vstack((
        Element::from(
            body_strong(e.display_name.clone()).font_family(theme::FONT_UI).foreground(theme::TEXT).wrap(),
        ),
        Element::from(caption(e.key.clone()).foreground(theme::TEXT_4).font_family(theme::FONT_MONO)),
    ))
    .spacing(1.0);
    entry_row_grid(name.into(), age_el, count_el, warm_el)
}

pub fn cache_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // `data_tick` re-keys (refetches) the status + summary resources; bumped on
    // Refresh and after any action completes. `action`/`action_tick` drive the
    // fire-and-forget Warm/Evict resource (bulk.rs busy-flag idiom).
    let (data_tick, bump_data) = cx.use_reducer(0_u64);
    let (action, set_action) = cx.use_state(Action::None);
    let (action_tick, bump_action) = cx.use_reducer(0_u64);

    let summary = cx.use_resource(
        |_: u64| api().cache_summary().map_err(service_err),
        data_tick,
    );
    let entries = cx.use_resource(
        |_: u64| api().cache_status().map_err(service_err),
        data_tick,
    );

    // The action resource: runs the pending action when triggered. On success a
    // `use_effect` (below) bumps the data tick so the grid + summary refetch the
    // post-action state — same shape as screen_devices' action-done effect (the bump
    // happens on the UI thread, never inside the background fetcher).
    let action_res = cx.use_resource(
        |key: (Action, u64)| -> std::result::Result<Option<String>, String> {
            let (a, _) = key;
            run_action(a)
        },
        (action, action_tick),
    );
    let busy = action_res.is_loading();

    // After an action reports a result message, refetch the cache status + summary.
    let action_done = matches!(action_res.data(), Some(Some(_)));
    {
        let bump_data = bump_data.clone();
        cx.use_effect(action_done, move || {
            if action_done {
                bump_data.call(|n| n + 1);
            }
        });
    }

    let warm_btn = button(if busy { "Working…" } else { "Warm" })
        .accent()
        .enabled(!busy)
        .on_click({
            let set_action = set_action.clone();
            let bump_action = bump_action.clone();
            move || {
                set_action.call(Action::Warm);
                bump_action.call(|n| n + 1);
            }
        });
    let force_btn = button("Force warm")
        .enabled(!busy)
        .on_click({
            let set_action = set_action.clone();
            let bump_action = bump_action.clone();
            move || {
                set_action.call(Action::ForceWarm);
                bump_action.call(|n| n + 1);
            }
        });
    let evict_btn = button("Evict all")
        .enabled(!busy)
        .on_click({
            let set_action = set_action.clone();
            let bump_action = bump_action.clone();
            move || {
                set_action.call(Action::EvictAll);
                bump_action.call(|n| n + 1);
            }
        });
    let refresh_btn = button("Refresh")
        .enabled(!busy)
        .on_click({
            let bump_data = bump_data.clone();
            move || bump_data.call(|n| n + 1)
        });

    // Summary stat cards + availability banner.
    let header: Element = summary
        .view(|s: &CacheSummary| -> Element {
            let last = s
                .last_warmed_utc
                .as_deref()
                .map(|i| format!("{} ago", age(i)))
                .unwrap_or_else(|| "never".to_string());
            let cards = grid((
                stat_card("Live keys", s.entry_count.to_string()).grid_column(0),
                stat_card("Cached items", s.total_items.to_string()).grid_column(1),
                stat_card("Last warmed", last).grid_column(2),
            ))
            .columns([GridLength::Star(1.0), GridLength::Star(1.0), GridLength::Star(1.0)])
            .column_spacing(10.0);

            if s.available {
                Element::from(cards)
            } else {
                let banner = border(
                    body("Cache unavailable — the LiteDB blob cache is disabled (NullCacheService). \
                          Lists pass through to live Graph.")
                        .foreground(theme::WARN)
                        .wrap(),
                )
                .background(theme::SURFACE_2)
                .border_brush(theme::LINE)
                .corner_radius(8.0)
                .padding(Thickness::uniform(12.0));
                vstack((Element::from(banner), Element::from(cards))).spacing(10.0).into()
            }
        })
        .loading(caption("loading cache summary…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    // Action result line (started / error message).
    let action_msg: Element = action_res
        .view(|r: &Option<String>| -> Element {
            match r {
                Some(m) => caption(m.clone()).foreground(theme::OK).font_family(theme::FONT_UI).into(),
                None => Element::Empty,
            }
        })
        .loading(caption("working…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let toolbar = hstack((
        Element::from(warm_btn),
        Element::from(force_btn),
        Element::from(evict_btn),
        Element::from(refresh_btn),
        action_msg,
    ))
    .spacing(8.0);

    let intro = caption(
        "Inspect, warm and evict the per-tenant read-through blob cache. LIST keys only — \
         per-object DETAIL keys fill lazily and aren't listed.",
    )
    .opacity(0.7)
    .wrap();

    let list_height = crate::list_height(cx);

    // Inspect grid: a fixed header row + a scrolling list of entries.
    let grid_header = entry_row_grid(
        caption("SURFACE / KEY").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("AGE").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("ITEMS").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("PREFETCH").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
    );

    let grid_el: Element = entries
        .view(move |rows: &Vec<CacheEntryStatus>| -> Element {
            if rows.is_empty() {
                return body("Sign in (top right) to inspect the cache.").opacity(0.6).wrap().into();
            }
            list_view(rows.clone(), |e: &CacheEntryStatus, _| entry_row(e))
                .with_key_selector(|e: &CacheEntryStatus| e.key.clone())
                .height(list_height - 60.0)
                .into()
        })
        .loading(caption("loading cache status…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let body_col = crate::fluid_fill(
        10.0,
        vec![
            Element::from(header),
            Element::from(toolbar),
            Element::from(intro),
            Element::from(grid_header),
        ],
        grid_el,
    );

    border(body_col).margin(Thickness::uniform(16.0)).into()
}
