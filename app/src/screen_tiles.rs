//! Generic "tiles" workspace — fetches a `Vec<ListItem>` from a sidecar path and
//! renders it as a grid of metric cards. Reused by Dashboard and Security Posture,
//! whose endpoints return metric tiles as `ListItem`s: `title` = metric name,
//! `subtitle` = description, `badge` = the count/value string.

use api_types::ListItem;
use windows_reactor::*;

use crate::theme;

// Parse a tile badge like "1,234" / "57" into a number (thousands separators stripped).
fn badge_num(it: &ListItem) -> Option<f64> {
    it.badge.as_ref().and_then(|b| b.replace(',', "").trim().parse::<f64>().ok())
}

// A titled card wrapper for a chart block.
fn chart_card(title: &str, body_el: Element) -> Element {
    border(
        vstack((
            Element::from(body_strong(title.to_string()).foreground(theme::TEXT_2)),
            body_el,
        ))
        .spacing(12.0),
    )
    .background(theme::SURFACE_2)
    .border_brush(theme::LINE)
    .corner_radius(8.0)
    .padding(Thickness::uniform(16.0))
    .into()
}

/// Dispatched as `component(tiles_workspace, "<path>")`, so the prop arrives as a
/// `&&'static str`; we immediately re-borrow it to a plain `&str`.
pub fn tiles_workspace(path: &&'static str, cx: &mut RenderCx) -> Element {
    let path: &str = path;

    // Refresh tick — bumping it re-keys (and thus refetches) the resource below.
    let (tick, bump) = cx.use_reducer(0_u64);

    let res = cx.use_resource(
        |key: (String, u64)| {
            let (p, _) = key;
            crate::api_client::api()
                .get_list(&p)
                .map_err(crate::api_client::service_err)
        },
        (path.to_string(), tick),
    );

    let header = hstack((
        body_strong("Metrics".to_string()),
        button("Refresh").on_click(move || bump.call(|n| n + 1)),
    ))
    .spacing(12.0);

    let cards: Element = res
        .view(|items: &Vec<ListItem>| -> Element {
            if items.is_empty() {
                return body("Sign in (top right) to load.").opacity(0.6).into();
            }

            // Chart 1 — a bar chart of every tile whose badge is numeric (covers the
            // Dashboard inventory counts and the Security Posture numbers alike).
            let numeric: Vec<(String, f64)> = items
                .iter()
                .filter_map(|it| badge_num(it).map(|v| (it.title.clone(), v)))
                .collect();
            let bar_card: Element = if numeric.len() >= 2 {
                chart_card("Counts", crate::charts::bar_chart(&numeric))
            } else {
                Element::Empty
            };

            // Chart 2 — a compliance stacked bar, only when this surface carries the
            // device-compliance tiles (Security Posture). Keyed off the stable tile ids.
            let by_id = |id: &str| items.iter().find(|it| it.id == id).and_then(badge_num);
            let compliance_card: Element = match (by_id("total-devices"), by_id("compliant"), by_id("noncompliant")) {
                (Some(total), Some(comp), Some(noncomp)) => {
                    let other = (total - comp - noncomp).max(0.0);
                    chart_card(
                        "Device compliance",
                        crate::charts::stacked_bar(&[
                            ("Compliant".to_string(), comp, theme::OK),
                            ("Noncompliant".to_string(), noncomp, theme::ERROR),
                            ("Other / unknown".to_string(), other, theme::TEXT_4),
                        ]),
                    )
                }
                _ => Element::Empty,
            };

            // Chunk the tiles into rows of 4 and build a vstack of hstacks. Each
            // chunk is cloned into an owned Vec so the produced widgets capture no
            // borrows of `items`.
            let rows: Vec<Element> = items
                .chunks(4)
                .map(|chunk| {
                    let cells: Vec<Element> = chunk.iter().map(tile_card).collect();
                    hstack(cells).spacing(12.0).into()
                })
                .collect();

            vstack((
                bar_card,
                compliance_card,
                Element::from(vstack(rows).spacing(12.0)),
            ))
            .spacing(16.0)
            .into()
        })
        .loading(caption("loading…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    vstack((header, cards))
        .spacing(12.0)
        .margin(Thickness::uniform(16.0))
        .into()
}

/// A single metric card: big value/badge on top, then the metric name, then the
/// (wrapped) description. All strings are cloned so the card owns its data.
fn tile_card(item: &ListItem) -> Element {
    border(
        vstack((
            body_strong(item.badge.clone().unwrap_or_default()).font_size(30.0),
            body(item.title.clone()),
            caption(item.subtitle.clone()).opacity(0.7).wrap(),
        ))
        .spacing(4.0),
    )
    .corner_radius(8.0)
    .padding(Thickness::uniform(16.0))
    .into()
}
