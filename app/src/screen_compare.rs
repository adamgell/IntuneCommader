//! Policy Comparison — two modes over the same verdict-grouped result view:
//!   • Baseline vs Policy — an embedded SettingsCatalog security baseline vs a live tenant
//!     policy (POST /baselines/compare).
//!   • Policy vs Policy — any two live SettingsCatalog policies, A vs B (POST /policies/compare).
//! Both group settings by verdict (drifted/different · missing/only-A · extra/only-B ·
//! matching/same) and now show each setting's human-readable name (resolved from the
//! embedded Settings Catalog definition registry) with the raw definition id underneath.
//!
//! Only SettingsCatalog policies/baselines are comparable (BaselineService).

use api_types::{BaselineComparison, BaselineSettingComparison, ListItem};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

fn val(v: &Option<String>) -> String {
    v.clone().unwrap_or_else(|| "∅".to_string())
}

// One setting row: readable name (falls back to the id), the raw definition id, then the
// A/B (or baseline/tenant) values. `labels` is the (left, right) value-column caption pair.
fn setting_row(c: &BaselineSettingComparison, labels: (&str, &str)) -> Element {
    let name = c
        .setting_name
        .clone()
        .filter(|s| !s.trim().is_empty())
        .unwrap_or_else(|| c.setting_definition_id.clone());
    border(
        vstack((
            Element::from(body_strong(name).wrap()),
            Element::from(
                caption(c.setting_definition_id.clone())
                    .foreground(theme::TEXT_4)
                    .font_family(theme::FONT_MONO)
                    .wrap(),
            ),
            Element::from(
                caption(format!(
                    "{}: {}   ·   {}: {}",
                    labels.0,
                    val(&c.baseline_value),
                    labels.1,
                    val(&c.tenant_value)
                ))
                .foreground(theme::TEXT_2)
                .font_family(theme::FONT_MONO)
                .wrap(),
            ),
        ))
        .spacing(1.0),
    )
    .corner_radius(4.0)
    .padding(Thickness::uniform(6.0))
    .into()
}

// A titled section: heading (name + count pill) + up to `cap` rows.
fn group(
    title: &str,
    color: Color,
    items: &[BaselineSettingComparison],
    labels: (&str, &str),
    cap: usize,
    with_rows: bool,
) -> Element {
    let heading = border(
        caption(format!("{} — {}", title, items.len()))
            .font_family(theme::FONT_UI)
            .foreground(color),
    )
    .background(theme::SURFACE_2)
    .border_brush(theme::LINE)
    .corner_radius(4.0)
    .padding(Thickness::xy(8.0, 3.0));
    if !with_rows || items.is_empty() {
        return Element::from(heading);
    }
    let mut els = vec![Element::from(heading)];
    els.extend(items.iter().take(cap).map(|c| setting_row(c, labels)));
    if items.len() > cap {
        els.push(Element::from(
            caption(format!("… and {} more", items.len() - cap)).opacity(0.6),
        ));
    }
    vstack(els).spacing(4.0).into()
}

// A settings-catalog policy picker (header + combo over `items`), calling `on_pick` with
// the chosen policy id.
fn policy_picker(
    header: &str,
    items: &[ListItem],
    selected: &str,
    on_pick: impl Fn(String) + Clone + 'static,
) -> ComboBox {
    let names: Vec<String> = items.iter().map(|p| p.title.clone()).collect();
    let ids: Vec<String> = items.iter().map(|p| p.id.clone()).collect();
    let idx = ids.iter().position(|i| i == selected).map(|i| i as i32).unwrap_or(-1);
    ComboBox::new(names)
        .header(header)
        .selected_index(idx)
        .on_selection_changed(move |i: i32| {
            if let Some(id) = ids.get(i as usize) {
                on_pick(id.clone());
            }
        })
}

pub fn compare_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // mode: "baseline" (baseline vs tenant policy) | "policy" (policy A vs policy B)
    let (mode, set_mode) = cx.use_state(String::from("baseline"));
    let (baseline_id, set_baseline_id) = cx.use_state(String::new());
    let (policy_id, set_policy_id) = cx.use_state(String::new()); // baseline-mode tenant / policy-mode A
    let (policy_b, set_policy_b) = cx.use_state(String::new()); // policy-mode B

    let baselines = cx.use_resource(|_: ()| api().get_list("/baselines").map_err(service_err), ());
    let policies =
        cx.use_resource(|_: ()| api().get_list("/settings-catalog").map_err(service_err), ());

    let comparison = cx.use_resource(
        |k: (String, String, String, String)| -> std::result::Result<Option<BaselineComparison>, String> {
            let (m, b, a, bb) = k;
            match m.as_str() {
                "policy" => {
                    if a.is_empty() || bb.is_empty() || a == bb {
                        return Ok(None);
                    }
                    api().compare_policies(&a, &bb).map(Some).map_err(service_err)
                }
                _ => {
                    if b.is_empty() || a.is_empty() {
                        return Ok(None);
                    }
                    api().compare_baseline(&b, &a).map(Some).map_err(service_err)
                }
            }
        },
        (mode.clone(), baseline_id.clone(), policy_id.clone(), policy_b.clone()),
    );

    let list_height = crate::list_height(cx);
    let policy_mode = mode == "policy";

    // ── Mode toggle ──────────────────────────────────────────────────────────
    let mk_mode = |label: &'static str, m: &'static str| -> Element {
        let active = mode == m;
        let sm = set_mode.clone();
        let b = button(label).enabled(!active).on_click(move || sm.call(m.to_string()));
        if active { Element::from(b.accent()) } else { Element::from(b) }
    };
    let mode_row = hstack((
        mk_mode("Baseline vs Policy", "baseline"),
        mk_mode("Policy vs Policy", "policy"),
    ))
    .spacing(6.0);

    // ── Pickers (mode-dependent) ─────────────────────────────────────────────
    let pol: Vec<ListItem> = policies.data().cloned().unwrap_or_default();
    let pickers: Element = if policy_mode {
        let a = policy_picker("Policy A", &pol, &policy_id, {
            let s = set_policy_id.clone();
            move |id| s.call(id)
        });
        let b = policy_picker("Policy B", &pol, &policy_b, {
            let s = set_policy_b.clone();
            move |id| s.call(id)
        });
        hstack((Element::from(a), Element::from(b))).spacing(12.0).into()
    } else {
        // Baseline picker — filtered to SettingsCatalog (only those are comparable).
        let sc_baselines: Vec<ListItem> = baselines
            .data()
            .cloned()
            .unwrap_or_default()
            .into_iter()
            .filter(|b| b.badge.as_deref() == Some("SettingsCatalog"))
            .collect();
        let bpick = policy_picker("Baseline (SettingsCatalog)", &sc_baselines, &baseline_id, {
            let s = set_baseline_id.clone();
            move |id| s.call(id)
        });
        let ppick = policy_picker("Tenant policy", &pol, &policy_id, {
            let s = set_policy_id.clone();
            move |id| s.call(id)
        });
        hstack((Element::from(bpick), Element::from(ppick))).spacing(12.0).into()
    };

    let intro = caption(if policy_mode {
        "Compare any two live SettingsCatalog policies (A vs B) setting-by-setting. Sign in first."
    } else {
        "Compare an embedded SettingsCatalog baseline against a tenant policy's live settings. \
         Sign in first; only SettingsCatalog baselines are comparable."
    })
    .opacity(0.7)
    .wrap();

    // Verdict labels + value-column captions vary by mode.
    let (h_drift, h_missing, h_extra, h_match) = if policy_mode {
        ("Different", "Only in A", "Only in B", "Same")
    } else {
        ("Drifted", "Missing (baseline-only)", "Extra (tenant-only)", "Matching")
    };
    let labels = if policy_mode { ("A", "B") } else { ("baseline", "tenant") };

    let result: Element = comparison
        .view(move |c: &Option<BaselineComparison>| -> Element {
            match c {
                None => body(if policy_mode {
                    "Pick two different SettingsCatalog policies to compare."
                } else {
                    "Pick a baseline and a tenant policy to compare."
                })
                .opacity(0.6)
                .wrap()
                .into(),
                Some(r) => {
                    let summary = caption(format!(
                        "{}   ·   {} {} · {} {} · {} {} · {} {}",
                        r.baseline_name,
                        h_drift, r.drifted.len(),
                        h_missing, r.missing.len(),
                        h_extra, r.extra.len(),
                        h_match, r.matching.len()
                    ))
                    .foreground(theme::TEXT_2)
                    .font_family(theme::FONT_UI)
                    .wrap();
                    vstack((
                        Element::from(summary),
                        group(h_drift, theme::WARN, &r.drifted, labels, 200, true),
                        group(h_missing, theme::ERROR, &r.missing, labels, 200, true),
                        group(h_extra, theme::BRAND_BRIGHT, &r.extra, labels, 200, true),
                        group(h_match, theme::OK, &r.matching, labels, 0, false),
                    ))
                    .spacing(10.0)
                    .into()
                }
            }
        })
        .loading(caption("comparing settings…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let content = crate::fluid_fill(
        12.0,
        vec![Element::from(mode_row), pickers, Element::from(intro)],
        scroll_viewer(result).height((list_height - 110.0).max(200.0)).into(),
    );

    border(content).margin(Thickness::uniform(16.0)).into()
}
