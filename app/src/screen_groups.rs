//! Groups workspace — like-to-like with IntuneCommander's GroupsWorkspace +
//! GroupDetailPanel: a filterable group list on the left, and a rich detail panel
//! on the right (status grid · properties · dynamic membership rule · member table
//! with user/device/group type badges). Data from the sidecar's /groups and
//! /groups/{id}/detail. Brand colors via theme.rs.

use api_types::{GroupDetail, GroupMember, ListItem};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

fn stat_inline(text: String, color: Color) -> Element {
    caption(text).foreground(color).font_family(theme::FONT_UI).into()
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

fn status_grid(cards: Vec<(&str, String)>) -> Element {
    let cols: Vec<GridLength> = cards.iter().map(|_| GridLength::Star(1.0)).collect();
    let cells: Vec<Element> = cards
        .into_iter()
        .enumerate()
        .map(|(i, (label, value))| stat_card(label, value).grid_column(i as i32))
        .collect();
    grid(cells).columns(cols).column_spacing(10.0).into()
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
    .columns([GridLength::Pixel(150.0), GridLength::Star(1.0)])
    .column_spacing(8.0)
    .margin(Thickness::xy(0.0, 2.0))
    .into()
}

fn section(title: &str, children: Vec<Element>) -> Element {
    let mut all = vec![Element::from(
        body_strong(title.to_string()).font_family(theme::FONT_UI).foreground(theme::BRAND_BRIGHT),
    )];
    all.extend(children);
    vstack(all).spacing(4.0).margin(Thickness::xy(0.0, 6.0)).into()
}

fn date10(s: &str) -> String {
    s.split('T').next().unwrap_or(s).to_string()
}

fn yesno(b: bool) -> String {
    if b { "Yes".into() } else { "No".into() }
}

// A small colored square badge carrying the member-type initial (U / D / G).
fn member_type_badge(t: &str) -> Element {
    let (color, letter) = match t {
        "User" => (theme::BRAND_BRIGHT, "U"),
        "Device" => (theme::OK, "D"),
        _ => (theme::WARN, "G"),
    };
    border(
        body_strong(letter.to_string())
            .font_size(11.0)
            .font_family(theme::FONT_UI)
            .foreground(color)
            .horizontal_alignment(HorizontalAlignment::Center),
    )
    .background(theme::SURFACE_2)
    .corner_radius(4.0)
    .width(22.0)
    .padding(Thickness::xy(0.0, 2.0))
    .into()
}

// One four-column member row (shared by the header and each member).
fn member_row_grid(type_el: Element, name: Element, info: Element, status: Element) -> Element {
    grid((
        type_el.grid_column(0),
        name.grid_column(1),
        info.grid_column(2),
        status.grid_column(3),
    ))
    .columns([
        GridLength::Pixel(36.0),
        GridLength::Star(2.0),
        GridLength::Star(2.0),
        GridLength::Pixel(110.0),
    ])
    .column_spacing(8.0)
    .margin(Thickness::xy(0.0, 3.0))
    .into()
}

fn member_row(m: &GroupMember) -> Element {
    let info = if !m.secondary_info.is_empty() {
        m.secondary_info.clone()
    } else if !m.tertiary_info.is_empty() {
        m.tertiary_info.clone()
    } else {
        "—".to_string()
    };
    let status_color = if m.status == "Enabled" || m.status == "Managed" {
        theme::OK
    } else {
        theme::TEXT_3
    };
    let status_text = if m.status.is_empty() { "—".to_string() } else { m.status.clone() };
    member_row_grid(
        member_type_badge(&m.member_type),
        caption(m.display_name.clone()).foreground(theme::TEXT).font_family(theme::FONT_UI).wrap().into(),
        caption(info).foreground(theme::TEXT_3).font_family(theme::FONT_UI).wrap().into(),
        caption(status_text).foreground(status_color).font_family(theme::FONT_UI).into(),
    )
}

fn group_row(item: &ListItem) -> Element {
    vstack((
        Element::from(
            body_strong(item.title.clone()).font_family(theme::FONT_UI).foreground(theme::TEXT).wrap(),
        ),
        Element::from(caption(item.subtitle.clone()).foreground(theme::TEXT_3).font_family(theme::FONT_UI)),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 5.0))
    .into()
}

fn group_detail_panel(d: &GroupDetail, height: f64) -> Element {
    let title = body_strong(d.display_name.clone())
        .font_size(18.0)
        .font_family(theme::FONT_DISPLAY)
        .foreground(theme::TEXT)
        .wrap();
    let desc: Element = match &d.description {
        Some(s) if !s.is_empty() => caption(s.clone()).foreground(theme::TEXT_3).wrap().into(),
        _ => Element::Empty,
    };

    let counts = &d.counts;
    let status = status_grid(vec![
        ("Members", counts.total.to_string()),
        ("Users", counts.users.to_string()),
        ("Devices", counts.devices.to_string()),
        ("Nested", counts.nested_groups.to_string()),
    ]);

    let mut prop_rows = vec![
        prop_row("Group type", d.group_type.clone()),
        prop_row("Security enabled", yesno(d.security_enabled)),
        prop_row("Mail enabled", yesno(d.mail_enabled)),
    ];
    if let Some(m) = &d.mail {
        if !m.is_empty() {
            prop_rows.push(prop_row("Mail", m.clone()));
        }
    }
    if let Some(s) = &d.membership_rule_processing_state {
        if !s.is_empty() {
            prop_rows.push(prop_row("Rule processing", s.clone()));
        }
    }
    if let Some(c) = &d.created_date_time {
        prop_rows.push(prop_row("Created", date10(c)));
    }
    let props = section("Properties", prop_rows);

    // Dynamic membership rule — a monospace code block when present.
    let rule_section: Element = match &d.membership_rule {
        Some(r) if !r.is_empty() => section(
            "Dynamic membership rule",
            vec![Element::from(
                border(
                    caption(r.clone()).font_family(theme::FONT_MONO).foreground(theme::TEXT).wrap(),
                )
                .background(theme::SURFACE_2)
                .border_brush(theme::LINE)
                .corner_radius(6.0)
                .padding(Thickness::uniform(10.0)),
            )],
        ),
        _ => Element::Empty,
    };

    // Members table (header + rows, capped so a huge group stays responsive).
    let header = member_row_grid(
        caption("").into(),
        caption("NAME").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("INFO").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
        caption("STATUS").foreground(theme::TEXT_4).font_family(theme::FONT_UI).into(),
    );
    let cap = 150usize;
    let mut member_els: Vec<Element> = vec![Element::from(header)];
    if d.members.is_empty() {
        member_els.push(Element::from(caption("No members found").foreground(theme::TEXT_3)));
    } else {
        member_els.extend(d.members.iter().take(cap).map(member_row));
        if d.members.len() > cap {
            member_els.push(Element::from(
                caption(format!("… showing first {cap} of {} members", d.members.len()))
                    .foreground(theme::TEXT_4),
            ));
        }
    }
    let members = section(&format!("Members ({})", counts.total), member_els);

    let content = vstack((
        Element::from(title),
        desc,
        Element::from(status),
        Element::from(props),
        rule_section,
        Element::from(members),
    ))
    .spacing(10.0)
    .margin(Thickness::uniform(4.0));

    scroll_viewer(content).height(height).into()
}

pub fn groups_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (filter, set_filter) = cx.use_state(String::new());
    let (selected, set_selected) = cx.use_state(String::new());
    let (refresh, _bump) = cx.use_reducer(0_u64);

    let groups = cx.use_resource(|_: u64| api().get_list("/groups").map_err(service_err), refresh);
    let detail = cx.use_resource(
        |key: (String, u64)| {
            let (id, _) = key;
            if id.is_empty() {
                return Ok(None);
            }
            api().group_detail(&id).map(Some).map_err(service_err)
        },
        (selected.clone(), refresh),
    );

    let list_height = crate::list_height(cx);
    let total = groups.data().map(|g| g.len()).unwrap_or(0);
    let stats_bar = hstack((
        Element::from(
            body_strong("Groups")
                .font_family(theme::FONT_DISPLAY)
                .font_size(16.0)
                .foreground(theme::TEXT),
        ),
        Element::from(stat_inline(format!("{total} total"), theme::TEXT_2)),
    ))
    .spacing(16.0);

    let filter_box = auto_suggest_box(filter.clone())
        .placeholder_text("Filter groups…".to_string())
        .on_text_changed({
            let s = set_filter.clone();
            move |t| s.call(t)
        });

    let filter_lc = filter.to_lowercase();
    let sel = selected.clone();
    let set_sel_list = set_selected.clone();
    let list_el: Element = groups
        .view(move |all: &Vec<ListItem>| -> Element {
            let matches: Vec<ListItem> = all
                .iter()
                .filter(|g| {
                    filter_lc.is_empty()
                        || g.title.to_lowercase().contains(&filter_lc)
                        || g.subtitle.to_lowercase().contains(&filter_lc)
                })
                .cloned()
                .collect();
            if matches.is_empty() {
                return body("No groups match. Sign in (top right) if this needs Graph data.")
                    .opacity(0.6)
                    .wrap()
                    .into();
            }
            let ids: Vec<String> = matches.iter().map(|g| g.id.clone()).collect();
            let sel_index = matches.iter().position(|g| g.id == sel).map(|i| i as i32).unwrap_or(-1);
            let set_sel = set_sel_list.clone();
            list_view(matches, |g: &ListItem, _| group_row(g))
                .with_key_selector(|g: &ListItem| g.id.clone())
                .selected_index(sel_index)
                .on_selection_changed(move |i| {
                    if let Some(id) = ids.get(i as usize) {
                        set_sel.call(id.clone());
                    }
                })
                .height(list_height - 90.0)
                .into()
        })
        .loading(caption("loading groups…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let left = crate::fluid_fill(
        10.0,
        vec![Element::from(stats_bar), Element::from(filter_box)],
        list_el,
    );

    let right: Element = if selected.is_empty() {
        body("Select a group to see its members, membership rule, and properties.")
            .opacity(0.6)
            .wrap()
            .into()
    } else {
        detail
            .view(move |d: &Option<GroupDetail>| -> Element {
                match d {
                    Some(d) => group_detail_panel(d, list_height - 20.0),
                    None => caption("loading details…").opacity(0.6).into(),
                }
            })
            .loading(caption("loading details…").opacity(0.6))
            .error(|e| crate::error_box(e))
            .into()
    };

    // Key the detail panel on the selection so it fully REMOUNTS when the selected
    // group changes — without this, the reactor's nested-dirty-reconcile leaves deep
    // sub-elements (e.g. the member table) stale from the previously-selected group
    // while shallower ones (the stat cards) repaint. See reactor_nested_component
    // gotcha: force a fresh subtree rather than relying on in-place reconcile.
    grid((left.grid_column(0), right.with_key(selected.clone()).grid_column(1)))
        .columns([GridLength::Star(1.2), GridLength::Star(1.0)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
