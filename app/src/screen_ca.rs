//! Conditional Access workspace — like-to-like with IntuneCommander: a state stats
//! bar + filter chips, a multi-column policy grid (Name · State · Users · Apps ·
//! Grant controls), and a side detail panel showing the full include/exclude
//! conditions (GUIDs resolved to names), grant + session controls. Data from the
//! sidecar's /conditional-access/list and /{id}/detail. Brand colors via theme.rs.

use api_types::{CaDetail, CaPolicyListItem};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

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

fn badge(text: String) -> Element {
    border(caption(text).foreground(theme::TEXT_2).font_family(theme::FONT_UI))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(4.0)
        .padding(Thickness::xy(8.0, 2.0))
        .into()
}

fn stat_inline(text: String, color: Color) -> Element {
    caption(text).foreground(color).font_family(theme::FONT_UI).into()
}

// One five-column row (shared by the header and each policy row).
fn ca_row_grid(name: Element, state: Element, users: Element, apps: Element, grant: Element) -> Element {
    grid((
        name.grid_column(0),
        state.grid_column(1),
        users.grid_column(2),
        apps.grid_column(3),
        grant.grid_column(4),
    ))
    .columns([
        GridLength::Star(2.0),
        GridLength::Pixel(100.0),
        GridLength::Pixel(120.0),
        GridLength::Pixel(120.0),
        GridLength::Star(1.5),
    ])
    .column_spacing(8.0)
    .margin(Thickness::xy(8.0, 5.0))
    .into()
}

fn ca_row(p: &CaPolicyListItem) -> Element {
    let name_el: Element = match &p.description {
        Some(d) if !d.is_empty() => vstack((
            Element::from(
                body_strong(p.display_name.clone())
                    .font_family(theme::FONT_UI)
                    .foreground(theme::TEXT)
                    .wrap(),
            ),
            Element::from(
                caption(crate::truncate(d, 90))
                    .foreground(theme::TEXT_4)
                    .wrap(),
            ),
        ))
        .spacing(2.0)
        .into(),
        _ => body_strong(p.display_name.clone())
            .font_family(theme::FONT_UI)
            .foreground(theme::TEXT)
            .wrap()
            .into(),
    };
    ca_row_grid(
        name_el,
        state_chip(&p.state),
        caption(p.users.clone()).foreground(theme::TEXT_3).font_family(theme::FONT_UI).wrap().into(),
        caption(p.applications.clone()).foreground(theme::TEXT_3).font_family(theme::FONT_UI).wrap().into(),
        caption(p.grant_controls.join(", ")).foreground(theme::TEXT_3).font_family(theme::FONT_UI).wrap().into(),
    )
}

// A labelled row of badges (one per include/exclude item). Empty → nothing.
fn cond_list(label: &str, items: &[String]) -> Element {
    if items.is_empty() {
        return Element::Empty;
    }
    let shown = items.len().min(10);
    let mut badges: Vec<Element> = items.iter().take(shown).map(|it| badge(it.clone())).collect();
    if items.len() > shown {
        badges.push(Element::from(
            caption(format!("+{}", items.len() - shown)).foreground(theme::TEXT_4),
        ));
    }
    grid((
        caption(label.to_string())
            .foreground(theme::TEXT_3)
            .font_family(theme::FONT_UI)
            .grid_column(0),
        hstack(badges).spacing(4.0).grid_column(1),
    ))
    .columns([GridLength::Pixel(130.0), GridLength::Star(1.0)])
    .column_spacing(8.0)
    .margin(Thickness::xy(0.0, 2.0))
    .into()
}

fn prop_row(label: &str, value: String) -> Element {
    grid((
        caption(label.to_string())
            .foreground(theme::TEXT_3)
            .font_family(theme::FONT_UI)
            .grid_column(0),
        caption(value)
            .foreground(theme::TEXT)
            .font_family(theme::FONT_MONO)
            .wrap()
            .grid_column(1),
    ))
    .columns([GridLength::Pixel(130.0), GridLength::Star(1.0)])
    .column_spacing(8.0)
    .margin(Thickness::xy(0.0, 2.0))
    .into()
}

fn section(title: &str, children: Vec<Element>) -> Element {
    let mut all = vec![Element::from(
        body_strong(title.to_string())
            .font_family(theme::FONT_UI)
            .foreground(theme::BRAND_BRIGHT),
    )];
    all.extend(children);
    vstack(all).spacing(4.0).margin(Thickness::xy(0.0, 6.0)).into()
}

fn date10(s: &str) -> String {
    s.split('T').next().unwrap_or(s).to_string()
}

fn ca_detail_panel(d: &CaDetail, height: f64) -> Element {
    let c = &d.conditions;
    let title = hstack((
        Element::from(
            body_strong(d.display_name.clone())
                .font_size(18.0)
                .font_family(theme::FONT_DISPLAY)
                .foreground(theme::TEXT)
                .wrap(),
        ),
        Element::from(state_chip(&d.state)),
    ))
    .spacing(10.0);

    let desc: Element = match &d.description {
        Some(s) if !s.is_empty() => caption(s.clone()).foreground(theme::TEXT_3).wrap().into(),
        _ => Element::Empty,
    };

    let conditions = section(
        "Conditions",
        vec![
            cond_list("Include users", &c.include_users),
            cond_list("Exclude users", &c.exclude_users),
            cond_list("Include groups", &c.include_groups),
            cond_list("Exclude groups", &c.exclude_groups),
            cond_list("Include apps", &c.include_applications),
            cond_list("Exclude apps", &c.exclude_applications),
            cond_list("Include platforms", &c.include_platforms),
            cond_list("Exclude platforms", &c.exclude_platforms),
            cond_list("Include locations", &c.include_locations),
            cond_list("Exclude locations", &c.exclude_locations),
            cond_list("Client apps", &c.client_app_types),
            cond_list("Sign-in risk", &c.sign_in_risk_levels),
            cond_list("User risk", &c.user_risk_levels),
        ],
    );

    let mut grant_rows = vec![prop_row(
        "Operator",
        if d.grant.operator.is_empty() { "—".into() } else { d.grant.operator.clone() },
    )];
    if !d.grant.built_in_controls.is_empty() {
        grant_rows.push(cond_list("Controls", &d.grant.built_in_controls));
    }
    if let Some(a) = &d.grant.auth_strength {
        grant_rows.push(prop_row("Auth strength", a.clone()));
    }
    let grant = section("Grant controls", grant_rows);

    let mut sess_rows: Vec<Element> = Vec::new();
    if let Some(f) = &d.session.sign_in_frequency {
        sess_rows.push(prop_row("Sign-in frequency", f.clone()));
    }
    if let Some(b) = &d.session.persistent_browser {
        sess_rows.push(prop_row("Persistent browser", b.clone()));
    }
    sess_rows.push(prop_row("App-enforced restrictions", yesno(d.session.app_enforced)));
    sess_rows.push(prop_row("Cloud app security", yesno(d.session.cloud_app_security)));
    let session = section("Session controls", sess_rows);

    let props = section(
        "Properties",
        vec![
            prop_row("State", d.state.clone()),
            prop_row("Created", date10(&d.created)),
            prop_row("Modified", date10(&d.modified)),
            prop_row("ID", d.id.clone()),
        ],
    );

    let content = vstack((
        Element::from(title),
        desc,
        Element::from(conditions),
        Element::from(grant),
        Element::from(session),
        Element::from(props),
    ))
    .spacing(10.0)
    .margin(Thickness::uniform(4.0));

    scroll_viewer(content).height(height).into()
}

fn yesno(b: bool) -> String {
    if b { "Yes".into() } else { "No".into() }
}

pub fn ca_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (filter, set_filter) = cx.use_state(String::new());
    let (state_filter, set_state_filter) = cx.use_state(String::new()); // "" | enabled | report | disabled
    let (selected, set_selected) = cx.use_state(String::new());
    let (refresh, _bump) = cx.use_reducer(0_u64);

    let policies = cx.use_resource(|_: u64| api().ca_list().map_err(service_err), refresh);
    let detail = cx.use_resource(
        |key: (String, u64)| {
            let (id, _) = key;
            if id.is_empty() {
                return Ok(None);
            }
            api().ca_detail(&id).map(Some).map_err(service_err)
        },
        (selected.clone(), refresh),
    );

    let list_height = crate::list_height(cx);

    // Stats bar from the loaded list.
    let (n_en, n_rep, n_dis) = match policies.data() {
        Some(ps) => (
            ps.iter().filter(|p| p.state.eq_ignore_ascii_case("enabled")).count(),
            ps.iter().filter(|p| p.state.to_lowercase().contains("report")).count(),
            ps.iter().filter(|p| p.state.eq_ignore_ascii_case("disabled")).count(),
        ),
        None => (0, 0, 0),
    };
    let stats_bar = hstack((
        Element::from(body_strong("Conditional Access").font_family(theme::FONT_DISPLAY).font_size(16.0).foreground(theme::TEXT)),
        Element::from(stat_inline(format!("{n_en} enabled"), theme::OK)),
        Element::from(stat_inline(format!("{n_rep} report-only"), theme::WARN)),
        Element::from(stat_inline(format!("{n_dis} disabled"), theme::TEXT_3)),
    ))
    .spacing(16.0);

    // State filter chips.
    let chip_specs: [(&str, &str); 4] =
        [("All", ""), ("Enabled", "enabled"), ("Report-only", "report"), ("Disabled", "disabled")];
    let chips: Vec<Element> = chip_specs
        .iter()
        .map(|&(label, val)| {
            let active = state_filter == val;
            let set_sf = set_state_filter.clone();
            let v = val.to_string();
            let mut b = button(label).on_click(move || set_sf.call(v.clone()));
            if active {
                b = b.accent();
            }
            Element::from(b)
        })
        .collect();
    let chip_row = hstack(chips).spacing(8.0);

    let filter_box = auto_suggest_box(filter.clone())
        .placeholder_text("Filter policies…".to_string())
        .on_text_changed({
            let s = set_filter.clone();
            move |t| s.call(t)
        });

    let header = ca_row_grid(
        caption("POLICY NAME").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("STATE").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("USERS").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("APPS").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("GRANT").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
    );

    let filter_lc = filter.to_lowercase();
    let sf = state_filter.clone();
    let sel = selected.clone();
    let set_sel_list = set_selected.clone();
    let list_el: Element = policies
        .view(move |all: &Vec<CaPolicyListItem>| -> Element {
            let matches: Vec<CaPolicyListItem> = all
                .iter()
                .filter(|p| {
                    let state_ok = sf.is_empty()
                        || (sf == "enabled" && p.state.eq_ignore_ascii_case("enabled"))
                        || (sf == "report" && p.state.to_lowercase().contains("report"))
                        || (sf == "disabled" && p.state.eq_ignore_ascii_case("disabled"));
                    let text_ok = filter_lc.is_empty()
                        || p.display_name.to_lowercase().contains(&filter_lc)
                        || p.users.to_lowercase().contains(&filter_lc)
                        || p.applications.to_lowercase().contains(&filter_lc);
                    state_ok && text_ok
                })
                .cloned()
                .collect();
            if matches.is_empty() {
                return body("No policies match.").opacity(0.6).wrap().into();
            }
            let ids: Vec<String> = matches.iter().map(|p| p.id.clone()).collect();
            let sel_index = matches.iter().position(|p| p.id == sel).map(|i| i as i32).unwrap_or(-1);
            let set_sel = set_sel_list.clone();
            list_view(matches, |p: &CaPolicyListItem, _| ca_row(p))
                .with_key_selector(|p: &CaPolicyListItem| p.id.clone())
                .selected_index(sel_index)
                .on_selection_changed(move |i| {
                    if let Some(id) = ids.get(i as usize) {
                        set_sel.call(id.clone());
                    }
                })
                .height(list_height - 150.0)
                .into()
        })
        .loading(caption("loading policies…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let left = crate::fluid_fill(
        10.0,
        vec![
            Element::from(stats_bar),
            Element::from(chip_row),
            Element::from(filter_box),
            Element::from(header),
        ],
        list_el,
    );

    let right: Element = if selected.is_empty() {
        body("Select a policy to see its full conditions, grant, and session controls.")
            .opacity(0.6)
            .wrap()
            .into()
    } else {
        detail
            .view(move |d: &Option<CaDetail>| -> Element {
                match d {
                    Some(d) => ca_detail_panel(d, list_height - 20.0),
                    None => caption("loading details…").opacity(0.6).into(),
                }
            })
            .loading(caption("loading details…").opacity(0.6))
            .error(|e| crate::error_box(e))
            .into()
    };

    // Key the detail panel on the selection so it fully remounts per policy — the
    // reactor's in-place reconcile otherwise leaves deep sub-elements stale from the
    // previously-selected policy. See the nested-component re-render gotcha.
    grid((left.grid_column(0), right.with_key(selected.clone()).grid_column(1)))
        .columns([GridLength::Star(1.7), GridLength::Star(1.0)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
