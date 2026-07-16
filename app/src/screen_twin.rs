//! M17 Tenant Digital Twin — offline graph analytics over the materialized
//! tenant graph (devices/users/groups/policies + assignment edges). Read-only.
//!
//! Top: a freshness banner (GET /twin/stats) + Rebuild (offline) / Refresh-from-Graph
//! actions (POST /twin/rebuild?warm=). Left: a query picker over the seven canned
//! analytics (GET /twin/analytics/{query}) rendering findings as rows. Right: the
//! selected finding's node neighborhood (GET /twin/node/{id}) — inbound/outbound
//! edges, members, warnings. No write path (analytics only; the M13 inbox owns writes).

use api_types::{TwinAnalyticsResult, TwinEdgeView, TwinFinding, TwinNeighborhood, TwinStats};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

// (query id, display label) for the seven canned analytics (TwinEndpoints ValidQueries).
const QUERIES: [(&str, &str); 7] = [
    ("orphaned-policies", "Orphaned policies"),
    ("redundant-assignments", "Redundant assignments"),
    ("conflicting-assignments", "Conflicting assignments"),
    ("assignment-cycles", "Assignment cycles"),
    ("ca-escape-paths", "CA escape paths"),
    ("coverage-gaps", "Coverage gaps"),
    ("drift-hotspots", "Drift hotspots"),
];

fn query_idx(id: &str) -> i32 {
    QUERIES.iter().position(|(q, _)| *q == id).map(|i| i as i32).unwrap_or(0)
}

fn pill(text: &str, color: Color) -> Element {
    border(caption(text.to_string()).font_size(11.0).font_family(theme::FONT_UI).foreground(color))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(4.0)
        .padding(Thickness::xy(6.0, 2.0))
        .into()
}

// Stable list key for a finding (id → name → detail, whichever is present first).
fn finding_key(f: &TwinFinding) -> String {
    f.id.clone()
        .or_else(|| f.name.clone())
        .unwrap_or_else(|| format!("{}:{}", f.kind, f.detail))
}

fn finding_row(f: &TwinFinding) -> Element {
    let title = f.name.clone().or_else(|| f.id.clone()).unwrap_or_else(|| f.kind.clone());
    let heading = hstack((
        pill(&f.kind, theme::BRAND_BRIGHT),
        Element::from(body_strong(title).foreground(theme::TEXT).wrap()),
    ))
    .spacing(8.0);

    // metric pills (key=value) — keys vary per query, so render generically.
    let metric_pills: Vec<Element> =
        f.metrics.iter().map(|(k, v)| pill(&format!("{k} {v}"), theme::TEXT_3)).collect();
    let metrics_row: Element = if metric_pills.is_empty() {
        Element::Empty
    } else {
        hstack(metric_pills).spacing(6.0).into()
    };

    let detail = caption(f.detail.clone()).foreground(theme::TEXT_3).wrap();

    vstack((Element::from(heading), Element::from(detail), metrics_row))
        .spacing(3.0)
        .margin(Thickness::xy(8.0, 6.0))
        .into()
}

fn edge_row(arrow: &str, e: &TwinEdgeView) -> Element {
    caption(format!("{} {} · {} ({})", arrow, e.r#type, e.other_node.name, e.other_node.r#type))
        .foreground(theme::TEXT_3)
        .font_family(theme::FONT_MONO)
        .wrap()
        .into()
}

fn node_view(nb: &TwinNeighborhood) -> Element {
    let head = hstack((
        pill(&nb.node.r#type, theme::BRAND_BRIGHT),
        Element::from(body_strong(nb.node.name.clone()).foreground(theme::TEXT).wrap()),
    ))
    .spacing(8.0);

    let mut sections: Vec<Element> = vec![head.into()];

    if !nb.warnings.is_empty() {
        let mut items = vec![Element::from(caption("Warnings").foreground(theme::WARN))];
        items.extend(
            nb.warnings.iter().map(|w| caption(w.clone()).foreground(theme::WARN).wrap().into()),
        );
        sections.push(vstack(items).spacing(2.0).into());
    }
    if !nb.outbound.is_empty() {
        let mut items = vec![Element::from(
            caption(format!("Outbound ({})", nb.outbound.len())).foreground(theme::TEXT_4),
        )];
        items.extend(nb.outbound.iter().map(|e| edge_row("→", e)));
        sections.push(vstack(items).spacing(2.0).into());
    }
    if !nb.inbound.is_empty() {
        let mut items = vec![Element::from(
            caption(format!("Inbound ({})", nb.inbound.len())).foreground(theme::TEXT_4),
        )];
        items.extend(nb.inbound.iter().map(|e| edge_row("←", e)));
        sections.push(vstack(items).spacing(2.0).into());
    }
    if !nb.members.is_empty() {
        let mut items = vec![Element::from(
            caption(format!("Members ({})", nb.members.len())).foreground(theme::TEXT_4),
        )];
        items.extend(nb.members.iter().take(50).map(|m| {
            caption(format!("{} ({})", m.name, m.r#type))
                .foreground(theme::TEXT_3)
                .font_family(theme::FONT_MONO)
                .wrap()
                .into()
        }));
        sections.push(vstack(items).spacing(2.0).into());
    }

    vstack(sections).spacing(12.0).into()
}

pub fn twin_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (query, set_query) = cx.use_state(String::from(QUERIES[0].0));
    let (selected, set_selected) = cx.use_state(String::new()); // selected finding node id
    let (data_tick, bump_data) = cx.use_reducer(0_u64);
    let (rebuild_warm, set_rebuild_warm) = cx.use_state::<Option<bool>>(None);
    let (rebuild_tick, bump_rebuild) = cx.use_reducer(0_u64);

    let stats = cx.use_resource(|_: u64| api().twin_stats().map_err(service_err), data_tick);

    let rebuild = cx.use_resource(
        |k: (Option<bool>, u64)| -> std::result::Result<Option<TwinStats>, String> {
            let (warm, _) = k;
            match warm {
                None => Ok(None),
                Some(w) => api().twin_rebuild(w).map(Some).map_err(service_err),
            }
        },
        (rebuild_warm, rebuild_tick),
    );

    // After a rebuild reports stats, refetch the header + analytics + node.
    let rebuilt = matches!(rebuild.data(), Some(Some(_)));
    {
        let bump_data = bump_data.clone();
        cx.use_effect(rebuilt, move || {
            if rebuilt {
                bump_data.call(|n| n + 1);
            }
        });
    }

    let analytics = cx.use_resource(
        |k: (String, u64)| {
            let (q, _) = k;
            api().twin_analytics(&q).map_err(service_err)
        },
        (query.clone(), data_tick),
    );

    let node = cx.use_resource(
        |k: (String, u64)| -> std::result::Result<Option<TwinNeighborhood>, String> {
            let (id, _) = k;
            if id.is_empty() {
                return Ok(None);
            }
            api().twin_node(&id).map_err(service_err)
        },
        (selected.clone(), data_tick),
    );

    let busy_rebuild = rebuild.is_loading();
    let list_height = crate::list_height(cx);

    // ── Freshness banner + rebuild actions ─────────────────────────────────
    let banner: Element = stats
        .view(|s: &TwinStats| -> Element {
            let stale_pill: Element = if s.stale {
                pill("stale", theme::WARN)
            } else {
                pill("fresh", theme::OK)
            };
            let built = s.built_utc.clone().unwrap_or_else(|| "never".to_string());
            hstack((
                stale_pill,
                Element::from(
                    caption(format!(
                        "{} nodes · {} edges · source {} · built {}",
                        s.node_count, s.edge_count, s.source, built
                    ))
                    .foreground(theme::TEXT_3)
                    .font_family(theme::FONT_UI),
                ),
            ))
            .spacing(8.0)
            .into()
        })
        .loading(caption("loading twin stats…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let rebuild_btn = button(if busy_rebuild { "Rebuilding…" } else { "Rebuild (offline)" })
        .enabled(!busy_rebuild)
        .on_click({
            let set_rebuild_warm = set_rebuild_warm.clone();
            let bump_rebuild = bump_rebuild.clone();
            move || {
                set_rebuild_warm.call(Some(false));
                bump_rebuild.call(|n| n + 1);
            }
        });
    let refresh_btn = button(if busy_rebuild { "Refreshing…" } else { "Refresh from Graph" })
        .accent()
        .enabled(!busy_rebuild)
        .on_click({
            let set_rebuild_warm = set_rebuild_warm.clone();
            let bump_rebuild = bump_rebuild.clone();
            move || {
                set_rebuild_warm.call(Some(true));
                bump_rebuild.call(|n| n + 1);
            }
        });
    let rebuild_err: Element = rebuild
        .error()
        .map(|e| caption(format!("rebuild failed: {e}")).foreground(theme::ERROR).wrap().into())
        .unwrap_or(Element::Empty);

    let header = vstack((
        banner,
        Element::from(
            hstack((Element::from(rebuild_btn), Element::from(refresh_btn), rebuild_err)).spacing(8.0),
        ),
    ))
    .spacing(6.0);

    // ── Query picker + findings list ───────────────────────────────────────
    let query_combo = ComboBox::new(QUERIES.iter().map(|(_, l)| l.to_string()).collect::<Vec<_>>())
        .header("Analytics")
        .selected_index(query_idx(&query))
        .on_selection_changed({
            let set_query = set_query.clone();
            let set_selected = set_selected.clone();
            move |i: i32| {
                if let Some((q, _)) = QUERIES.get(i as usize) {
                    set_query.call(q.to_string());
                    set_selected.call(String::new());
                }
            }
        });

    let selected_now = selected.clone();
    let findings_list: Element = analytics
        .view(move |r: &TwinAnalyticsResult| -> Element {
            if r.findings.is_empty() {
                return body(format!(
                    "No findings for this query (source: {}). Rebuild the twin, or sign in and Refresh from Graph.",
                    r.source
                ))
                .opacity(0.6)
                .wrap()
                .into();
            }
            let findings = r.findings.clone();
            let sel_index = findings
                .iter()
                .position(|f| f.id.as_deref() == Some(selected_now.as_str()))
                .map(|i| i as i32)
                .unwrap_or(-1);
            let ids: Vec<Option<String>> = findings.iter().map(|f| f.id.clone()).collect();
            let set_selected = set_selected.clone();
            list_view(findings, |f: &TwinFinding, _| finding_row(f))
                .with_key_selector(|f: &TwinFinding| finding_key(f))
                .selected_index(sel_index)
                .on_selection_changed(move |i| {
                    let id = ids.get(i as usize).cloned().flatten().unwrap_or_default();
                    set_selected.call(id);
                })
                .height(list_height - 150.0)
                .into()
        })
        .loading(caption("running analytics…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let left = crate::fluid_fill(12.0, vec![Element::from(header), Element::from(query_combo)], findings_list);

    // ── Node neighborhood detail ───────────────────────────────────────────
    let detail: Element = node
        .view(|n: &Option<TwinNeighborhood>| -> Element {
            match n {
                None => body("Select a finding with a node to inspect its neighborhood.")
                    .opacity(0.6)
                    .wrap()
                    .into(),
                Some(nb) => node_view(nb),
            }
        })
        .loading(caption("loading node neighborhood…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();
    let detail_scroll = scroll_viewer(detail).height((list_height - 20.0).max(200.0));

    grid((left.grid_column(0), Element::from(detail_scroll).grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(1.2)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
