//! M20 Fleet — MSP-scale multi-tenant fan-out (Pattern G). A tenant-group picker +
//! surface picker over a tenant-tagged fan-out LIST (GET /fleet/list/{surface}), with
//! a per-tenant status strip + errors strip so one tenant's throttle doesn't fail the
//! view. On the right, a campaign panel broadcasts a golden tenant's object to the
//! group as a DRY-RUN (POST /fleet/campaign, dryRun), previewing per-tenant diffs with
//! the M6 drift_row panel.
//!
//! Locked guarantee: a live (non-dry-run) campaign enqueues ONE gated M13 pending
//! change per tenant — never a bulk write. This screen runs dry-run previews only;
//! applying a campaign stays in the Pending AI Changes inbox.

use api_types::{
    CampaignRequest, CampaignResult, CampaignTenantResult, FleetDriftResponse, FleetDriftTenant,
    FleetListItem, FleetListResponse, FleetPostureResponse, FleetTenant, GoldenRef, TenantGroup,
};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

// Fleet-supported LIST surfaces (FleetSurfaceReader) offered in the picker.
const SURFACES: [&str; 5] = [
    "compliance-policies",
    "device-configs",
    "settings-catalog",
    "app-protection",
    "endpoint-security",
];

fn pill(text: &str, color: Color) -> Element {
    border(caption(text.to_string()).font_size(11.0).font_family(theme::FONT_UI).foreground(color))
        .background(theme::SURFACE_2)
        .border_brush(theme::LINE)
        .corner_radius(4.0)
        .padding(Thickness::xy(6.0, 2.0))
        .into()
}

fn status_color(s: &str) -> Color {
    match s {
        "ok" => theme::OK,
        "throttled" | "signedOut" => theme::WARN,
        _ => theme::ERROR,
    }
}
fn outcome_color(o: &str) -> Color {
    match o {
        "success" => theme::OK,
        "pending" => theme::BRAND_BRIGHT,
        "skip" => theme::TEXT_4,
        _ => theme::ERROR,
    }
}
fn drift_status_color(s: &str) -> Color {
    match s {
        "golden" => theme::BRAND_BRIGHT,
        "inSync" => theme::OK,
        "diverged" => theme::WARN,
        _ => theme::ERROR,
    }
}
// Posture pill colour: score bands when scored, else the tenant's status.
fn posture_color(status: &str, score: Option<i32>) -> Color {
    match (status, score) {
        (_, Some(s)) if s >= 70 => theme::OK,
        (_, Some(s)) if s >= 40 => theme::WARN,
        (_, Some(_)) => theme::ERROR,
        ("signedOut", _) => theme::WARN,
        _ => theme::ERROR,
    }
}

// One tenant's divergence from the resolved effective golden. Accepted overrides are
// folded into the golden server-side (from the stored template), so they read inSync.
fn drift_tenant_row(t: &FleetDriftTenant) -> Element {
    let mut items = vec![Element::from(
        hstack((
            pill(&t.status, drift_status_color(&t.status)),
            Element::from(body_strong(t.tenant_name.clone()).foreground(theme::TEXT).wrap()),
            Element::from(caption(format!("{} drift", t.drift_count)).foreground(theme::TEXT_4)),
        ))
        .spacing(8.0),
    )];
    if let Some(note) = &t.note {
        items.push(Element::from(caption(note.clone()).foreground(theme::TEXT_4).wrap()));
    }
    for d in t.drifts.iter().take(6) {
        let g = d.golden.as_ref().map(|v| v.to_string()).unwrap_or_else(|| "—".to_string());
        let a = d.actual.as_ref().map(|v| v.to_string()).unwrap_or_else(|| "—".to_string());
        items.push(Element::from(
            caption(format!("{}: golden {} · actual {}", d.field, g, a)).foreground(theme::TEXT_3).wrap(),
        ));
    }
    border(vstack(items).spacing(3.0)).corner_radius(4.0).padding(Thickness::uniform(8.0)).into()
}

fn fleet_row(it: &FleetListItem) -> Element {
    vstack((
        Element::from(
            hstack((
                pill(&it.tenant_name, theme::BRAND),
                Element::from(body_strong(it.title.clone()).foreground(theme::TEXT).wrap()),
            ))
            .spacing(8.0),
        ),
        Element::from(caption(it.subtitle.clone()).foreground(theme::TEXT_4).wrap()),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

fn camp_row(r: &CampaignTenantResult) -> Element {
    let mut items = vec![Element::from(
        hstack((
            pill(&r.outcome, outcome_color(&r.outcome)),
            Element::from(body_strong(r.tenant_name.clone()).foreground(theme::TEXT).wrap()),
        ))
        .spacing(8.0),
    )];
    if let Some(reason) = &r.reason {
        items.push(Element::from(caption(reason.clone()).foreground(theme::TEXT_4).wrap()));
    }
    items.extend(r.diff.iter().take(8).map(|c| crate::drift_row(c)));
    border(vstack(items).spacing(3.0)).corner_radius(4.0).padding(Thickness::uniform(8.0)).into()
}

pub fn fleet_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (group_id, set_group_id) = cx.use_state(String::new());
    let (surface, set_surface) = cx.use_state(String::from(SURFACES[0]));
    let (obj_name, set_obj_name) = cx.use_state(String::new());
    let (golden, set_golden) = cx.use_state(String::new());
    let (data_tick, bump_data) = cx.use_reducer(0_u64);
    let (camp_req, set_camp_req) = cx.use_state::<Option<CampaignRequest>>(None);
    let (camp_tick, bump_camp) = cx.use_reducer(0_u64);
    // Live-campaign confirm gate: false = dry-run only; true = the inline confirm panel
    // (modeled on the M6 review step — no modal in the reactor fork). A live fleet write
    // is N gated M13 replays, so it never fires without this explicit second step.
    let (live_review, set_live_review) = cx.use_state(false);
    // Fleet drift + posture reads (backend /fleet/drift, /fleet/posture). Armed on demand
    // via an Option request + tick, mirroring the campaign resource.
    let (drift_req, set_drift_req) =
        cx.use_state::<Option<(String, String, String, Option<String>)>>(None);
    let (drift_tick, bump_drift) = cx.use_reducer(0_u64);
    let (posture_req, set_posture_req) = cx.use_state::<Option<String>>(None);
    let (posture_tick, bump_posture) = cx.use_reducer(0_u64);
    let (newg, do_newg) = cx.use_mutation::<()>();

    let groups = cx.use_resource(|_: u64| api().fleet_groups().map_err(service_err), data_tick);
    let tenants = cx.use_resource(|_: u64| api().fleet_tenants().map_err(service_err), data_tick);

    let fleet_list = cx.use_resource(
        |k: (String, String, u64)| -> std::result::Result<Option<FleetListResponse>, String> {
            let (surface, group, _) = k;
            if group.is_empty() {
                return Ok(None);
            }
            api().fleet_list(&surface, &group).map(Some).map_err(service_err)
        },
        (surface.clone(), group_id.clone(), data_tick),
    );

    let campaign = cx.use_resource(
        |k: (Option<CampaignRequest>, u64)| -> std::result::Result<Option<CampaignResult>, String> {
            match k.0 {
                None => Ok(None),
                Some(req) => api().fleet_campaign(&req).map(Some).map_err(service_err),
            }
        },
        (camp_req.clone(), camp_tick),
    );

    let drift = cx.use_resource(
        |k: (Option<(String, String, String, Option<String>)>, u64)| -> std::result::Result<Option<FleetDriftResponse>, String> {
            match k.0 {
                None => Ok(None),
                Some((group, surface, obj, golden)) => api()
                    .fleet_drift(&group, &surface, &obj, golden.as_deref())
                    .map(Some)
                    .map_err(service_err),
            }
        },
        (drift_req.clone(), drift_tick),
    );

    let posture = cx.use_resource(
        |k: (Option<String>, u64)| -> std::result::Result<Option<FleetPostureResponse>, String> {
            match k.0 {
                None => Ok(None),
                Some(group) => api().fleet_posture(&group).map(Some).map_err(service_err),
            }
        },
        (posture_req.clone(), posture_tick),
    );

    // After creating a group, reload the groups + list.
    let newg_done = newg.data().is_some();
    {
        let bump_data = bump_data.clone();
        let do_newg = do_newg.clone();
        cx.use_effect(newg_done, move || {
            if newg_done {
                bump_data.call(|n| n + 1);
                do_newg.reset();
            }
        });
    }

    let groups_data: Vec<TenantGroup> = groups.data().cloned().unwrap_or_default();
    let tenants_data: Vec<FleetTenant> = tenants.data().cloned().unwrap_or_default();
    let list_height = crate::list_height(cx);
    let camp_busy = campaign.is_loading();

    // ── Header: group + surface pickers + new-group ────────────────────────
    let group_names: Vec<String> = groups_data.iter().map(|g| g.name.clone()).collect();
    let group_idx = groups_data.iter().position(|g| g.id == group_id).map(|i| i as i32).unwrap_or(-1);
    let group_ids: Vec<String> = groups_data.iter().map(|g| g.id.clone()).collect();
    let group_combo = ComboBox::new(group_names)
        .header("Tenant group")
        .selected_index(group_idx)
        .on_selection_changed({
            let set_group_id = set_group_id.clone();
            let set_camp_req = set_camp_req.clone();
            let set_live_review = set_live_review.clone();
            move |i: i32| {
                if let Some(id) = group_ids.get(i as usize) {
                    set_group_id.call(id.clone());
                    // Changing the target set invalidates any dry preview + live confirm.
                    set_camp_req.call(None);
                    set_live_review.call(false);
                }
            }
        });
    let surface_combo = ComboBox::new(SURFACES.iter().map(|s| s.to_string()).collect::<Vec<_>>())
        .header("Surface")
        .selected_index(SURFACES.iter().position(|s| *s == surface).map(|i| i as i32).unwrap_or(0))
        .on_selection_changed({
            let set_surface = set_surface.clone();
            let set_camp_req = set_camp_req.clone();
            let set_live_review = set_live_review.clone();
            move |i: i32| {
                if let Some(s) = SURFACES.get(i as usize) {
                    set_surface.call(s.to_string());
                    set_camp_req.call(None);
                    set_live_review.call(false);
                }
            }
        });
    let newg_btn = button(if newg.is_loading() { "Creating…" } else { "New group (all tenants)" })
        .enabled(!newg.is_loading())
        .on_click({
            let do_newg = do_newg.clone();
            move || {
                do_newg.fire(move || {
                    let tenants = api().fleet_tenants().map_err(service_err)?;
                    let ids: Vec<String> = tenants.iter().map(|t| t.tenant_id.clone()).collect();
                    if ids.is_empty() {
                        return Err("no known tenant profiles to group".to_string());
                    }
                    let g = TenantGroup {
                        id: String::new(),
                        name: "All tenants".to_string(),
                        tenant_ids: ids,
                        golden_tenant_id: None,
                        created_utc: String::new(),
                    };
                    api().fleet_upsert_group(&g).map(|_| ()).map_err(service_err)
                });
            }
        });
    let newg_err: Element = newg
        .error()
        .map(|e| caption(format!("group: {e}")).foreground(theme::ERROR).wrap().into())
        .unwrap_or(Element::Empty);
    let header = hstack((
        Element::from(group_combo),
        Element::from(surface_combo),
        Element::from(newg_btn),
        newg_err,
    ))
    .spacing(8.0);

    // ── Fan-out list (tenant-tagged) + status/error strips ─────────────────
    let sel_group = group_id.clone();
    let list_el: Element = fleet_list
        .view(move |resp: &Option<FleetListResponse>| -> Element {
            match resp {
                None => body("Pick (or create) a tenant group to fan out a LIST across it.")
                    .opacity(0.6)
                    .wrap()
                    .into(),
                Some(r) => {
                    let status_pills: Vec<Element> =
                        r.tenants.iter().map(|t| pill(&format!("{}: {}", t.tenant_name, t.status), status_color(&t.status))).collect();
                    let status_strip: Element = if status_pills.is_empty() {
                        Element::Empty
                    } else {
                        Element::from(hstack(status_pills).spacing(6.0))
                    };
                    let err_strip: Element = if r.errors.is_empty() {
                        Element::Empty
                    } else {
                        let items: Vec<Element> = r
                            .errors
                            .iter()
                            .map(|e| {
                                caption(format!("{}: {} ({})", e.tenant_id, e.message, e.code))
                                    .foreground(theme::ERROR)
                                    .wrap()
                                    .into()
                            })
                            .collect();
                        Element::from(vstack(items).spacing(2.0))
                    };
                    let rows: Element = if r.items.is_empty() {
                        caption("No items across the group for this surface.").opacity(0.6).into()
                    } else {
                        list_view(r.items.clone(), |it: &FleetListItem, _| fleet_row(it))
                            .with_key_selector(|it: &FleetListItem| format!("{}|{}", it.tenant_id, it.id))
                            .height((list_height - 210.0).max(140.0))
                            .into()
                    };
                    crate::fluid_fill(8.0, vec![status_strip, err_strip], rows).into()
                }
            }
        })
        .loading(caption("fanning out across the group…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();
    let _ = sel_group;

    let left = crate::fluid_fill(12.0, vec![Element::from(header)], list_el);

    // ── Campaign panel (dry-run) ───────────────────────────────────────────
    let golden_names: Vec<String> =
        tenants_data.iter().map(|t| t.tenant_name.clone()).collect();
    let golden_ids: Vec<String> = tenants_data.iter().map(|t| t.tenant_id.clone()).collect();
    let golden_idx = tenants_data.iter().position(|t| t.tenant_id == golden).map(|i| i as i32).unwrap_or(-1);
    let golden_combo = ComboBox::new(golden_names)
        .header("Golden tenant")
        .selected_index(golden_idx)
        .on_selection_changed({
            let set_golden = set_golden.clone();
            let set_camp_req = set_camp_req.clone();
            let set_live_review = set_live_review.clone();
            move |i: i32| {
                if let Some(id) = golden_ids.get(i as usize) {
                    set_golden.call(id.clone());
                    set_camp_req.call(None);
                    set_live_review.call(false);
                }
            }
        });
    let obj_tb = text_box(obj_name.clone())
        .placeholder_text("Object name to broadcast (as on the golden tenant)".to_string())
        .on_text_changed({
            let s = set_obj_name.clone();
            let set_camp_req = set_camp_req.clone();
            let set_live_review = set_live_review.clone();
            move |t| {
                s.call(t);
                // Editing the object name invalidates the reviewed dry preview.
                set_camp_req.call(None);
                set_live_review.call(false);
            }
        });
    // Build the CampaignRequest for the current inputs. dry=true previews + applies
    // nothing; dry=false enqueues one gated M13 replay per target (outcome=pending).
    let mk_req = {
        let group_id = group_id.clone();
        let surface = surface.clone();
        let obj_name = obj_name.clone();
        let golden = golden.clone();
        move |dry: bool| CampaignRequest {
            campaign_id: None,
            group: group_id.clone(),
            golden: GoldenRef {
                kind: "goldenTenant".to_string(),
                tenant_id: Some(golden.clone()),
                repo_ref: None,
            },
            surface: surface.clone(),
            object_name: obj_name.clone(),
            verb: None,
            dry_run: Some(dry),
            gate: Some("perTenantConfirm".to_string()),
            overrides: None,
        }
    };
    let inputs_ready = !group_id.is_empty() && !golden.is_empty() && !obj_name.is_empty();
    let dry_btn = button(if camp_busy { "Previewing…" } else { "Dry-run campaign" })
        .accent()
        .enabled(!camp_busy && inputs_ready)
        .on_click({
            let set_camp_req = set_camp_req.clone();
            let set_live_review = set_live_review.clone();
            let bump_camp = bump_camp.clone();
            let mk_req = mk_req.clone();
            move || {
                set_live_review.call(false); // a fresh preview clears any prior live confirm
                set_camp_req.call(Some(mk_req(true)));
                bump_camp.call(|n| n + 1);
            }
        });

    // A live fan-out is only reachable AFTER a dry-run preview the operator has seen
    // (mirrors the M6 Review → Confirm gate). The go-live button arms the inline confirm;
    // the confirm panel makes the "N gated replays, one per tenant" cost explicit.
    let last_result = campaign.data().and_then(|o| o.clone());
    let dry_preview_ready = last_result.as_ref().map(|r| r.dry_run).unwrap_or(false);
    let group_tenant_count =
        groups_data.iter().find(|g| g.id == group_id).map(|g| g.tenant_ids.len()).unwrap_or(0);
    let go_live_btn = button("Go live…")
        .enabled(!camp_busy && dry_preview_ready && !live_review)
        .on_click({
            let set_live_review = set_live_review.clone();
            move || set_live_review.call(true)
        });
    let live_panel: Element = if live_review {
        let confirm_btn = button(if camp_busy {
            "Queuing…".to_string()
        } else {
            format!("Queue {group_tenant_count} gated changes")
        })
        .accent()
        .enabled(!camp_busy && inputs_ready)
        .on_click({
            let set_camp_req = set_camp_req.clone();
            let set_live_review = set_live_review.clone();
            let bump_camp = bump_camp.clone();
            let mk_req = mk_req.clone();
            move || {
                set_camp_req.call(Some(mk_req(false)));
                bump_camp.call(|n| n + 1);
                set_live_review.call(false);
            }
        });
        let cancel_btn = button("Cancel").enabled(!camp_busy).on_click({
            let set_live_review = set_live_review.clone();
            move || set_live_review.call(false)
        });
        let warn = caption(format!(
            "Live fan-out: this enqueues ONE gated change per tenant ({group_tenant_count} total) \
             into Pending AI Changes. Nothing is written now — each change is approved \
             individually in its own tenant. This is not a bulk write.",
        ))
        .foreground(theme::WARN)
        .wrap();
        border(
            vstack((
                Element::from(body_strong("Confirm live campaign").font_family(theme::FONT_UI).foreground(theme::TEXT)),
                Element::from(warn),
                Element::from(hstack((Element::from(confirm_btn), Element::from(cancel_btn))).spacing(8.0)),
            ))
            .spacing(8.0),
        )
        .background(theme::SURFACE_2)
        .border_brush(theme::WARN)
        .corner_radius(6.0)
        .padding(Thickness::uniform(10.0))
        .into()
    } else {
        Element::from(go_live_btn)
    };
    let camp_note = caption(
        "Dry-run previews each tenant's diff and applies nothing. Go live enqueues one \
         gated change per tenant in Pending AI Changes — never a bulk write.",
    )
    .foreground(theme::TEXT_4)
    .wrap();

    let camp_result: Element = campaign
        .view(|r: &Option<CampaignResult>| -> Element {
            match r {
                None => caption("Pick a golden tenant + object, then Dry-run to preview the fan-out.")
                    .opacity(0.6)
                    .wrap()
                    .into(),
                Some(res) => {
                    let s = &res.summary;
                    let head = caption(format!(
                        "success {} · skip {} · conflict {} · error {} · pending {} (of {})",
                        s.success, s.skip, s.conflict, s.error, s.pending, s.total
                    ))
                    .foreground(theme::TEXT_2)
                    .font_family(theme::FONT_UI);
                    let rows: Vec<Element> = res.results.iter().map(camp_row).collect();
                    let kind = if res.dry_run { "dry-run" } else { "live — gated" };
                    let mut items = vec![
                        Element::from(caption(kind).foreground(theme::TEXT_4).font_family(theme::FONT_UI)),
                        Element::from(head),
                    ];
                    items.extend(rows);
                    vstack(items).spacing(6.0).into()
                }
            }
        })
        .loading(caption("running the campaign…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    // ── Fleet health: drift vs the resolved golden + posture roll-up ───────
    let drift_ready = !group_id.is_empty() && !obj_name.is_empty();
    let drift_btn = button(if drift.is_loading() { "Checking…" } else { "Check drift" })
        .enabled(!drift.is_loading() && drift_ready)
        .on_click({
            let set_drift_req = set_drift_req.clone();
            let bump_drift = bump_drift.clone();
            let group_id = group_id.clone();
            let surface = surface.clone();
            let obj_name = obj_name.clone();
            let golden = golden.clone();
            move || {
                let g = if golden.is_empty() { None } else { Some(golden.clone()) };
                set_drift_req.call(Some((group_id.clone(), surface.clone(), obj_name.clone(), g)));
                bump_drift.call(|n| n + 1);
            }
        });
    let posture_btn = button(if posture.is_loading() { "Scoring…" } else { "Fleet posture" })
        .enabled(!posture.is_loading() && !group_id.is_empty())
        .on_click({
            let set_posture_req = set_posture_req.clone();
            let bump_posture = bump_posture.clone();
            let group_id = group_id.clone();
            move || {
                set_posture_req.call(Some(group_id.clone()));
                bump_posture.call(|n| n + 1);
            }
        });
    let posture_el: Element = posture
        .view(|r: &Option<FleetPostureResponse>| -> Element {
            match r {
                None => caption("Run Fleet posture to score each tenant.").opacity(0.6).wrap().into(),
                Some(p) => {
                    let head = caption(format!("avg {} · {} scored", p.average_score, p.scored_tenants))
                        .foreground(theme::TEXT_2)
                        .font_family(theme::FONT_UI);
                    let pills: Vec<Element> = p
                        .tenants
                        .iter()
                        .map(|t| {
                            let label = match t.score {
                                Some(s) => format!("{}: {}", t.tenant_name, s),
                                None => format!("{}: {}", t.tenant_name, t.status),
                            };
                            pill(&label, posture_color(&t.status, t.score))
                        })
                        .collect();
                    vstack((Element::from(head), Element::from(hstack(pills).spacing(6.0)))).spacing(6.0).into()
                }
            }
        })
        .loading(caption("scoring the fleet…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();
    let drift_el: Element = drift
        .view(|r: &Option<FleetDriftResponse>| -> Element {
            match r {
                None => caption("Check drift to compare each tenant to the resolved golden.")
                    .opacity(0.6)
                    .wrap()
                    .into(),
                Some(d) => {
                    let s = &d.summary;
                    let head = caption(format!(
                        "golden {} · inSync {} · diverged {} · error {}",
                        s.golden, s.in_sync, s.diverged, s.error
                    ))
                    .foreground(theme::TEXT_2)
                    .font_family(theme::FONT_UI);
                    let rows: Vec<Element> = d.tenants.iter().map(drift_tenant_row).collect();
                    let mut items = vec![Element::from(head)];
                    items.extend(rows);
                    vstack(items).spacing(6.0).into()
                }
            }
        })
        .loading(caption("computing drift…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();
    let fleet_health = vstack((
        Element::from(body_strong("Fleet health").font_family(theme::FONT_UI).foreground(theme::TEXT_3)),
        Element::from(hstack((Element::from(drift_btn), Element::from(posture_btn))).spacing(8.0)),
        posture_el,
        drift_el,
    ))
    .spacing(8.0);

    let right_form = vstack((
        Element::from(body_strong("Campaign").font_family(theme::FONT_UI).foreground(theme::TEXT_3)),
        Element::from(golden_combo),
        Element::from(obj_tb),
        Element::from(dry_btn),
        live_panel,
        Element::from(camp_note),
    ))
    .spacing(8.0);
    let right = crate::fluid_fill(
        10.0,
        vec![Element::from(right_form)],
        scroll_viewer(vstack((camp_result, Element::from(fleet_health))).spacing(12.0))
            .height((list_height - 260.0).max(140.0))
            .into(),
    );

    grid((left.grid_column(0), right.grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(1.1)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
