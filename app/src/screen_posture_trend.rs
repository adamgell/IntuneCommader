//! M19 Continuous Posture — benchmark-mapped score, trend, POA&M, and evidence-pack
//! export. Complements the point-in-time Security Posture screen: this one picks a
//! benchmark (CIS / OIB / Essential 8 / NIST), shows the mapped score + control
//! coverage, trends the score over the snapshot store, lists the open POA&M, and
//! exports a reproducible evidence pack.
//!
//! GET /posture/score (seeds a trend point) · GET /posture/trend · GET /posture/poam
//! · POST /posture/evidence-pack. The evidence pack is a report/export — no Graph
//! write, no M13 inbox. The reactor fork has no SVG, so the trend is graded chips.

use api_types::{
    BenchmarkedPosture, EvidencePackManifest, EvidencePackRequest, Poam, PoamItem, PostureTrend,
    PostureTrendPoint,
};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

const BENCHMARKS: [(&str, &str); 4] =
    [("cis", "CIS"), ("oib", "OIB"), ("e8", "Essential 8"), ("nist", "NIST 800-53")];

fn bench_idx(b: &str) -> i32 {
    BENCHMARKS.iter().position(|(k, _)| *k == b).map(|i| i as i32).unwrap_or(0)
}

fn band(score: i32) -> Color {
    if score >= 80 {
        theme::OK
    } else if score >= 60 {
        theme::WARN
    } else {
        theme::ERROR
    }
}

fn day(iso: &str) -> String {
    iso.split('T').next().unwrap_or(iso).to_string()
}

fn pill(text: &str, color: Color) -> Element {
    border(caption(text.to_string()).font_size(11.0).font_family(theme::FONT_UI).foreground(color))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(4.0)
        .padding(Thickness::xy(6.0, 2.0))
        .into()
}

fn point_chip(p: &PostureTrendPoint) -> Element {
    border(
        vstack((
            Element::from(
                body_strong(p.score.to_string())
                    .font_size(20.0)
                    .font_family(theme::FONT_DISPLAY)
                    .foreground(band(p.score)),
            ),
            Element::from(caption(day(&p.captured_utc)).foreground(theme::TEXT_4).font_family(theme::FONT_MONO)),
        ))
        .spacing(2.0),
    )
    .background(theme::SURFACE_2)
    .border_brush(theme::LINE)
    .corner_radius(6.0)
    .padding(Thickness::xy(10.0, 6.0))
    .into()
}

fn poam_row(it: &PoamItem) -> Element {
    let sev = match it.severity.as_str() {
        "high" => theme::ERROR,
        "medium" => theme::WARN,
        _ => theme::TEXT_3,
    };
    let controls: String = it
        .controls
        .iter()
        .map(|c| format!("{}:{}", c.framework, c.id))
        .collect::<Vec<_>>()
        .join(", ");
    border(
        vstack((
            Element::from(
                hstack((
                    pill(&it.severity, sev),
                    Element::from(body_strong(it.finding.clone()).foreground(theme::TEXT).wrap()),
                ))
                .spacing(8.0),
            ),
            Element::from(
                caption(format!("{} · {} · due {}", it.category, controls, day(&it.due_utc)))
                    .foreground(theme::TEXT_4)
                    .font_family(theme::FONT_UI)
                    .wrap(),
            ),
        ))
        .spacing(2.0),
    )
    .corner_radius(4.0)
    .padding(Thickness::uniform(8.0))
    .into()
}

pub fn posture_trend_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (benchmark, set_benchmark) = cx.use_state(String::from("cis"));
    let (tick, bump) = cx.use_reducer(0_u64);
    let (ev_key, set_ev_key) = cx.use_state::<Option<String>>(None);
    let (ev_tick, bump_ev) = cx.use_reducer(0_u64);

    let score = cx.use_resource(
        |k: (String, u64)| api().posture_score(&k.0).map_err(service_err),
        (benchmark.clone(), tick),
    );
    let trend = cx.use_resource(
        |k: (String, u64)| api().posture_trend(&k.0).map_err(service_err),
        (benchmark.clone(), tick),
    );
    let poam = cx.use_resource(
        |k: (String, u64)| api().posture_poam(&k.0).map_err(service_err),
        (benchmark.clone(), tick),
    );
    let evidence = cx.use_resource(
        |k: (Option<String>, u64)| -> std::result::Result<Option<EvidencePackManifest>, String> {
            match k.0 {
                None => Ok(None),
                Some(b) => api()
                    .posture_evidence_pack(&EvidencePackRequest {
                        benchmark: Some(b),
                        ..Default::default()
                    })
                    .map(Some)
                    .map_err(service_err),
            }
        },
        (ev_key.clone(), ev_tick),
    );

    let list_height = crate::list_height(cx);
    let ev_busy = evidence.is_loading();

    // ── Benchmark picker + refresh ─────────────────────────────────────────
    let combo = ComboBox::new(BENCHMARKS.iter().map(|(_, l)| l.to_string()).collect::<Vec<_>>())
        .header("Benchmark")
        .selected_index(bench_idx(&benchmark))
        .on_selection_changed({
            let set_benchmark = set_benchmark.clone();
            move |i: i32| {
                if let Some((k, _)) = BENCHMARKS.get(i as usize) {
                    set_benchmark.call(k.to_string());
                }
            }
        });
    let refresh_btn = button("Refresh").on_click({
        let bump = bump.clone();
        move || bump.call(|n| n + 1)
    });
    let ev_btn = button(if ev_busy { "Exporting…" } else { "Export evidence pack" })
        .accent()
        .enabled(!ev_busy)
        .on_click({
            let set_ev_key = set_ev_key.clone();
            let bump_ev = bump_ev.clone();
            let benchmark = benchmark.clone();
            move || {
                set_ev_key.call(Some(benchmark.clone()));
                bump_ev.call(|n| n + 1);
            }
        });
    let toolbar = hstack((Element::from(combo), Element::from(refresh_btn), Element::from(ev_btn))).spacing(8.0);

    // ── Score header ───────────────────────────────────────────────────────
    let score_el: Element = score
        .view(|s: &BenchmarkedPosture| -> Element {
            let head = hstack((
                pill(&format!("{}", s.score), band(s.score)),
                Element::from(
                    body_strong(format!("{} {}", s.benchmark.to_uppercase(), s.benchmark_version))
                        .foreground(theme::TEXT),
                ),
            ))
            .spacing(8.0);
            let coverage = caption(format!(
                "coverage {}/{} controls ({}%)",
                s.coverage.controls_covered, s.coverage.controls_total, s.coverage.percent
            ))
            .foreground(theme::TEXT_3);
            let cats: Vec<Element> = s
                .breakdown
                .iter()
                .map(|c| {
                    caption(format!("{}: {}/{}", c.category, c.score, c.max_score))
                        .foreground(theme::TEXT_3)
                        .font_family(theme::FONT_MONO)
                        .into()
                })
                .collect();
            let mut items = vec![head.into(), coverage.into()];
            items.extend(cats);
            vstack(items).spacing(3.0).into()
        })
        .loading(caption("scoring the tenant against the benchmark… (sign in first)").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    // ── Trend ──────────────────────────────────────────────────────────────
    let trend_el: Element = trend
        .view(|t: &PostureTrend| -> Element {
            if t.points.is_empty() {
                return caption(
                    "No trend yet — the score is snapshotted on each scoring; refresh again after a change to see movement.",
                )
                .opacity(0.6)
                .wrap()
                .into();
            }
            // Show the most recent ~24 points left→right.
            let start = t.points.len().saturating_sub(24);
            let chips: Vec<Element> = t.points[start..].iter().map(point_chip).collect();
            let delta_color = if t.delta.score_change >= 0 { theme::OK } else { theme::ERROR };
            let delta = caption(format!("Δ {} since last", t.delta.score_change))
                .foreground(delta_color)
                .font_family(theme::FONT_UI);
            let regressions: Vec<Element> =
                t.delta.regressions.iter().map(|r| pill(r, theme::WARN)).collect();
            let mut head_row = vec![Element::from(delta)];
            head_row.extend(regressions);
            vstack((
                Element::from(hstack(head_row).spacing(6.0)),
                Element::from(scroll_viewer(hstack(chips).spacing(6.0)).height(74.0)),
            ))
            .spacing(6.0)
            .into()
        })
        .loading(caption("loading trend…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    // ── POA&M ────────────────────────────────────────────────────────────
    let poam_el: Element = poam
        .view(|p: &Poam| -> Element {
            if p.items.is_empty() {
                return caption("No open POA&M items for this benchmark.").opacity(0.6).into();
            }
            let rows: Vec<Element> = p.items.iter().take(50).map(poam_row).collect();
            vstack(rows).spacing(6.0).into()
        })
        .loading(caption("loading POA&M…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    // ── Evidence-pack result ─────────────────────────────────────────────
    let ev_el: Element = evidence
        .view(|m: &Option<EvidencePackManifest>| -> Element {
            match m {
                None => Element::Empty,
                Some(man) => border(
                    vstack((
                        Element::from(body_strong("Evidence pack created").foreground(theme::OK)),
                        Element::from(
                            caption(format!("pack {} · score {} · {} artifact(s)", man.pack_id, man.score, man.artifacts.len()))
                                .foreground(theme::TEXT_2),
                        ),
                        Element::from(caption(format!("zip: {}", man.zip_path)).foreground(theme::TEXT_3).font_family(theme::FONT_MONO).wrap()),
                        Element::from(caption(format!("hash: {}", man.manifest_hash)).foreground(theme::TEXT_4).font_family(theme::FONT_MONO).wrap()),
                    ))
                    .spacing(3.0),
                )
                .background(theme::SURFACE_2)
                .border_brush(theme::LINE)
                .corner_radius(8.0)
                .padding(Thickness::uniform(10.0))
                .into(),
            }
        })
        .loading(caption("packaging evidence…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    fn section(title: &str, body: Element) -> Element {
        vstack((
            Element::from(body_strong(title.to_string()).font_family(theme::FONT_UI).foreground(theme::TEXT_3)),
            body,
        ))
        .spacing(6.0)
        .into()
    }

    let content = vstack((
        Element::from(toolbar),
        section("Score", score_el),
        section("Trend", trend_el),
        section("Plan of action & milestones", poam_el),
        ev_el,
    ))
    .spacing(16.0)
    .margin(Thickness::uniform(16.0));

    scroll_viewer(content).height((list_height + 120.0).max(240.0)).into()
}
