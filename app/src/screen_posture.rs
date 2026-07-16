//! Security Posture dashboard — like-to-like with IntuneCommander: a two-column
//! layout with a score gauge + weighted breakdown on the left, and stat cards,
//! severity-ranked gaps, and CA/Compliance detail tables on the right. Data from
//! the sidecar's /security-posture/summary. Brand colors via theme.rs.

use api_types::{ScoreCategory, SecurityGap, SecurityPosture};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

fn score_color(score: i32) -> Color {
    if score >= 70 {
        theme::OK
    } else if score >= 40 {
        theme::WARN
    } else {
        theme::ERROR
    }
}

// A bordered surface card.
fn card(child: impl Into<Element>) -> Element {
    border(child)
        .background(theme::SURFACE)
        .border_brush(theme::LINE)
        .corner_radius(12.0)
        .padding(Thickness::uniform(16.0))
        .into()
}

// Circular score gauge (graded ring + big number) — the reactor has no SVG, so a
// thick circular border stands in for IntuneCommander's arc ring.
fn gauge(score: i32) -> Element {
    let color = score_color(score);
    let inner = vstack((
        Element::from(
            body_strong(score.to_string())
                .font_size(40.0)
                .font_family(theme::FONT_DISPLAY)
                .foreground(color),
        ),
        Element::from(caption("/ 100").foreground(theme::TEXT_3)),
    ))
    .spacing(0.0)
    .horizontal_alignment(HorizontalAlignment::Center)
    .vertical_alignment(VerticalAlignment::Center);
    let ring = border(inner)
        .width(130.0)
        .height(130.0)
        .corner_radius(65.0)
        .border_thickness(Thickness::uniform(8.0))
        .border_brush(color)
        .background(theme::SURFACE_2)
        .horizontal_alignment(HorizontalAlignment::Center);
    card(vstack((
        Element::from(ring),
        Element::from(
            caption("Security Score")
                .foreground(theme::TEXT_2)
                .horizontal_alignment(HorizontalAlignment::Center),
        ),
    ))
    .spacing(10.0)
    .horizontal_alignment(HorizontalAlignment::Center))
}

// One category breakdown: name + score/max, a graded bar, and the item list.
fn category_bar(c: &ScoreCategory) -> Element {
    let frac = if c.max_score > 0 {
        (c.score as f64 / c.max_score as f64).clamp(0.0, 1.0)
    } else {
        1.0
    };
    let bar_color = if frac >= 0.7 {
        theme::OK
    } else if frac >= 0.4 {
        theme::WARN
    } else {
        theme::ERROR
    };
    let track_w = 240.0;
    let fill = border(Element::Empty)
        .background(bar_color)
        .corner_radius(3.0)
        .height(6.0)
        .width(track_w * frac)
        .horizontal_alignment(HorizontalAlignment::Left);
    let track = border(Element::from(fill))
        .background(theme::SURFACE_2)
        .corner_radius(3.0)
        .height(6.0)
        .width(track_w);
    vstack((
        Element::from(
            hstack((
                Element::from(
                    caption(c.category.clone())
                        .font_family(theme::FONT_UI)
                        .foreground(theme::TEXT),
                ),
                Element::from(
                    caption(format!("{} / {}", c.score, c.max_score))
                        .font_family(theme::FONT_MONO)
                        .foreground(theme::TEXT_3),
                ),
            ))
            .spacing(12.0),
        ),
        Element::from(track),
        Element::from(
            caption(c.items.join(" · "))
                .font_family(theme::FONT_UI)
                .foreground(theme::TEXT_3)
                .wrap(),
        ),
    ))
    .spacing(4.0)
    .margin(Thickness::xy(0.0, 6.0))
    .into()
}

fn stat_card(label: &str, value: String, sub: Option<String>) -> Element {
    let mut kids: Vec<Element> = vec![
        Element::from(
            caption(label.to_uppercase())
                .font_family(theme::FONT_UI)
                .foreground(theme::TEXT_4),
        ),
        Element::from(
            body_strong(value)
                .font_size(26.0)
                .font_family(theme::FONT_DISPLAY)
                .foreground(theme::TEXT),
        ),
    ];
    if let Some(s) = sub {
        kids.push(Element::from(
            caption(s).font_family(theme::FONT_UI).foreground(theme::TEXT_3).wrap(),
        ));
    }
    border(vstack(kids).spacing(2.0))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(10.0)
        .padding(Thickness::uniform(14.0))
        .into()
}

fn gap_row(g: &SecurityGap) -> Element {
    let (color, label) = match g.severity.as_str() {
        "high" => (theme::ERROR, "HIGH"),
        "medium" => (theme::WARN, "MED"),
        _ => (theme::TEXT_3, "LOW"),
    };
    let pill = border(caption(label).foreground(color).font_family(theme::FONT_UI))
        .background(theme::SURFACE_2)
        .corner_radius(9.0)
        .padding(Thickness::xy(8.0, 2.0));
    border(
        hstack((
            Element::from(pill),
            Element::from(
                vstack((
                    Element::from(
                        caption(g.category.clone())
                            .foreground(theme::TEXT_3)
                            .font_family(theme::FONT_UI),
                    ),
                    Element::from(body(g.description.clone()).foreground(theme::TEXT).wrap()),
                ))
                .spacing(2.0),
            ),
        ))
        .spacing(10.0),
    )
    .corner_radius(6.0)
    .padding(Thickness::uniform(8.0))
    .into()
}

fn state_chip(state: &str) -> Element {
    let (color, label) = if state.eq_ignore_ascii_case("enabled") {
        (theme::OK, "Enabled")
    } else if state.to_lowercase().contains("report") {
        (theme::WARN, "Report-only")
    } else {
        (theme::TEXT_3, "Disabled")
    };
    border(caption(label).foreground(color).font_family(theme::FONT_UI))
        .background(theme::SURFACE_2)
        .corner_radius(10.0)
        .padding(Thickness::xy(8.0, 1.0))
        .into()
}

// A two-column table card: left text + a right cell element per row.
fn table_card(title: String, col2: &str, rows: Vec<(String, Element)>) -> Element {
    let header = grid((
        caption("NAME")
            .foreground(theme::TEXT_4)
            .font_family(theme::FONT_UI)
            .grid_column(0),
        caption(col2.to_uppercase())
            .foreground(theme::TEXT_4)
            .font_family(theme::FONT_UI)
            .grid_column(1),
    ))
    .columns([GridLength::Star(2.0), GridLength::Star(1.0)])
    .column_spacing(8.0);

    let row_els: Vec<Element> = rows
        .into_iter()
        .map(|(name, right)| {
            grid((
                caption(name)
                    .foreground(theme::TEXT_2)
                    .font_family(theme::FONT_UI)
                    .wrap()
                    .grid_column(0),
                right.grid_column(1),
            ))
            .columns([GridLength::Star(2.0), GridLength::Star(1.0)])
            .column_spacing(8.0)
            .margin(Thickness::xy(0.0, 3.0))
            .into()
        })
        .collect();

    card(vstack((
        Element::from(
            body_strong(title)
                .font_family(theme::FONT_UI)
                .foreground(theme::TEXT),
        ),
        Element::from(header),
        Element::from(vstack(row_els).spacing(2.0)),
    ))
    .spacing(8.0))
}

pub fn posture_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (tick, bump) = cx.use_reducer(0_u64);
    let posture = cx.use_resource(|_: u64| api().security_posture().map_err(service_err), tick);
    let list_height = crate::list_height(cx);

    let body_el: Element = posture
        .view(|p: &SecurityPosture| -> Element {
            // ── Left column: gauge + breakdown ──
            let mut bd: Vec<Element> = vec![Element::from(
                body_strong("Score breakdown")
                    .font_family(theme::FONT_UI)
                    .foreground(theme::TEXT),
            )];
            bd.extend(p.breakdown.iter().map(category_bar));
            let left = vstack((
                Element::from(gauge(p.score)),
                Element::from(card(vstack(bd).spacing(4.0))),
            ))
            .spacing(20.0);

            // ── Right column: stat cards + gaps + tables ──
            let s = &p.stats;
            let mk = |i: usize, label: &str, value: String, sub: Option<String>| -> Element {
                stat_card(label, value, sub)
                    .grid_row((i / 3) as i32)
                    .grid_column((i % 3) as i32)
            };
            let cells = vec![
                mk(0, "CA Policies", s.ca_total.to_string(),
                   Some(format!("{} enabled · {} report-only", s.ca_enabled, s.ca_report_only))),
                mk(1, "Compliance", s.compliance_policies.to_string(),
                   Some(if s.compliance_platforms.is_empty() { "no platforms".into() } else { s.compliance_platforms.join(", ") })),
                mk(2, "Endpoint Security", s.endpoint_security_intents.to_string(), None),
                mk(3, "App Protection", s.app_protection_policies.to_string(), None),
                mk(4, "Auth Strengths", s.auth_strength_policies.to_string(), None),
                mk(5, "Named Locations", s.named_locations.to_string(), None),
            ];
            let stats_grid = grid(cells)
                .columns([GridLength::Star(1.0), GridLength::Star(1.0), GridLength::Star(1.0)])
                .rows([GridLength::Auto, GridLength::Auto])
                .column_spacing(12.0)
                .row_spacing(12.0);

            let gaps_card = if p.gaps.is_empty() {
                card(caption("No gaps detected.").foreground(theme::OK))
            } else {
                let mut g: Vec<Element> = vec![Element::from(
                    body_strong(format!("Security gaps ({})", p.gaps.len()))
                        .font_family(theme::FONT_UI)
                        .foreground(theme::TEXT),
                )];
                g.extend(p.gaps.iter().map(gap_row));
                card(vstack(g).spacing(6.0))
            };

            let ca_rows: Vec<(String, Element)> = p
                .ca_policies
                .iter()
                .take(60)
                .map(|r| (r.name.clone(), state_chip(&r.state)))
                .collect();
            let comp_rows: Vec<(String, Element)> = p
                .compliance_policies
                .iter()
                .take(60)
                .map(|r| {
                    (
                        r.name.clone(),
                        Element::from(
                            caption(r.platform.clone())
                                .foreground(theme::TEXT_3)
                                .font_family(theme::FONT_UI),
                        ),
                    )
                })
                .collect();

            let right = vstack((
                Element::from(stats_grid),
                Element::from(gaps_card),
                Element::from(table_card(format!("Conditional Access ({})", p.ca_policies.len()), "State", ca_rows)),
                Element::from(table_card(format!("Compliance ({})", p.compliance_policies.len()), "Platform", comp_rows)),
            ))
            .spacing(20.0);

            grid((left.grid_column(0), right.grid_column(1)))
                .columns([GridLength::Pixel(320.0), GridLength::Star(1.0)])
                .column_spacing(20.0)
                .into()
        })
        .loading(caption("scoring posture…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    crate::fluid_fill(
        12.0,
        vec![Element::from(
            button("Refresh").on_click(move || bump.call(|n| n + 1)),
        )],
        scroll_viewer(body_el)
            .height(list_height)
            .into(),
    )
    .margin(Thickness::uniform(16.0))
    .into()
}
