//! Sign-in interstitial — a FULL-WINDOW takeover shown whenever the sidecar
//! reports any auth state other than `SignedIn`. The root `app` returns this as
//! the window's render root (no nav rail / shell bar / header) until sign-in
//! completes, then swaps in the NavigationView shell.
//!
//! The root owns the /health poll and passes the latest snapshot down as props
//! every tick; the changing `refresh` prop also guarantees this component keeps
//! re-rendering (see assignments.rs on why that matters). Everything interactive
//! in here (sign-in / profile-switch mutations, picker override, copy feedback)
//! lives in this component's own hooks.

use api_types::{
    AuthMethod, AuthState, Cloud, DeviceCodePrompt, NewProfile, SyncStatus, TenantProfileSummary,
};
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::theme;

/// Props from the root: the last successful /health snapshot (None until the
/// first poll lands), the live transport error when the sidecar is
/// unreachable, and the root poll counter.
#[derive(Clone, PartialEq)]
pub struct LoginProps {
    pub status: Option<SyncStatus>,
    pub service_error: Option<String>,
    pub refresh: u64,
}

pub fn login_workspace(props: &LoginProps, cx: &mut RenderCx) -> Element {
    // Tenant-profile roster. Re-keyed on a coarse cadence (every ~10 polls)
    // plus a local generation bumped after a successful switch, so is_active
    // tracks the server without refetching every 2s.
    let (prof_gen, bump_prof) = cx.use_reducer(0_u64);
    let profiles = cx.use_resource(
        |_key: (u64, u64)| api().profiles().map_err(service_err),
        (props.refresh / 10, prof_gen),
    );
    // Snapshot the roster for the management panel's Edit/Delete list, so it doesn't
    // depend on re-reading the resource after the picker below consumes it.
    let profs_snapshot = profiles.data().cloned();

    let (signin, do_signin) = cx.use_mutation::<()>();
    let (activate, do_activate) = cx.use_mutation::<()>();
    // Cancel an in-flight interactive/device-code prompt: signs out, which makes the
    // sidecar abort the blocking token acquisition so the user can retry.
    let (cancel_mut, do_cancel) = cx.use_mutation::<()>();
    // Picker override: the index the user chose, shown until the server's
    // is_active catches up (the roster refetch below). -1 = follow the server.
    let (picked, set_picked) = cx.use_state(-1_i32);
    // Last code copied to the clipboard — drives the "Copied ✓" label.
    let (copied, set_copied) = cx.use_state(String::new());

    // ── Profile management (M11): add / edit / delete + cloud selector ──
    // A collapsible panel under the card; `edit_id == ""` means we're creating.
    let (managing, set_managing) = cx.use_state(false);
    let (edit_id, set_edit_id) = cx.use_state(String::new());
    let (f_name, set_f_name) = cx.use_state(String::new());
    let (f_tenant, set_f_tenant) = cx.use_state(String::new());
    let (f_client, set_f_client) = cx.use_state(String::new());
    let (f_secret, set_f_secret) = cx.use_state(String::new());
    let (f_cloud, set_f_cloud) = cx.use_state(String::from("Commercial"));
    let (f_method, set_f_method) = cx.use_state(String::from("ClientSecret"));
    let (prof_mut, do_prof) = cx.use_mutation::<()>();
    // After a successful add/edit/delete, refetch the roster and close the form.
    let prof_done = prof_mut.data().is_some();
    {
        let bump_prof = bump_prof.clone();
        let do_prof = do_prof.clone();
        let set_managing = set_managing.clone();
        let set_edit_id = set_edit_id.clone();
        cx.use_effect(prof_done, move || {
            if prof_done {
                bump_prof.call(|n| n + 1);
                set_managing.call(false);
                set_edit_id.call(String::new());
                do_prof.reset();
            }
        });
    }

    // After a successful switch, refetch the roster (is_active moved) and
    // clear the mutation so a stale error can't linger into the next switch.
    let switched = activate.data().is_some();
    {
        let bump_prof = bump_prof.clone();
        let do_activate = do_activate.clone();
        cx.use_effect(switched, move || {
            if switched {
                bump_prof.call(|n| n + 1);
                do_activate.reset();
            }
        });
    }

    let auth = props.status.as_ref().map(|s| s.auth_state);
    let connecting = props.status.is_none() && props.service_error.is_none();
    let unreachable = props.service_error.is_some();
    // Busy = a sign-in is in flight, locally or server-side. Between the
    // accepted POST and the next poll the button can briefly re-enable; a
    // duplicate POST just 409s and surfaces as a normal error line.
    let busy = signin.is_loading()
        || matches!(
            auth,
            Some(AuthState::SigningIn)
                | Some(AuthState::AwaitingDeviceCode)
                | Some(AuthState::AwaitingInteractive)
        );

    // The active profile's auth method drives the CTA copy (browser vs device-code
    // vs app-only). Follows the server's is_active, not the picker override.
    let active_method = profs_snapshot
        .as_ref()
        .and_then(|ps| ps.iter().find(|p| p.is_active))
        .and_then(|p| p.auth_method);

    // ── Header (right panel) ──────────────────────────────────────────────
    // The wordmark/brand identity now lives in the left hero; this is just the
    // form's "Sign in" heading.
    let title = body_strong("Sign in")
        .font_size(24.0)
        .font_family(theme::FONT_DISPLAY)
        .foreground(theme::TEXT);
    let tagline = body("Pick a tenant, then connect to Entra & Intune.")
        .foreground(theme::TEXT_3)
        .wrap();

    // ── Current-state section ────────────────────────────────────────────
    // `connecting` is the cloud-connect moment → tint it cloud-blue (the one
    // place blue is allowed to lead, per BRAND.md). Everything else is teal/neutral.
    let state_el: Element = if connecting {
        cloud_wait_line("connecting to service…")
    } else if let Some(e) = &props.service_error {
        body(e.clone())
            .wrap()
            .foreground(theme::ERROR)
            .into()
    } else {
        match (auth, props.status.as_ref()) {
            (Some(AuthState::AwaitingDeviceCode), Some(s)) => match &s.device_code {
                Some(dc) => device_code_panel(dc, &copied, &set_copied),
                None => wait_line("waiting for the device code…"),
            },
            (Some(AuthState::AwaitingInteractive), _) => {
                cloud_wait_line("Continue in your browser to finish signing in…")
            }
            (Some(AuthState::SigningIn), _) => wait_line("Signing in…"),
            (Some(AuthState::Failed), Some(s)) => {
                let msg = s.error.clone().unwrap_or_else(|| "Sign-in failed.".into());
                border(body(msg).wrap().foreground(theme::ERROR))
                    .corner_radius(6.0)
                    .padding(Thickness::uniform(12.0))
                    .background(theme::SURFACE_2)
                    .into()
            }
            (Some(AuthState::SignedIn), _) => caption("Signed in ✓")
                .foreground(theme::BRAND_BRIGHT)
                .into(),
            _ => caption("Signed out — pick a tenant and sign in.")
                .foreground(theme::TEXT_3)
                .into(),
        }
    };

    // ── Tenant/cloud picker ──────────────────────────────────────────────
    let activating = activate.is_loading();
    let picker: Element = {
        let set_picked = set_picked.clone();
        let do_activate = do_activate.clone();
        profiles
            .view(move |profs: &Vec<TenantProfileSummary>| -> Element {
                if profs.is_empty() {
                    return caption("No tenant profiles configured in the sidecar yet.")
                        .opacity(0.6)
                        .wrap()
                        .into();
                }
                let items: Vec<String> = profs.iter().map(profile_label).collect();
                let server_active = profs
                    .iter()
                    .position(|p| p.is_active)
                    .map(|i| i as i32)
                    .unwrap_or(-1);
                let shown = if picked >= 0 && (picked as usize) < profs.len() {
                    picked
                } else {
                    server_active
                };
                let ids: Vec<String> = profs.iter().map(|p| p.id.clone()).collect();
                ComboBox::new(items)
                    .header("Tenant profile")
                    .placeholder_text("Pick a tenant profile…")
                    .selected_index(shown)
                    .enabled(!activating && !busy)
                    .on_selection_changed(move |i: i32| {
                        // Reconciliation re-asserts SelectedIndex
                        // programmatically; only a genuinely different index
                        // is a user action.
                        if i < 0 || i == shown {
                            return;
                        }
                        let Some(id) = ids.get(i as usize).cloned() else {
                            return;
                        };
                        set_picked.call(i);
                        do_activate
                            .fire(move || api().activate_profile(&id).map_err(service_err));
                    })
                    .into()
            })
            .loading(caption("loading tenant profiles…").opacity(0.6))
            .error(|e| {
                caption(format!("couldn't load profiles: {e}"))
                    .opacity(0.7)
                    .wrap()
                    .into()
            })
            .into()
    };

    // ── CTA ──────────────────────────────────────────────────────────────
    let cta_label = if matches!(auth, Some(AuthState::AwaitingInteractive)) {
        "Continue in browser…"
    } else if busy {
        "Signing in…"
    } else if matches!(auth, Some(AuthState::Failed)) {
        "Retry sign-in"
    } else {
        match active_method {
            Some(AuthMethod::Interactive) => "Sign in with browser",
            Some(AuthMethod::DeviceCode) => "Sign in with device code",
            _ => "Sign in",
        }
    };
    let cta = button(cta_label)
        .accent()
        .enabled(!busy && !connecting && !unreachable)
        .on_click({
            let m = do_signin.clone();
            move || m.fire(begin_sign_in)
        });

    // ── Non-fatal notes ──────────────────────────────────────────────────
    let mut notes: Vec<Element> = Vec::new();
    if let Some(e) = signin.error() {
        notes.push(
            caption(format!("sign-in failed: {e}"))
                .opacity(0.85)
                .wrap()
                .into(),
        );
    }
    if activating {
        notes.push(caption("switching tenant…").opacity(0.6).into());
    } else if let Some(e) = activate.error() {
        notes.push(
            caption(format!("couldn't switch tenant: {e}"))
                .opacity(0.85)
                .wrap()
                .into(),
        );
    }

    // ── Status strip: tenant + cloud + auth state ────────────────────────
    let strip: Element = match &props.status {
        Some(s) => {
            let tenant = s
                .profile_name
                .clone()
                .or_else(|| s.tenant_id.clone())
                .unwrap_or_else(|| "no active profile".into());
            let cloud = s.cloud.map(cloud_label).unwrap_or("—");
            caption(format!(
                "{tenant} · {cloud} · {}",
                crate::auth_label(s.auth_state)
            ))
            .foreground(theme::TEXT_4)
            .into()
        }
        None => Element::Empty,
    };

    // ── Profile management panel (collapsed → a button; expanded → a form) ─
    let creating = edit_id.is_empty();
    let manage_el: Element = if !managing {
        Element::from(button("Manage tenant profiles").on_click({
            let set_managing = set_managing.clone();
            let set_edit_id = set_edit_id.clone();
            let set_f_name = set_f_name.clone();
            let set_f_tenant = set_f_tenant.clone();
            let set_f_client = set_f_client.clone();
            let set_f_secret = set_f_secret.clone();
            let set_f_cloud = set_f_cloud.clone();
            let set_f_method = set_f_method.clone();
            move || {
                set_edit_id.call(String::new());
                set_f_name.call(String::new());
                set_f_tenant.call(String::new());
                set_f_client.call(String::new());
                set_f_secret.call(String::new());
                set_f_cloud.call("Commercial".to_string());
                set_f_method.call("ClientSecret".to_string());
                set_managing.call(true);
            }
        }))
    } else {
        let name_tb = text_box(f_name.clone())
            .placeholder_text("Display name".to_string())
            .on_text_changed({ let s = set_f_name.clone(); move |t| s.call(t) });
        let tenant_tb = text_box(f_tenant.clone())
            .placeholder_text("Tenant ID (GUID or domain)".to_string())
            .on_text_changed({ let s = set_f_tenant.clone(); move |t| s.call(t) });
        let client_tb = text_box(f_client.clone())
            .placeholder_text(if creating { "Client ID" } else { "Client ID (blank = keep)" }.to_string())
            .on_text_changed({ let s = set_f_client.clone(); move |t| s.call(t) });
        let secret_tb = text_box(f_secret.clone())
            .placeholder_text(if creating { "Client secret (optional)" } else { "Client secret (blank = keep)" }.to_string())
            .on_text_changed({ let s = set_f_secret.clone(); move |t| s.call(t) });

        let cloud_combo = ComboBox::new(CLOUDS.iter().map(|s| s.to_string()).collect::<Vec<_>>())
            .header("Cloud")
            .selected_index(idx_of(&CLOUDS, &f_cloud))
            .on_selection_changed({
                let s = set_f_cloud.clone();
                move |i: i32| {
                    if let Some(v) = CLOUDS.get(i as usize) {
                        s.call(v.to_string());
                    }
                }
            });
        let method_combo = ComboBox::new(METHODS.iter().map(|s| s.to_string()).collect::<Vec<_>>())
            .header("Auth method")
            .selected_index(idx_of(&METHODS, &f_method))
            .on_selection_changed({
                let s = set_f_method.clone();
                move |i: i32| {
                    if let Some(v) = METHODS.get(i as usize) {
                        s.call(v.to_string());
                    }
                }
            });

        let saving = prof_mut.is_loading();
        let can_save = !saving
            && (!creating || (!f_name.is_empty() && !f_tenant.is_empty() && !f_client.is_empty()));
        let save_btn = button(if saving {
            "Saving…"
        } else if creating {
            "Add profile"
        } else {
            "Update profile"
        })
        .accent()
        .enabled(can_save)
        .on_click({
            let do_prof = do_prof.clone();
            let edit_id = edit_id.clone();
            let f_name = f_name.clone();
            let f_tenant = f_tenant.clone();
            let f_client = f_client.clone();
            let f_secret = f_secret.clone();
            let f_cloud = f_cloud.clone();
            let f_method = f_method.clone();
            move || {
                let np = NewProfile {
                    name: f_name.clone(),
                    tenant_id: f_tenant.clone(),
                    client_id: f_client.clone(),
                    cloud: cloud_from_label(&f_cloud),
                    auth_method: method_from_label(&f_method),
                    client_secret: if f_secret.is_empty() { None } else { Some(f_secret.clone()) },
                };
                let edit_id = edit_id.clone();
                if edit_id.is_empty() {
                    do_prof.fire(move || api().create_profile(&np).map_err(service_err));
                } else {
                    do_prof.fire(move || api().update_profile(&edit_id, &np).map_err(service_err));
                }
            }
        });
        let cancel_btn = button("Cancel").enabled(!saving).on_click({
            let s = set_managing.clone();
            move || s.call(false)
        });

        // Existing profiles with Edit (prefill the form) / Delete.
        let roster: Element = match &profs_snapshot {
            Some(profs) if !profs.is_empty() => {
                let rows: Vec<Element> = profs
                    .iter()
                    .map(|p| {
                        let edit = button("Edit").on_click({
                            let set_edit_id = set_edit_id.clone();
                            let set_f_name = set_f_name.clone();
                            let set_f_tenant = set_f_tenant.clone();
                            let set_f_client = set_f_client.clone();
                            let set_f_secret = set_f_secret.clone();
                            let set_f_cloud = set_f_cloud.clone();
                            let set_f_method = set_f_method.clone();
                            let id = p.id.clone();
                            let name = p.name.clone();
                            let tenant = p.tenant_id.clone();
                            let cloud = p.cloud.map(cloud_to_value).unwrap_or("Commercial").to_string();
                            let method =
                                p.auth_method.map(method_to_value).unwrap_or("ClientSecret").to_string();
                            move || {
                                set_edit_id.call(id.clone());
                                set_f_name.call(name.clone());
                                set_f_tenant.call(tenant.clone());
                                set_f_client.call(String::new());
                                set_f_secret.call(String::new());
                                set_f_cloud.call(cloud.clone());
                                set_f_method.call(method.clone());
                            }
                        });
                        let del = button("Delete").on_click({
                            let do_prof = do_prof.clone();
                            let id = p.id.clone();
                            move || {
                                let id = id.clone();
                                do_prof.fire(move || api().delete_profile(&id).map_err(service_err));
                            }
                        });
                        border(
                            hstack((
                                caption(p.name.clone()).foreground(theme::TEXT_2),
                                Element::from(edit),
                                Element::from(del),
                            ))
                            .spacing(8.0),
                        )
                        .corner_radius(4.0)
                        .padding(Thickness::uniform(6.0))
                        .background(theme::SURFACE_3)
                        .into()
                    })
                    .collect();
                vstack(rows).spacing(4.0).into()
            }
            Some(_) => caption("No saved profiles.").opacity(0.6).into(),
            None => caption("loading profiles…").opacity(0.5).into(),
        };

        let err: Element = prof_mut
            .error()
            .map(|e| caption(format!("failed: {e}")).opacity(0.85).wrap().into())
            .unwrap_or(Element::Empty);

        border(
            vstack((
                body_strong(if creating { "Add tenant profile" } else { "Edit tenant profile" }),
                Element::from(name_tb),
                Element::from(tenant_tb),
                Element::from(client_tb),
                Element::from(secret_tb),
                Element::from(cloud_combo),
                Element::from(method_combo),
                Element::from(
                    hstack((Element::from(save_btn), Element::from(cancel_btn))).spacing(8.0),
                ),
                Element::from(caption("Saved profiles").foreground(theme::TEXT_3)),
                roster,
                err,
            ))
            .spacing(8.0),
        )
        .corner_radius(6.0)
        .padding(Thickness::uniform(12.0))
        .background(theme::SURFACE_2)
        .border_thickness(Thickness::uniform(1.0))
        .border_brush(theme::LINE)
        .into()
    };

    // ── Right-panel card assembly (sign-in form) ──────────────────────────
    // Same items, same order, same logic — only the container is re-themed.
    // Cancel affordance — only while an interactive/device-code prompt is pending, so
    // a user who closed the browser (or never finishes) can back out and retry instead
    // of being stuck on "Continue in your browser…".
    let cancel_el: Element = if matches!(
        auth,
        Some(AuthState::AwaitingInteractive) | Some(AuthState::AwaitingDeviceCode)
    ) {
        Element::from(
            button(if cancel_mut.is_loading() { "Canceling…" } else { "Cancel" })
                .enabled(!cancel_mut.is_loading())
                .on_click({
                    let m = do_cancel.clone();
                    move || m.fire(|| api().sign_out().map_err(service_err))
                }),
        )
    } else {
        Element::Empty
    };

    let mut items: Vec<Element> = vec![
        title.into(),
        tagline.into(),
        state_el,
        picker,
        Element::from(cta),
        cancel_el,
    ];
    items.extend(notes);
    items.push(manage_el);
    items.push(strip);

    let card = border(vstack(items).spacing(14.0))
        .corner_radius(8.0)
        .padding(Thickness::uniform(32.0))
        .border_thickness(Thickness::uniform(1.0))
        .border_brush(theme::LINE)
        .background(theme::SURFACE)
        .width(440.0)
        .horizontal_alignment(HorizontalAlignment::Center)
        .vertical_alignment(VerticalAlignment::Center);

    // ── Split Hero ────────────────────────────────────────────────────────
    // Two-column grid: col 0 = deep-teal brand hero, col 1 = the form card on
    // the app background. No gradient brush exists in the reactor (Brush::Solid
    // only), so the hero is a SOLID deep-teal panel (teal-20 #00312A) — the
    // expected, brand-sanctioned fallback.
    // Full-window takeover: this grid is the window's render root, so size it to
    // the whole client area (no shell chrome to subtract) — both Star columns
    // then stretch edge-to-edge and the hero fills top-to-bottom.
    let h = cx.use_inner_size().height.max(380.0);

    let hero = hero_panel().grid_column(0);

    let form_pane = border(Element::from(card))
        .background(theme::BG)
        .grid_column(1);

    grid((hero, form_pane))
        .columns([GridLength::Star(1.1), GridLength::Star(1.0)])
        .rows([GridLength::Star(1.0)])
        .height(h)
        .into()
}

// The deep-teal brand hero (left column of the Split Hero). Solid panel — the
// reactor has no gradient brush. Contains: a simple stacked-bars logo mark + the
// two-tone wordmark, the product tagline, and the two heritage feature lines
// (Diagnostics = teal, Management = the one sanctioned cloud-blue cue).
fn hero_panel() -> Element {
    // Logo mark approximation: three stacked "log bars" (the bottom one bright,
    // per the brand mark spec) built from small borders — no SVG/Shape needed.
    let bar = |w: f64, color: Color| -> Element {
        border(Element::Empty)
            .background(color)
            .corner_radius(2.0)
            .width(w)
            .height(6.0)
            .horizontal_alignment(HorizontalAlignment::Left)
            .into()
    };
    let mark = vstack((
        bar(40.0, theme::BRAND),
        bar(28.0, theme::BRAND),
        bar(34.0, theme::BRAND_BRIGHT),
    ))
    .spacing(5.0);

    // Two-tone wordmark: "Intune" teal-bright, "Commander" near-white, Bahnschrift.
    let wordmark = hstack((
        body_strong("Intune")
            .font_size(30.0)
            .font_family(theme::FONT_DISPLAY)
            .foreground(theme::BRAND_BRIGHT),
        body_strong("Commander")
            .font_size(30.0)
            .font_family(theme::FONT_DISPLAY)
            .foreground(theme::TEXT),
    ))
    .spacing(0.0);

    let tagline = body("Local logs, enriched with live Entra & Intune context.")
        .foreground(theme::TEXT_2)
        .wrap();

    // Feature lines — teal leads (Diagnostics); Management gets the one
    // sanctioned cloud-blue cue (it's the Entra/Intune half).
    let feature = |dot: Color, label: &str| -> Element {
        hstack((
            border(Element::Empty)
                .background(dot)
                .corner_radius(5.0)
                .width(10.0)
                .height(10.0)
                .vertical_alignment(VerticalAlignment::Center),
            body(label.to_string()).foreground(theme::TEXT_2),
        ))
        .spacing(10.0)
        .into()
    };
    let features = vstack((
        feature(theme::BRAND_BRIGHT, "Diagnostics — local logs, severity, timeline"),
        feature(theme::CLOUD_BRIGHT, "Management — tenant, assignments, drift, restore"),
    ))
    .spacing(10.0);

    border(
        vstack((
            Element::from(mark),
            Element::from(wordmark),
            Element::from(tagline),
            Element::from(features),
        ))
        .spacing(20.0),
    )
    .background(theme::TEAL_DEEP)
    .padding(Thickness::uniform(48.0))
    .into()
}

// POST /auth/signin, with the sidecar's 409 (no active profile / flow already
// running) translated into something actionable instead of a raw status line.
fn begin_sign_in() -> std::result::Result<(), String> {
    api().sign_in().map_err(|e| {
        if e.status() == Some(reqwest::StatusCode::CONFLICT) {
            "Couldn't start sign-in — no tenant profile is active, or a sign-in is already in progress.".to_string()
        } else {
            service_err(e)
        }
    })
}

// The prominent device-code prompt: big monospaced user code + copy button, a
// hyperlink that opens the verification URI in the default browser, and the
// sidecar's full instruction message.
fn device_code_panel(dc: &DeviceCodePrompt, copied: &str, set_copied: &SetState<String>) -> Element {
    let copy_btn = button(if copied == dc.user_code {
        "Copied ✓"
    } else {
        "Copy code"
    })
    .on_click({
        let set_copied = set_copied.clone();
        let code = dc.user_code.clone();
        move || {
            if copy_to_clipboard(&code).is_ok() {
                set_copied.call(code.clone());
            }
        }
    });
    // The device-login link is a cloud/Entra cue → tint it cloud-blue.
    let open = HyperlinkButton::new(dc.verification_uri.clone())
        .navigate_uri(dc.verification_uri.clone())
        .foreground(theme::CLOUD_BRIGHT);
    border(
        vstack((
            caption("Finish signing in from a browser").foreground(theme::TEXT_3),
            hstack((
                body_strong(dc.user_code.clone())
                    .font_size(24.0)
                    .font_family(theme::FONT_MONO)
                    .foreground(theme::TEXT),
                Element::from(copy_btn),
            ))
            .spacing(12.0),
            Element::from(open),
            caption(dc.message.clone()).foreground(theme::TEXT_4).wrap(),
        ))
        .spacing(8.0),
    )
    .corner_radius(6.0)
    .padding(Thickness::uniform(16.0))
    .background(theme::SURFACE_2)
    .border_thickness(Thickness::uniform(1.0))
    .border_brush(theme::CLOUD_DEEP)
    .into()
}

// Small indeterminate spinner + muted caption, for the transitional states.
fn wait_line(msg: &str) -> Element {
    hstack((
        Element::from(ProgressRing::indeterminate().width(18.0).height(18.0)),
        caption(msg).foreground(theme::TEXT_3),
    ))
    .spacing(10.0)
    .into()
}

// Like `wait_line`, but tinted cloud-blue — for the cloud-connect moment (the
// one place blue is allowed to lead, per BRAND.md).
fn cloud_wait_line(msg: &str) -> Element {
    hstack((
        Element::from(ProgressRing::indeterminate().width(18.0).height(18.0)),
        caption(msg).foreground(theme::CLOUD_BRIGHT),
    ))
    .spacing(10.0)
    .into()
}

fn profile_label(p: &TenantProfileSummary) -> String {
    let cloud = p.cloud.map(cloud_label).unwrap_or("Commercial");
    let method = p.auth_method.map(auth_method_label).unwrap_or("interactive");
    format!("{} — {} · {} · {}", p.name, p.tenant_id, cloud, method)
}

fn cloud_label(c: Cloud) -> &'static str {
    match c {
        Cloud::Commercial => "Commercial",
        Cloud::Gcc => "GCC",
        Cloud::GccHigh => "GCC High",
        Cloud::DoD => "DoD",
    }
}

fn auth_method_label(m: AuthMethod) -> &'static str {
    match m {
        AuthMethod::Interactive => "interactive",
        AuthMethod::ClientSecret => "client secret",
        AuthMethod::DeviceCode => "device code",
    }
}

// ── Profile-form combo options + enum<->canonical-string mapping (M11) ──
// The canonical strings match the api-types serde names and the sidecar's
// Cloud/AuthMethod parsing exactly, so a round-trip preserves the selection.
const CLOUDS: [&str; 4] = ["Commercial", "GCC", "GCCHigh", "DoD"];
const METHODS: [&str; 3] = ["Interactive", "ClientSecret", "DeviceCode"];

fn idx_of(list: &[&str], v: &str) -> i32 {
    list.iter().position(|x| *x == v).map(|i| i as i32).unwrap_or(0)
}

fn cloud_from_label(s: &str) -> Option<Cloud> {
    Some(match s {
        "GCC" => Cloud::Gcc,
        "GCCHigh" => Cloud::GccHigh,
        "DoD" => Cloud::DoD,
        _ => Cloud::Commercial,
    })
}

fn method_from_label(s: &str) -> Option<AuthMethod> {
    Some(match s {
        "ClientSecret" => AuthMethod::ClientSecret,
        "DeviceCode" => AuthMethod::DeviceCode,
        _ => AuthMethod::Interactive,
    })
}

fn cloud_to_value(c: Cloud) -> &'static str {
    match c {
        Cloud::Commercial => "Commercial",
        Cloud::Gcc => "GCC",
        Cloud::GccHigh => "GCCHigh",
        Cloud::DoD => "DoD",
    }
}

fn method_to_value(m: AuthMethod) -> &'static str {
    match m {
        AuthMethod::Interactive => "Interactive",
        AuthMethod::ClientSecret => "ClientSecret",
        AuthMethod::DeviceCode => "DeviceCode",
    }
}

// Pipe the text through clip.exe — no clipboard API in the reactor's bindings,
// and this avoids pulling a whole `windows` crate dep for one call. Runs on
// the UI thread but clip.exe round-trips in a few ms.
fn copy_to_clipboard(text: &str) -> std::io::Result<()> {
    use std::io::Write;
    use std::os::windows::process::CommandExt;
    use std::process::{Command, Stdio};
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;
    let mut child = Command::new("clip.exe")
        .creation_flags(CREATE_NO_WINDOW)
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()?;
    child
        .stdin
        .take()
        .expect("piped stdin")
        .write_all(text.as_bytes())?;
    child.wait()?;
    Ok(())
}
