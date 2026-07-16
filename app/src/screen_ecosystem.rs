//! M21 Ecosystem — shareable packs, playbooks, and a plugin marketplace. A tab
//! switcher over three master/detail lists:
//!   Packs        GET /packs        → "Preview plan" (POST /packs/{id}/adopt, confirm:false)
//!                                     then "Approve & apply" (confirm:true + planId)
//!   Playbooks    GET /playbooks    → "Run" (POST /playbooks/{id}/run)
//!   Marketplace  GET /marketplace  → "Install" (POST /marketplace/{id}/install)
//!
//! Each detail pane renders a parameter/config FORM (one input per declared
//! PackParameter / playbook parameter / MarketplaceConfigField, honoring required +
//! default), and the bound values ride the request: a pack adopt sends `parameters`
//! (so targetGroupId reaches the M15 plan), a playbook run sends `parameters` (so
//! deviceId reaches the isolate steps), an install sends `config` (so servicenow-itsm's
//! instance/token reach the plugins.json row).
//!
//! Locked guarantee: a pack adopt drives the M15 gitops plan first (plan-only, a read);
//! applying stays a second, explicit, operator-confirmed step (confirm:true + the
//! reviewed planId). A playbook run lands native steps as gated PendingChanges in the
//! M13 inbox; a marketplace install only appends a plugins.json row. Nothing
//! auto-applies a write.

use std::collections::HashMap;

use api_types::{
    MarketplaceConfigField, MarketplaceEntry, MarketplaceInstallRequest, PackAdoptRequest,
    PackManifest, PackParameter, Playbook, PlaybookRunRequest,
};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

const TABS: [(&str, &str); 3] =
    [("packs", "Packs"), ("playbooks", "Playbooks"), ("marketplace", "Marketplace")];

fn tab_idx(t: &str) -> i32 {
    TABS.iter().position(|(k, _)| *k == t).map(|i| i as i32).unwrap_or(0)
}

// A namespaced key for a single form field's typed value — per (tab, item id, field
// name) so switching item or tab never bleeds one form's input into another.
fn fkey(tab: &str, id: &str, field: &str) -> String {
    format!("{tab}\u{1}{id}\u{1}{field}")
}

// One editable form field, normalized across the three artifact kinds.
#[derive(Clone)]
struct FieldDesc {
    name: String,
    default: Option<String>,
    required: bool,
    placeholder: String,
}

impl FieldDesc {
    fn from_param(p: &PackParameter) -> Self {
        FieldDesc {
            name: p.name.clone(),
            default: p.default.clone(),
            required: p.required,
            placeholder: p.prompt.clone().unwrap_or_else(|| p.r#type.clone()),
        }
    }
    fn from_config(c: &MarketplaceConfigField) -> Self {
        FieldDesc {
            name: c.name.clone(),
            default: None,
            required: c.required,
            placeholder: if c.r#type == "secret" { "secret".into() } else { c.r#type.clone() },
        }
    }
}

// The action to dispatch. Params are captured (as (name,value) pairs) at click time
// from the current field values, so the async closure never reads stale UI state.
// PackApply carries only the reviewed planId — the M15 apply re-derives the object set
// from the stored plan, not from parameters.
#[derive(Clone, PartialEq, Eq, Hash)]
enum PendingAction {
    PackPlan { id: String, params: Vec<(String, String)> },
    PackApply { id: String, plan_id: String },
    PlaybookRun { id: String, params: Vec<(String, String)> },
    Install { id: String, config: Vec<(String, String)> },
}

// The outcome carried in the action resource. `plan_id` is Some only after a pack PLAN
// succeeds — its presence (for the selected pack) reveals the "Approve & apply" button.
#[derive(Clone, PartialEq)]
struct ActionOutcome {
    subject: String,
    message: String,
    plan_id: Option<String>,
}

fn to_map(pairs: Vec<(String, String)>) -> Option<HashMap<String, String>> {
    if pairs.is_empty() {
        None
    } else {
        Some(pairs.into_iter().collect())
    }
}

fn pill(text: &str, color: Color) -> Element {
    border(caption(text.to_string()).font_size(11.0).font_family(theme::FONT_UI).foreground(color))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(4.0)
        .padding(Thickness::xy(6.0, 2.0))
        .into()
}

fn pack_row(p: &PackManifest) -> Element {
    vstack((
        Element::from(body_strong(p.name.clone()).foreground(theme::TEXT).wrap()),
        Element::from(
            caption(format!(
                "v{} · {} · {} object(s)",
                p.version,
                p.publisher.clone().unwrap_or_else(|| "—".into()),
                p.object_count
            ))
            .foreground(theme::TEXT_4)
            .font_family(theme::FONT_UI),
        ),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

fn playbook_row(p: &Playbook) -> Element {
    vstack((
        Element::from(body_strong(p.name.clone()).foreground(theme::TEXT).wrap()),
        Element::from(
            caption(format!("v{} · {} step(s)", p.version, p.steps.len()))
                .foreground(theme::TEXT_4)
                .font_family(theme::FONT_UI),
        ),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

fn market_row(m: &MarketplaceEntry) -> Element {
    let verified: Element =
        if m.verified { pill("verified", theme::OK) } else { pill("community", theme::TEXT_4) };
    let installed: Element = if m.installed { pill("installed", theme::BRAND_BRIGHT) } else { Element::Empty };
    vstack((
        Element::from(
            hstack((
                verified,
                installed,
                Element::from(body_strong(m.name.clone()).foreground(theme::TEXT).wrap()),
            ))
            .spacing(6.0),
        ),
        Element::from(
            caption(format!("{} · {}", m.transport, m.publisher.clone().unwrap_or_else(|| "—".into())))
                .foreground(theme::TEXT_4)
                .font_family(theme::FONT_UI),
        ),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

fn kv(label: &str, value: String) -> Element {
    Element::from(
        caption(format!("{label}: {value}"))
            .foreground(theme::TEXT_3)
            .font_family(theme::FONT_UI)
            .wrap(),
    )
}

// A labeled text input for one form field.
fn form_field(
    label: String,
    value: String,
    placeholder: String,
    on_change: impl Fn(String) + 'static,
) -> Element {
    vstack((
        Element::from(caption(label).foreground(theme::TEXT_4).font_family(theme::FONT_UI)),
        Element::from(text_box(value).placeholder_text(placeholder).on_text_changed(on_change)),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(0.0, 3.0))
    .into()
}

pub fn ecosystem_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (tab, set_tab) = cx.use_state(String::from(TABS[0].0));
    let (selected, set_selected) = cx.use_state(String::new());
    let (data_tick, bump_data) = cx.use_reducer(0_u64);
    let (act_target, set_act_target) = cx.use_state::<Option<PendingAction>>(None);
    let (act_tick, bump_act) = cx.use_reducer(0_u64);
    // Typed form-field values, keyed by fkey(tab, id, field). Lifted to the parent so
    // the inputs (structurally-stable list children) re-render on each keystroke.
    let (field_vals, set_field_vals) = cx.use_state::<HashMap<String, String>>(HashMap::new());

    let packs = cx.use_resource(|_: u64| api().packs().map_err(service_err), data_tick);
    let playbooks = cx.use_resource(|_: u64| api().playbooks().map_err(service_err), data_tick);
    let market = cx.use_resource(|_: u64| api().marketplace().map_err(service_err), data_tick);

    let action = cx.use_resource(
        |k: (Option<PendingAction>, u64)| -> std::result::Result<Option<ActionOutcome>, String> {
            match k.0 {
                None => Ok(None),
                Some(PendingAction::PackPlan { id, params }) => {
                    let req = PackAdoptRequest {
                        parameters: to_map(params),
                        confirm: false,
                        ..Default::default()
                    };
                    api().adopt_pack(&id, &req).map(|r| {
                        Some(ActionOutcome {
                            subject: id.clone(),
                            message: format!(
                                "Plan phase={}{}",
                                r.phase,
                                r.summary.map(|s| format!(" — {s}")).unwrap_or_default()
                            ),
                            plan_id: r.plan_id,
                        })
                    })
                }
                Some(PendingAction::PackApply { id, plan_id }) => {
                    let req = PackAdoptRequest {
                        parameters: None,
                        confirm: true,
                        plan_id: Some(plan_id),
                        confirm_destroy: false,
                    };
                    api().adopt_pack(&id, &req).map(|r| {
                        Some(ActionOutcome {
                            subject: id.clone(),
                            message: format!(
                                "Apply phase={}{}",
                                r.phase,
                                r.message.map(|m| format!(" — {m}")).unwrap_or_default()
                            ),
                            plan_id: None,
                        })
                    })
                }
                Some(PendingAction::PlaybookRun { id, params }) => api()
                    .run_playbook(&id, &PlaybookRunRequest { parameters: to_map(params) })
                    .map_err(service_err)
                    .map(|r| {
                        Some(ActionOutcome {
                            subject: id.clone(),
                            message: format!(
                                "Ran {} step(s); {} enqueued in Pending AI Changes",
                                r.steps.len(),
                                r.pending_change_ids.len()
                            ),
                            plan_id: None,
                        })
                    }),
                Some(PendingAction::Install { id, config }) => api()
                    .install_plugin(&id, &MarketplaceInstallRequest { config: to_map(config) })
                    .map(|r| {
                        Some(ActionOutcome {
                            subject: id.clone(),
                            message: format!(
                                "{} — {}",
                                if r.installed { "installed" } else { "not installed" },
                                r.message.unwrap_or_default()
                            ),
                            plan_id: None,
                        })
                    }),
            }
        },
        (act_target.clone(), act_tick),
    );

    // Refresh lists after an action completes (e.g. marketplace installed flag).
    let acted = matches!(action.data(), Some(Some(_)));
    {
        let bump_data = bump_data.clone();
        cx.use_effect(acted, move || {
            if acted {
                bump_data.call(|n| n + 1);
            }
        });
    }

    let list_height = crate::list_height(cx);
    let act_busy = action.is_loading();

    let tab_combo = ComboBox::new(TABS.iter().map(|(_, l)| l.to_string()).collect::<Vec<_>>())
        .header("Catalog")
        .selected_index(tab_idx(&tab))
        .on_selection_changed({
            let set_tab = set_tab.clone();
            let set_selected = set_selected.clone();
            move |i: i32| {
                if let Some((k, _)) = TABS.get(i as usize) {
                    set_tab.call(k.to_string());
                    set_selected.call(String::new());
                }
            }
        });

    // ── Left: the active tab's list ────────────────────────────────────────
    let sel = selected.clone();
    let tab_key = tab.clone();
    let list_el: Element = match tab.as_str() {
        "playbooks" => playbooks
            .view({
                let sel = sel.clone();
                let set_selected = set_selected.clone();
                let list_height = list_height;
                move |rows: &Vec<Playbook>| -> Element {
                    if rows.is_empty() {
                        return body("No playbooks in the catalog.").opacity(0.6).wrap().into();
                    }
                    let rows = rows.clone();
                    let ids: Vec<String> = rows.iter().map(|p| p.id.clone()).collect();
                    let idx = ids.iter().position(|i| *i == sel).map(|i| i as i32).unwrap_or(-1);
                    let set_selected = set_selected.clone();
                    list_view(rows, |p: &Playbook, _| playbook_row(p))
                        .with_key_selector(|p: &Playbook| p.id.clone())
                        .selected_index(idx)
                        .on_selection_changed(move |i| {
                            if let Some(id) = ids.get(i as usize) {
                                set_selected.call(id.clone());
                            }
                        })
                        .height((list_height - 70.0).max(140.0))
                        .into()
                }
            })
            .loading(caption("loading playbooks…").opacity(0.6))
            .error(|e| crate::error_box(e))
            .into(),
        "marketplace" => market
            .view({
                let sel = sel.clone();
                let set_selected = set_selected.clone();
                let list_height = list_height;
                move |rows: &Vec<MarketplaceEntry>| -> Element {
                    if rows.is_empty() {
                        return body("No marketplace entries.").opacity(0.6).wrap().into();
                    }
                    let rows = rows.clone();
                    let ids: Vec<String> = rows.iter().map(|m| m.id.clone()).collect();
                    let idx = ids.iter().position(|i| *i == sel).map(|i| i as i32).unwrap_or(-1);
                    let set_selected = set_selected.clone();
                    list_view(rows, |m: &MarketplaceEntry, _| market_row(m))
                        .with_key_selector(|m: &MarketplaceEntry| m.id.clone())
                        .selected_index(idx)
                        .on_selection_changed(move |i| {
                            if let Some(id) = ids.get(i as usize) {
                                set_selected.call(id.clone());
                            }
                        })
                        .height((list_height - 70.0).max(140.0))
                        .into()
                }
            })
            .loading(caption("loading marketplace…").opacity(0.6))
            .error(|e| crate::error_box(e))
            .into(),
        _ => packs
            .view({
                let sel = sel.clone();
                let set_selected = set_selected.clone();
                let list_height = list_height;
                move |rows: &Vec<PackManifest>| -> Element {
                    if rows.is_empty() {
                        return body("No packs in the catalog (drop packs under %LocalAppData%\\cmProjectX\\packs).")
                            .opacity(0.6)
                            .wrap()
                            .into();
                    }
                    let rows = rows.clone();
                    let ids: Vec<String> = rows.iter().map(|p| p.id.clone()).collect();
                    let idx = ids.iter().position(|i| *i == sel).map(|i| i as i32).unwrap_or(-1);
                    let set_selected = set_selected.clone();
                    list_view(rows, |p: &PackManifest, _| pack_row(p))
                        .with_key_selector(|p: &PackManifest| p.id.clone())
                        .selected_index(idx)
                        .on_selection_changed(move |i| {
                            if let Some(id) = ids.get(i as usize) {
                                set_selected.call(id.clone());
                            }
                        })
                        .height((list_height - 70.0).max(140.0))
                        .into()
                }
            })
            .loading(caption("loading packs…").opacity(0.6))
            .error(|e| crate::error_box(e))
            .into(),
    };
    let left = crate::fluid_fill(12.0, vec![Element::from(tab_combo)], list_el);

    // ── Form fields for the selected item ──────────────────────────────────
    let cur_fields: Vec<FieldDesc> = if selected.is_empty() {
        Vec::new()
    } else {
        match tab_key.as_str() {
            "playbooks" => playbooks
                .data()
                .and_then(|v| v.iter().find(|p| p.id == selected))
                .map(|p| p.parameters.iter().map(FieldDesc::from_param).collect())
                .unwrap_or_default(),
            "marketplace" => market
                .data()
                .and_then(|v| v.iter().find(|m| m.id == selected))
                .map(|m| m.config_fields.iter().map(FieldDesc::from_config).collect())
                .unwrap_or_default(),
            _ => packs
                .data()
                .and_then(|v| v.iter().find(|p| p.id == selected))
                .map(|p| p.parameters.iter().map(FieldDesc::from_param).collect())
                .unwrap_or_default(),
        }
    };

    // Effective (name, value) pairs for the current item: a typed non-empty value wins,
    // else the declared default. Only non-empty pairs ride the request (the server also
    // re-applies defaults, so an omitted optional is fine).
    let cur_params: Vec<(String, String)> = cur_fields
        .iter()
        .filter_map(|f| {
            let k = fkey(&tab_key, &selected, &f.name);
            field_vals
                .get(&k)
                .cloned()
                .filter(|s| !s.is_empty())
                .or_else(|| f.default.clone().filter(|s| !s.is_empty()))
                .map(|v| (f.name.clone(), v))
        })
        .collect();

    // Every required field must resolve to a value before the action can fire.
    let can_act = cur_fields
        .iter()
        .all(|f| !f.required || cur_params.iter().any(|(n, _)| n == &f.name));

    // ── Right: selected item detail + form + action ────────────────────────
    let action_label = match tab_key.as_str() {
        "playbooks" => "Run playbook",
        "marketplace" => "Install",
        _ => "Preview plan",
    };

    let primary_action = match tab_key.as_str() {
        "playbooks" => PendingAction::PlaybookRun { id: selected.clone(), params: cur_params.clone() },
        "marketplace" => PendingAction::Install { id: selected.clone(), config: cur_params.clone() },
        _ => PendingAction::PackPlan { id: selected.clone(), params: cur_params.clone() },
    };

    let action_btn: Element = if selected.is_empty() {
        Element::Empty
    } else {
        Element::from(
            button(if act_busy { "Working…" } else { action_label })
                .accent()
                .enabled(!act_busy && can_act)
                .on_click({
                    let set_act_target = set_act_target.clone();
                    let bump_act = bump_act.clone();
                    let primary_action = primary_action.clone();
                    move || {
                        set_act_target.call(Some(primary_action.clone()));
                        bump_act.call(|n| n + 1);
                    }
                }),
        )
    };

    // "Approve & apply" surfaces only after a plan for THIS pack returned a planId.
    let last_outcome: Option<ActionOutcome> = action.data().and_then(|o| o.clone());
    let apply_plan_id: Option<String> = if tab_key == "packs" && !selected.is_empty() {
        last_outcome
            .as_ref()
            .and_then(|o| if o.subject == selected { o.plan_id.clone() } else { None })
    } else {
        None
    };
    let apply_btn: Element = match apply_plan_id {
        Some(pid) => Element::from(
            button(if act_busy { "Working…" } else { "Approve & apply plan" })
                .accent()
                .enabled(!act_busy)
                .on_click({
                    let set_act_target = set_act_target.clone();
                    let bump_act = bump_act.clone();
                    let id = selected.clone();
                    move || {
                        set_act_target
                            .call(Some(PendingAction::PackApply { id: id.clone(), plan_id: pid.clone() }));
                        bump_act.call(|n| n + 1);
                    }
                }),
        ),
        None => Element::Empty,
    };

    // Build the per-field inputs (empty when nothing is selected).
    let mut field_inputs: Vec<Element> = Vec::new();
    if !cur_fields.is_empty() {
        field_inputs.push(Element::from(
            caption(if tab_key == "marketplace" { "Configuration" } else { "Parameters" })
                .foreground(theme::TEXT_4)
                .font_family(theme::FONT_UI),
        ));
        for f in &cur_fields {
            let key = fkey(&tab_key, &selected, &f.name);
            let val = field_vals.get(&key).cloned().unwrap_or_else(|| f.default.clone().unwrap_or_default());
            let label = format!("{}{}", f.name, if f.required { " *" } else { "" });
            let placeholder = f.placeholder.clone();
            let on_change = {
                let set_field_vals = set_field_vals.clone();
                let snapshot = field_vals.clone();
                let key = key.clone();
                move |t: String| {
                    let mut m = snapshot.clone();
                    m.insert(key.clone(), t);
                    set_field_vals.call(m);
                }
            };
            field_inputs.push(form_field(label, val, placeholder, on_change));
        }
    }

    let detail_body: Element = if selected.is_empty() {
        body("Select an item to see its detail and action.").opacity(0.6).wrap().into()
    } else {
        let mut items: Vec<Element> = match tab_key.as_str() {
            "playbooks" => match playbooks.data().and_then(|v| v.iter().find(|p| p.id == selected)) {
                None => vec![Element::Empty],
                Some(p) => {
                    let mut items = vec![
                        Element::from(body_strong(p.name.clone()).foreground(theme::TEXT)),
                        kv("version", p.version.clone()),
                        kv("steps", p.steps.iter().map(|s| s.tool.clone()).collect::<Vec<_>>().join(" → ")),
                    ];
                    if let Some(d) = &p.description {
                        items.push(kv("description", d.clone()));
                    }
                    items
                }
            },
            "marketplace" => match market.data().and_then(|v| v.iter().find(|m| m.id == selected)) {
                None => vec![Element::Empty],
                Some(m) => {
                    let fields = m
                        .config_fields
                        .iter()
                        .map(|f| format!("{}{}", f.name, if f.required { "*" } else { "" }))
                        .collect::<Vec<_>>()
                        .join(", ");
                    let mut items = vec![
                        Element::from(body_strong(m.name.clone()).foreground(theme::TEXT)),
                        kv("transport", m.transport.clone()),
                        kv("trust", m.trust.clone().unwrap_or_else(|| "—".into())),
                        kv("config fields", if fields.is_empty() { "none".into() } else { fields }),
                    ];
                    if let Some(d) = &m.description {
                        items.push(kv("description", d.clone()));
                    }
                    items.push(Element::from(
                        caption("Install writes a plugins.json row; secrets are stored in cleartext and a sidecar restart is needed to load new tools.")
                            .foreground(theme::TEXT_4)
                            .wrap(),
                    ));
                    items
                }
            },
            _ => match packs.data().and_then(|v| v.iter().find(|p| p.id == selected)) {
                None => vec![Element::Empty],
                Some(p) => {
                    let mut items = vec![
                        Element::from(body_strong(p.name.clone()).foreground(theme::TEXT)),
                        kv("version", p.version.clone()),
                        kv("publisher", p.publisher.clone().unwrap_or_else(|| "—".into())),
                        kv("target surfaces", p.target_surfaces.join(", ")),
                        kv("objects", p.object_count.to_string()),
                    ];
                    if let Some(d) = &p.description {
                        items.push(kv("description", d.clone()));
                    }
                    items.push(Element::from(
                        caption("Preview plan runs the M15 gitops plan (read-only). Applying is a separate, confirmed step.")
                            .foreground(theme::TEXT_4)
                            .wrap(),
                    ));
                    items
                }
            },
        };
        items.extend(field_inputs);
        vstack(items).spacing(4.0).into()
    };

    let action_result: Element = action
        .view(|r: &Option<ActionOutcome>| -> Element {
            match r {
                None => Element::Empty,
                Some(o) => border(caption(o.message.clone()).foreground(theme::OK).wrap())
                    .background(theme::SURFACE_2)
                    .border_brush(theme::LINE)
                    .corner_radius(6.0)
                    .padding(Thickness::uniform(10.0))
                    .into(),
            }
        })
        .loading(caption("running action…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    let right = crate::fluid_fill(
        10.0,
        vec![action_btn, apply_btn, action_result],
        scroll_viewer(detail_body).height((list_height - 60.0).max(160.0)).into(),
    );

    grid((left.grid_column(0), right.grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(1.2)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
