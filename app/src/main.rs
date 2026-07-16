//! cmProjectX — WinUI 3 + Rust (Windows Reactor) client.
//!
//! A NavigationView shell over three workspaces (Timeline / Drift / Search)
//! plus a status bar that drives auth + sync. All data comes from the local
//! .NET sidecar over the OpenAPI contract; HTTP runs on Reactor's background
//! fetcher threads (`use_resource` / `use_mutation`) and results marshal back
//! onto the UI thread automatically.
//!
//! NOTE: console subsystem (no `#![windows_subsystem = "windows"]`) so panics
//! and service errors are visible during bring-up. Add the attribute for the
//! shipping build to suppress the console window.

mod api_client;
mod assignments;
mod bulk;
mod config_view;
mod diag_collector;
mod diag_eventlog;
mod diag_secureboot;
mod diag_sysmon;
mod diag_timeline;
mod charts;
mod dialogs;
mod reports;
mod features;
mod known_sources;
mod logview;
mod pending;
mod update_check;
mod screen_app_assignments;
mod screen_autonomy;
mod screen_bulk_assign;
mod screen_cache;
mod screen_ca;
mod screen_compare;
mod screen_detection;
mod screen_devices;
mod screen_ecosystem;
mod screen_fleet;
mod screen_gitops;
mod screen_assignment_explorer;
mod screen_groups;
mod screen_maester;
mod screen_simulator;
mod screen_twin;
mod screen_posture;
mod screen_posture_trend;
mod screen_tiles;
mod sidecar;
mod signin;
mod theme;
use api_client::{api, service_err};
use assignments::{assignment_editor, is_assignable, shows_assignments};
use bulk::{ca_pptx_workspace, export_workspace, import_workspace};
use screen_app_assignments::app_assignments_workspace;
use screen_autonomy::autonomy_workspace;
use screen_bulk_assign::bulk_assign_workspace;
use screen_cache::cache_workspace;
use screen_ca::ca_workspace;
use screen_compare::compare_workspace;
use screen_detection::detection_workspace;
use screen_devices::devices_workspace;
use screen_ecosystem::ecosystem_workspace;
use screen_fleet::fleet_workspace;
use screen_gitops::gitops_workspace;
use screen_assignment_explorer::assignment_explorer_workspace;
use screen_groups::groups_workspace;
use screen_maester::maester_workspace;
use screen_simulator::simulator_workspace;
use screen_twin::twin_workspace;
use screen_posture::posture_workspace;
use screen_posture_trend::posture_trend_workspace;
use diag_collector::collector_workspace;
use diag_eventlog::eventlog_workspace;
use diag_secureboot::secureboot_workspace;
use diag_sysmon::sysmon_workspace;
use diag_timeline::correlation_workspace;
use features::{Builtin, Screen};
use logview::logs_workspace;
use pending::pending_workspace;
use screen_tiles::tiles_workspace;
use signin::{LoginProps, login_workspace};

use api_types::{
    Assignment, AuditEvent, AuthState, DriftChange, DriftObject, DriftRecord, ListItem,
    SearchResult, SnapshotSummary, SyncStatus,
};
use cmtraceopen_parser::models::log_entry::{LogEntry, Severity};
use cmtraceopen_parser::{dsregcmd, error_db, parser};
use std::path::Path;
use std::time::{Duration, SystemTime};
use windows_reactor::*;

fn main() -> Result<()> {
    // Start (or reuse) the .NET sidecar — the app owns its lifecycle, so there's no
    // separate process to launch. Held for the app's lifetime; killed on exit.
    let _sidecar = sidecar::ensure_running();
    // Initialize the Windows App SDK bootstrapper before touching any WinUI types.
    let _bootstrap_handle = bootstrap()?;
    App::new()
        .title("IntuneCommander")
        .eager_templated_realization(true)
        .render(app)
}

// ─── Shell ───────────────────────────────────────────────────────────────

fn app(cx: &mut RenderCx) -> Element {
    // Initial workspace tag. Defaults to the Audit Timeline; `CMPX_START_TAB` (a
    // feature tag from `features::FEATURES`, e.g. "logs") overrides it so dev /
    // smoke runs can boot straight into a specific screen.
    let (tab, set_tab) = cx.use_state(
        std::env::var("CMPX_START_TAB").unwrap_or_else(|_| String::from("timeline")),
    );
    let (pane_open, set_pane_open) = cx.use_state(true);

    // Health-poll counter, bumped every 2s (tightened from 5s so the sign-in
    // screen surfaces device codes and auto-advances promptly). It lives here
    // at the root — not in a child — because the root is the only node Reactor
    // re-renders unconditionally. Passed down as a prop, its changing value
    // makes child components compare unequal across renders, so reconciliation
    // descends into them instead of pruning the structurally-stable subtree.
    let (refresh, bump) = cx.use_reducer(0_u64);
    let timer = cx.use_ref::<Option<DispatcherTimer>>(None);
    {
        let slot = timer.clone();
        let bump = bump.clone();
        cx.use_effect((), move || {
            let t = DispatcherTimer::new(Duration::from_secs(2), move || bump.call(|n| n + 1))
                .expect("DispatcherQueue.CreateTimer");
            slot.set(Some(t));
        });
    }

    // Root-owned /health poll — the single source of truth for the auth gate,
    // the status bar, and the sign-in screen.
    let health = cx.use_resource(|_gen: u64| api().health().map_err(service_err), refresh);

    // Best-effort update check (M12 notify-of-update). Runs once (hook — before any early
    // return); a newer published release surfaces a pill in the top bar. Never blocks or
    // nags on failure (offline / rate-limited / no releases yet).
    let update = cx.use_resource(
        |_: ()| Ok::<Option<String>, String>(update_check::check_for_update()),
        (),
    );
    // M12 increment 2 — the human-triggered self-update. Firing downloads this arch's release
    // bundle and spawns the detached swap helper; on success the pill asks the user to restart
    // (the helper waits for THIS process to exit before replacing the files, then relaunches).
    // A hook, so it must be declared before any early return, next to the update resource.
    let (update_apply, do_update_apply) = cx.use_mutation::<()>();

    // Remember the last successful snapshot: Resource::Error drops its data,
    // so without this a transient poll failure would bounce a signed-in user
    // back to the sign-in screen.
    let (remembered, set_remembered) = cx.use_state::<Option<SyncStatus>>(None);
    {
        let fresh = health.data().cloned();
        let set_remembered = set_remembered.clone();
        cx.use_effect(fresh.clone(), move || {
            if fresh.is_some() {
                set_remembered.call(fresh);
            }
        });
    }
    let status = health.data().cloned().or(remembered);
    let service_error = health.error().map(str::to_string);
    let signed_in = status
        .as_ref()
        .is_some_and(|s| s.auth_state == AuthState::SignedIn);

    // Dispatch the workspace from the feature registry. Each workspace is its own
    // `component(..)`, and we `.with_key(tab)` so switching between two same-fn
    // features (e.g. two `list_workspace` screens) remounts and resets hook state
    // rather than re-rendering in place — the reconciler only auto-remounts on a
    // differing component *type*, which these share.
    let inner: Element = match features::find(&tab).map(|f| f.kind) {
        Some(Screen::Builtin(Builtin::Drift)) => component(drift_workspace, ()),
        Some(Screen::Builtin(Builtin::Search)) => component(search_workspace, ()),
        Some(Screen::Builtin(Builtin::Logs)) => component(logs_workspace, ()),
        Some(Screen::Builtin(Builtin::Timeline)) => component(timeline_workspace, ()),
        Some(Screen::Builtin(Builtin::PendingChanges)) => component(pending_workspace, ()),
        Some(Screen::List(path, writable)) => component(list_workspace, (path, writable)),
        Some(Screen::Diag(kind)) => match kind {
            "dsregcmd" => component(dsregcmd_workspace, ()),
            "error-db" => component(errordb_workspace, ()),
            "intune-diag" => component(intune_diag_workspace, ()),
            "deployment" => component(deployment_workspace, ()),
            "dns-dhcp" => component(dns_workspace, ()),
            "registry" => component(registry_workspace, ()),
            "event-log" => component(eventlog_workspace, ()),
            "sysmon" => component(sysmon_workspace, ()),
            "secureboot" => component(secureboot_workspace, ()),
            "timeline" => component(correlation_workspace, ()),
            "collector" => component(collector_workspace, ()),
            _ => component(stub_workspace, tab.clone()),
        },
        Some(Screen::Tiles(path)) => component(tiles_workspace, path),
        Some(Screen::Bulk(kind)) => match kind {
            "export" => component(export_workspace, ()),
            "import" => component(import_workspace, ()),
            "ca-pptx" => component(ca_pptx_workspace, ()),
            _ => component(stub_workspace, tab.clone()),
        },
        Some(Screen::Posture) => component(posture_workspace, ()),
        Some(Screen::Maester(filter)) => component(maester_workspace, filter),
        Some(Screen::Ca) => component(ca_workspace, ()),
        Some(Screen::Devices(_)) => component(devices_workspace, ()),
        Some(Screen::Groups) => component(groups_workspace, ()),
        Some(Screen::AssignmentExplorer) => component(assignment_explorer_workspace, ()),
        Some(Screen::AppAssignments) => component(app_assignments_workspace, ()),
        Some(Screen::Cache) => component(cache_workspace, ()),
        Some(Screen::GitOps) => component(gitops_workspace, ()),
        Some(Screen::Simulate) => component(simulator_workspace, ()),
        Some(Screen::Twin) => component(twin_workspace, ()),
        Some(Screen::Autonomy) => component(autonomy_workspace, ()),
        Some(Screen::PostureTrend) => component(posture_trend_workspace, ()),
        Some(Screen::Fleet) => component(fleet_workspace, ()),
        Some(Screen::Ecosystem) => component(ecosystem_workspace, ()),
        Some(Screen::Compare) => component(compare_workspace, ()),
        Some(Screen::BulkAssign) => component(bulk_assign_workspace, ()),
        Some(Screen::DetectionRemediation) => component(detection_workspace, ()),
        _ => component(stub_workspace, tab.clone()),
    };
    let workspace = inner.with_key(tab.clone());

    // One collapsible nav parent per section; children are its features. The
    // parent's tag defaults to its first child so clicking a section header both
    // expands it and lands on the first item (WinUI parents are selectable and the
    // reactor has no `selects-on-invoked` yet).
    let nav_items: Vec<NavViewItem> = features::Section::all()
        .iter()
        .map(|sec| {
            let children: Vec<&features::Feature> = features::FEATURES
                .iter()
                .filter(|f| f.section == *sec)
                .collect();
            let mut parent =
                NavViewItem::new(format!("{}  {}", sec.motif(), sec.title())).icon(sec.icon());
            if let Some(first) = children.first() {
                parent = parent.tag(first.tag);
            }
            for f in children {
                parent = parent.child(NavViewItem::new(f.title).tag(f.tag));
            }
            parent
        })
        .collect();

    let sidebar_btn = button(if pane_open {
        "Hide sidebar"
    } else {
        "Show sidebar"
    })
    .icon(if pane_open {
        Symbol::Back
    } else {
        Symbol::Forward
    })
    .on_click({
        let set_pane_open = set_pane_open.clone();
        move || set_pane_open.call(!pane_open)
    });
    let sidecar_status: Element = if health.data().is_some() {
        caption("Sidecar: running").opacity(0.7).into()
    } else if service_error.is_some() {
        caption("Sidecar: offline").opacity(0.7).into()
    } else {
        caption("Sidecar: checking...").opacity(0.7).into()
    };
    // Signed out / mid-flow / failed / still connecting → FULL-WINDOW takeover:
    // the Split Hero login owns the whole window — no nav rail, shell bar, or
    // header — until /health reports SignedIn. Returning it as the render root
    // (rather than boxing it in the NavigationView body) is also what gives the
    // two-column hero the full window width; nested beside the nav pane it was
    // clipped to the narrower content area.
    if !signed_in {
        return component(
            login_workspace,
            LoginProps {
                status: status.clone(),
                service_error: service_error.clone(),
                refresh,
            },
        )
        .with_key("signin")
        .into();
    }

    // Signed in: a single top bar owns the very top of the window — the page
    // title + sidebar toggle + sidecar status on the left, and the sign-in/sync
    // controls pinned to the upper-right. The NavigationView's own string header
    // is dropped (we render the title here) so nothing sits above this bar.
    let title = body_strong(header_for(&tab))
        .font_size(22.0)
        .font_family(theme::FONT_DISPLAY)
        .foreground(theme::TEXT);
    let update_pill: Element = update
        .data()
        .cloned()
        .flatten()
        .map(|v| {
            // The notify pill is now actionable: click to download + stage the update. State
            // reflects the apply mutation — downloading, staged (restart to finish), or a
            // recoverable failure (re-enabled so the user can retry). Nothing is applied until
            // the app exits, so the click itself is non-destructive.
            let installing = update_apply.is_loading();
            let staged = update_apply.data().is_some();
            let failed = update_apply.error().is_some();
            let label = if installing {
                "Downloading update…".to_string()
            } else if staged {
                "Update ready — restart to finish".to_string()
            } else if failed {
                format!("Update failed — retry {v}")
            } else {
                format!("Update available: {v} — Install")
            };
            button(label)
                .enabled(!installing && !staged)
                .on_click({
                    let m = do_update_apply.clone();
                    move || m.fire(move || update_check::apply_update())
                })
                .into()
        })
        .unwrap_or(Element::Empty);
    let left_group = hstack((
        Element::from(sidebar_btn),
        Element::from(title),
        sidecar_status,
        update_pill,
    ))
    .spacing(12.0);
    // NOTE: `grid_column` is a no-op on a `component()` (components carry no
    // modifiers), so the status cluster is wrapped in a `border` — which does
    // accept the attached column property — to actually land in column 1 instead
    // of overlapping the left group in column 0.
    let status_cell = border(component(
        status_bar,
        StatusProps {
            status: status.clone(),
            service_error: service_error.clone(),
            refresh,
        },
    ));
    let top_bar: Element = border(
        grid((
            Element::from(left_group).grid_column(0),
            Element::from(status_cell).grid_column(1),
        ))
        .columns([GridLength::Star(1.0), GridLength::Auto])
        .column_spacing(16.0),
    )
    .padding(Thickness::xy(16.0, 10.0))
    .into();

    // Brand-dark content area (neutrals lead; #111 app background).
    let body: Element = border(vstack((top_bar, workspace)))
        .background(theme::BG)
        .into();

    NavigationView::new(nav_items, body)
        .background(theme::BG)
        .selected_tag(tab.clone())
        .pane_open(pane_open)
        .pane_display_mode(NavigationViewPaneDisplayMode::Left)
        .pane_toggle_button_visible(false)
        .settings_visible(false)
        .pane_title("IntuneCommander")
        .on_selection_changed(set_tab)
        .into()
}

fn header_for(tab: &str) -> &'static str {
    features::find(tab).map(|f| f.title).unwrap_or("IntuneCommander")
}

// ─── Status bar (auth + sync) ──────────────────────────────────────────────

/// Props from the root, which owns the /health poll. `refresh` exists so the
/// prop compares unequal every tick — that forced re-render is what keeps this
/// nested component's own mutation state ("Syncing…") rendering, since nested
/// components don't re-render on their own state changes (see assignments.rs).
#[derive(Clone, PartialEq)]
struct StatusProps {
    status: Option<SyncStatus>,
    service_error: Option<String>,
    refresh: u64,
}

fn status_bar(p: &StatusProps, cx: &mut RenderCx) -> Element {
    let _tick = p.refresh; // participates via PartialEq; see StatusProps doc
    let (signout, do_signout) = cx.use_mutation::<()>();
    let (sync, do_sync) = cx.use_mutation::<()>();

    // Live transport error trumps a remembered snapshot — if the sidecar dies
    // while signed in, say so rather than showing stale state as current.
    let status: Element = if let Some(e) = &p.service_error {
        caption(format!(
            "service unreachable — is the sidecar running? ({e})"
        ))
        .opacity(0.6)
        .into()
    } else if let Some(s) = &p.status {
        status_text(s)
    } else {
        caption("connecting to service…").opacity(0.6).into()
    };

    // Sign-in lives on the sign-in screen now; the bar only renders signed in,
    // where the useful action is the inverse.
    let signout_btn = button(if signout.is_loading() {
        "Signing out…"
    } else {
        "Sign out"
    })
    .enabled(!signout.is_loading())
    .on_click({
        let m = do_signout.clone();
        move || m.fire(|| api().sign_out().map_err(service_err))
    });

    let sync_btn = button(if sync.is_loading() {
        "Syncing…"
    } else {
        "Sync now"
    })
    .accent()
    .enabled(!sync.is_loading())
    .on_click({
        let m = do_sync.clone();
        move || m.fire(|| api().sync().map_err(|e| e.to_string()))
    });

    // Compact, content-sized cluster — it sits in the top bar's right column, so
    // it must size to its content (no Star) to stay pinned to the upper-right.
    hstack((
        status,
        Element::from(signout_btn),
        Element::from(sync_btn),
    ))
    .spacing(8.0)
    .into()
}

fn status_text(s: &SyncStatus) -> Element {
    let tenant = s
        .profile_name
        .clone()
        .or_else(|| s.tenant_id.clone())
        .unwrap_or_else(|| "no active profile".into());
    let last = s.last_sync_utc.clone().unwrap_or_else(|| "never".into());

    // M12.1 — the blob cache is warm once the sidecar finishes a PrefetchAllToCache
    // (on sign-in or /sync). Reuses the /health poll; no new endpoint, no per-row field.
    let warm_label: Element = match s.last_warmed_utc.as_deref() {
        Some(w) if !w.is_empty() => caption("· cache warm").opacity(0.45).into(),
        _ => Element::Empty,
    };

    let mut lines: Vec<Element> = vec![
        hstack((
            body_strong(auth_label(s.auth_state)),
            body(tenant).opacity(0.8),
            caption(format!("last sync: {last}")).opacity(0.55),
            warm_label,
        ))
        .spacing(10.0)
        .into(),
    ];

    // Device-code prompt (only non-empty for interactive/device-code profiles).
    if let Some(dc) = &s.device_code {
        lines.push(
            caption(format!(
                "Sign in at {} and enter code {}",
                dc.verification_uri, dc.user_code
            ))
            .opacity(0.85)
            .into(),
        );
    }
    if let Some(err) = &s.error {
        lines.push(caption(format!("error: {err}")).opacity(0.7).into());
    }

    vstack(lines).spacing(2.0).into()
}

fn auth_label(state: AuthState) -> &'static str {
    match state {
        AuthState::SignedOut => "Signed out",
        AuthState::AwaitingDeviceCode => "Awaiting device code",
        AuthState::AwaitingInteractive => "Awaiting browser sign-in",
        AuthState::SigningIn => "Signing in…",
        AuthState::SignedIn => "Signed in",
        AuthState::Failed => "Auth failed",
    }
}

// ─── Timeline ──────────────────────────────────────────────────────────────

fn timeline_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (draft, set_draft) = cx.use_state(String::new());
    let (applied, set_applied) = cx.use_state(String::new());

    let events = cx.use_resource(
        |q: String| {
            let filter = if q.is_empty() { None } else { Some(q.as_str()) };
            api().audit(filter).map_err(service_err)
        },
        applied.clone(),
    );

    let list_height = list_height(cx);
    let list: Element = events
        .view(move |evts: &Vec<AuditEvent>| -> Element {
            if evts.is_empty() {
                return body("No audit events for this filter.").opacity(0.6).into();
            }
            list_view(evts.clone(), |e: &AuditEvent, _| audit_row(e))
                .with_key_selector(|e: &AuditEvent| e.id.clone())
                .height(list_height - 48.0)
                .into()
        })
        .error(|e| error_box(e))
        .into();

    fluid_fill(
        12.0,
        vec![search_box(
            draft,
            "Filter audit events (actor, action, object)…",
            set_draft,
            set_applied,
        )],
        list,
    )
    .margin(Thickness::uniform(16.0))
    .into()
}

fn audit_row(e: &AuditEvent) -> Element {
    let actor = e.actor.clone().unwrap_or_else(|| "—".into());
    let name = e.object_name.clone().unwrap_or_else(|| e.object_id.clone());
    vstack((
        hstack((
            caption(e.timestamp.clone()).opacity(0.55),
            body_strong(e.action.clone()),
        ))
        .spacing(8.0),
        caption(format!("{} · {} · {}", e.object_type, name, actor)).opacity(0.8),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

// ─── Search ────────────────────────────────────────────────────────────────

fn search_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (draft, set_draft) = cx.use_state(String::new());
    let (applied, set_applied) = cx.use_state(String::new());

    // Empty query short-circuits to an empty result set — /search 400s on a
    // blank `q`, and there's nothing to show before the user types anyway.
    let results = cx.use_resource(
        |q: String| {
            if q.is_empty() {
                return Ok(Vec::<SearchResult>::new());
            }
            api().search(&q).map_err(service_err)
        },
        applied.clone(),
    );

    let list_height = list_height(cx);
    let list: Element = results
        .view(move |hits: &Vec<SearchResult>| -> Element {
            if hits.is_empty() {
                return body(
                    "Type a query and press Enter to search audit events and config snapshots.",
                )
                .opacity(0.6)
                .wrap()
                .into();
            }
            list_view(hits.clone(), |r: &SearchResult, _| search_row(r))
                .with_key_selector(|r: &SearchResult| format!("{:?}:{}", r.kind, r.id))
                .height(list_height - 48.0)
                .into()
        })
        .error(|e| error_box(e))
        .into();

    fluid_fill(
        12.0,
        vec![search_box(draft, "Search everything…", set_draft, set_applied)],
        list,
    )
    .margin(Thickness::uniform(16.0))
    .into()
}

fn search_row(r: &SearchResult) -> Element {
    let score = r.score.map(|s| format!("score {s:.2}")).unwrap_or_default();
    vstack((
        hstack((
            body_strong(format!("{:?}", r.kind)),
            caption(score).opacity(0.5),
        ))
        .spacing(8.0),
        body(r.summary.clone()).opacity(0.85).wrap(),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

// ─── Drift ─────────────────────────────────────────────────────────────────

fn drift_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (filter, set_filter) = cx.use_state(String::new());
    let (selected, set_selected) = cx.use_state(String::new());

    // Master list: objects with snapshot history (newest-captured first).
    let objects = cx.use_resource(|_: ()| api().objects().map_err(service_err), ());

    let filter_lc = filter.to_lowercase();

    // Effective selection: if the picked object isn't in the currently-filtered
    // set (e.g. the user typed a filter that hides it), treat the selection as
    // empty so the detail pane reverts to the placeholder instead of stranding
    // drift for an object that's no longer visible. Clearing the filter restores
    // it (we never mutate `selected` itself, so the highlight comes back too).
    let effective_selected = match objects.data() {
        Some(objs) => {
            if !selected.is_empty()
                && objs
                    .iter()
                    .any(|o| o.object_id == selected && object_matches(o, &filter_lc))
            {
                selected.clone()
            } else {
                String::new()
            }
        }
        None => selected.clone(),
    };

    // Detail: drift for the effective selection. Empty selection short-circuits so
    // we never hit /drift with a blank id.
    let drift = cx.use_resource(
        |object_id: String| {
            if object_id.is_empty() {
                return Ok(DriftRecord {
                    object_id: String::new(),
                    base_snapshot_id: None,
                    head_snapshot_id: None,
                    changes: Vec::new(),
                });
            }
            api().drift(&object_id).map_err(service_err)
        },
        effective_selected.clone(),
    );

    let list_height = list_height(cx);
    let selected_id = selected.clone();
    let filter_lc_view = filter_lc.clone();

    let list: Element = objects
        .view(move |objs: &Vec<DriftObject>| -> Element {
            let matches: Vec<DriftObject> = objs
                .iter()
                .filter(|&o| object_matches(o, &filter_lc_view))
                .cloned()
                .collect();

            if matches.is_empty() {
                return body("No diffable objects.").opacity(0.6).into();
            }

            let sel_index = matches
                .iter()
                .position(|o| o.object_id == selected_id)
                .map(|i| i as i32)
                .unwrap_or(-1);
            let ids: Vec<String> = matches.iter().map(|o| o.object_id.clone()).collect();
            let set_selected = set_selected.clone();

            list_view(matches, |o: &DriftObject, _| object_row(o))
                .with_key_selector(|o: &DriftObject| o.object_id.clone())
                .selected_index(sel_index)
                .on_selection_changed(move |i| {
                    if let Some(id) = ids.get(i as usize) {
                        set_selected.call(id.clone());
                    }
                })
                .height(list_height - 48.0)
                .into()
        })
        .error(|e| error_box(e))
        .into();

    let left = fluid_fill(
        12.0,
        vec![auto_suggest_box(filter)
            .placeholder_text("Filter objects (name, type, id)…".to_string())
            .on_text_changed(move |t| set_filter.call(t))
            .into()],
        list,
    );

    let detail: Element = drift
        .view(move |d: &DriftRecord| drift_view(d, list_height))
        .error(|e| error_box(e))
        .into();

    grid((left.grid_column(0), detail.grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(1.6)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}

// Case-insensitive match of an object against the filter box text (name, id, or
// type). Shared by the visible list and the effective-selection guard so they
// can never disagree about what "matches".
fn object_matches(o: &DriftObject, filter_lc: &str) -> bool {
    if filter_lc.is_empty() {
        return true;
    }
    let name = o.object_name.as_deref().unwrap_or("");
    name.to_lowercase().contains(filter_lc)
        || o.object_id.to_lowercase().contains(filter_lc)
        || o.object_type.to_lowercase().contains(filter_lc)
}

fn object_row(o: &DriftObject) -> Element {
    let name = o.object_name.clone().unwrap_or_else(|| o.object_id.clone());
    let meta = caption(format!(
        "{} · {} snapshot(s) · {}",
        o.object_type, o.snapshot_count, o.last_captured_utc
    ))
    .opacity(0.8);

    let heading: Element = if o.change_count > 0 {
        hstack((
            body_strong(name),
            caption(format!("{} drift", o.change_count)).opacity(0.85),
        ))
        .spacing(8.0)
        .into()
    } else {
        body_strong(name).into()
    };

    vstack((heading, meta))
        .spacing(2.0)
        .margin(Thickness::xy(8.0, 6.0))
        .into()
}

fn drift_view(d: &DriftRecord, list_height: f64) -> Element {
    if d.object_id.is_empty() {
        return body("Select an object on the left to see its drift.")
            .opacity(0.6)
            .wrap()
            .into();
    }
    if d.changes.is_empty() {
        // Empty changes has three distinct causes — don't claim "matches the
        // previous one" when there is no previous snapshot to compare against.
        let msg = match (&d.base_snapshot_id, &d.head_snapshot_id) {
            (None, Some(_)) => format!(
                "Only one snapshot captured for {} so far — nothing to compare yet.",
                d.object_id
            ),
            (None, None) => format!("No snapshot history for {} yet.", d.object_id),
            _ => format!(
                "No drift for {} — its latest snapshot matches the previous one.",
                d.object_id
            ),
        };
        return body(msg).opacity(0.7).wrap().into();
    }

    let base = d.base_snapshot_id.clone().unwrap_or_else(|| "∅".into());
    let head = d.head_snapshot_id.clone().unwrap_or_else(|| "∅".into());
    let summary = caption(format!(
        "{} change(s)   {} → {}",
        d.changes.len(),
        base,
        head
    ))
    .opacity(0.7);

    let list = list_view(d.changes.clone(), |c: &DriftChange, _| drift_row(c))
        .with_key_selector(|c: &DriftChange| format!("{:?}:{}", c.kind, c.path))
        .height(list_height - 28.0);

    fluid_fill(8.0, vec![summary.into()], list.into()).into()
}

pub(crate) fn drift_row(c: &DriftChange) -> Element {
    let before = c.before.as_ref().map(val_str).unwrap_or_else(|| "∅".into());
    let after = c.after.as_ref().map(val_str).unwrap_or_else(|| "∅".into());
    border(
        vstack((
            hstack((
                body_strong(format!("{:?}", c.kind)),
                caption(c.path.clone()).font_family("Consolas").opacity(0.8),
            ))
            .spacing(8.0),
            caption(format!("- {before}")).font_family("Consolas"),
            caption(format!("+ {after}")).font_family("Consolas"),
        ))
        .spacing(2.0),
    )
    .corner_radius(4.0)
    .padding(Thickness::uniform(8.0))
    .into()
}

// ─── Generic CRUD workspace — list + JSON view/edit/create/delete ────────────

// Drives any management surface by sidecar path. `prop = (path, writable)`. List
// rows come back normalized; the detail/editor reads & writes the full object JSON.
fn list_workspace(prop: &(&'static str, bool), cx: &mut RenderCx) -> Element {
    let (path, writable) = *prop;
    let (filter, set_filter) = cx.use_state(String::new());
    let (plat_filter, set_plat_filter) = cx.use_state(String::new()); // rich-list platform chip
    let (sort, set_sort) = cx.use_state(String::new()); // "" server order · name · name-desc · status
    let (selected, set_selected) = cx.use_state(String::new());
    let (draft, set_draft) = cx.use_state(String::new()); // editor text (new/edit)
    let (mode, set_mode) = cx.use_state(String::new()); // "" view · "new" · "edit"
    let (use_raw, set_use_raw) = cx.use_state(false); // shallow-surface edit: raw JSON vs typed form
    let (refresh, bump) = cx.use_reducer(0_u64);

    let (save, do_save) = cx.use_mutation::<()>();
    let (del, do_del) = cx.use_mutation::<()>();
    // Assignment editor state. The editor is a render helper (not a nested
    // component), so its working set + save mutation live in this — the parent's —
    // hook context; nested `component()` children don't re-render on their own
    // state changes in the reactor. See assignments.rs.
    let (working, set_working) = cx.use_state::<Vec<Assignment>>(Vec::new());
    let (asg_save, do_asg_save) = cx.use_mutation::<()>();

    // When a write completes, refresh the list/detail, close the editor, clear the
    // selection on delete, and reset the mutation so the next write can fire.
    let write_done = save.data().is_some() || del.data().is_some() || asg_save.data().is_some();
    let was_delete = del.data().is_some();
    {
        let bump = bump.clone();
        let set_mode = set_mode.clone();
        let set_selected = set_selected.clone();
        let do_save = do_save.clone();
        let do_del = do_del.clone();
        let do_asg_save = do_asg_save.clone();
        cx.use_effect(write_done, move || {
            if write_done {
                bump.call(|n| n + 1);
                set_mode.call(String::new());
                if was_delete {
                    set_selected.call(String::new());
                }
                do_save.reset();
                do_del.reset();
                do_asg_save.reset();
            }
        });
    }

    let rows = cx.use_resource(
        |key: (String, u64)| {
            let (p, _) = key;
            api().get_list(&p).map_err(service_err)
        },
        (path.to_string(), refresh),
    );
    let detail = cx.use_resource(
        |key: (String, String, u64)| {
            let (p, id, _) = key;
            if id.is_empty() {
                return Ok(String::new());
            }
            api().get_detail(&p, &id).map_err(service_err)
        },
        (path.to_string(), selected.clone(), refresh),
    );
    // Current assignments for the selected item, fetched here (the proven resource
    // context) and handed to the assignment editor as data. Only assignable surfaces
    // with a selection hit the network; everything else short-circuits to empty.
    let assignments = cx.use_resource(
        |key: (String, String, bool, u64)| {
            let (p, id, assignable, _) = key;
            if !assignable || id.is_empty() {
                return Ok(Vec::<Assignment>::new());
            }
            api().get_assignments(&p, &id).map_err(service_err)
        },
        (
            path.to_string(),
            selected.clone(),
            shows_assignments(path),
            refresh,
        ),
    );

    // Seed the assignment editor's working set from the freshly-loaded current
    // assignments — re-seeding whenever the selection changes or the set reloads
    // (e.g. after a save bumps `refresh`), so each item's editor starts from its
    // own current state. Gated on `!is_loading` so a selection change doesn't seed
    // from the previous item's still-cached (Reloading) data.
    {
        let set_working = set_working.clone();
        let loaded = assignments.data().cloned();
        let is_loading = assignments.is_loading();
        cx.use_effect((selected.clone(), is_loading), move || {
            if !is_loading {
                if let Some(rows) = loaded {
                    set_working.call(rows);
                }
            }
        });
    }

    // ── M6 safe-write rails: diff-preview/confirm + version history (restore) ──
    // Two-step Save: "Review changes" computes the change set (server `JsonDrift`)
    // into the confirm panel; only "Confirm save" writes. Editing the draft drops
    // back out of review (an effect also resets it on any mode change, so a stale
    // diff can never be confirmed). `history` powers the restore/undo picker.
    let (review, set_review) = cx.use_state(false);
    {
        let set_review = set_review.clone();
        cx.use_effect(mode.clone(), move || set_review.call(false));
    }
    let before_for_diff = if mode.as_str() == "new" {
        "{}".to_string()
    } else {
        detail.data().cloned().unwrap_or_default()
    };
    let preview = cx.use_resource(
        |key: (String, String, bool)| {
            let (before, after, want) = key;
            if !want {
                return Ok(Vec::<DriftChange>::new());
            }
            api().preview_diff(&before, &after).map_err(service_err)
        },
        (before_for_diff.clone(), draft.clone(), review),
    );
    let history = cx.use_resource(
        |key: (String, bool, u64)| {
            let (id, want, _) = key;
            if !want || id.is_empty() {
                return Ok(Vec::<SnapshotSummary>::new());
            }
            api().list_snapshots(&id).map_err(service_err)
        },
        (selected.clone(), mode.as_str() == "history", refresh),
    );
    let selected_name: Option<String> = rows
        .data()
        .and_then(|all| all.iter().find(|r| r.id == selected).map(|r| r.title.clone()));

    // Settings Catalog's deep settings[] — fetched apart from the policy metadata
    // (only for that surface); handed to render_config for the collapsible tree.
    let settings = cx.use_resource(
        |key: (String, String, u64)| {
            let (p, id, _) = key;
            if p != "/settings-catalog" || id.is_empty() {
                return Ok(String::new());
            }
            api().get_settings(&id).map_err(service_err)
        },
        (path.to_string(), selected.clone(), refresh),
    );
    let settings_data = settings.data().cloned();

    // Conditional Access → a readable, GUID-resolved summary (only for that surface).
    let ca = cx.use_resource(
        |key: (String, String, u64)| {
            let (p, id, _) = key;
            if p != "/conditional-access" || id.is_empty() {
                return Ok(None);
            }
            api().ca_summary(&id).map(Some).map_err(service_err)
        },
        (path.to_string(), selected.clone(), refresh),
    );
    let ca_data = ca.data().cloned().flatten();

    // Config detail view toggles — owned here (the parent's hook context) since
    // `render_config` is a plain helper. Reset on selection change so each item
    // opens collapsed.
    let (show_system, set_show_system) = cx.use_state(false);
    let (show_raw, set_show_raw) = cx.use_state(false);
    {
        let set_show_system = set_show_system.clone();
        let set_show_raw = set_show_raw.clone();
        cx.use_effect(selected.clone(), move || {
            set_show_system.call(false);
            set_show_raw.call(false);
        });
    }

    let filter_lc = filter.to_lowercase();
    let selected_id = selected.clone();

    // Rich-list facets (Wave 2): a total count + the distinct platforms present in the
    // loaded rows. Surfaces that don't project `platform` (most of the long tail) keep
    // the simple stacked rows; platform-faceted ones get columns + filter chips.
    let total_rows = rows.data().map(|v| v.len()).unwrap_or(0);
    let platforms: Vec<String> = {
        let mut set: Vec<String> = Vec::new();
        if let Some(v) = rows.data() {
            for r in v {
                if let Some(p) = r.platform.as_ref().filter(|p| !p.is_empty()) {
                    if !set.contains(p) {
                        set.push(p.clone());
                    }
                }
            }
        }
        set.sort();
        set
    };
    let has_platform = !platforms.is_empty();
    let list_height = list_height(cx);
    // Extra rows vs the base list: sort/copy + export (+~80px total).
    let list_chrome = if has_platform { 230.0 } else { 176.0 };

    // The filtered + sorted rows, computed once so the count, the Copy action and the
    // rendered list all agree.
    let visible: Vec<ListItem> = rows
        .data()
        .map(|all| filter_sort_rows(all, &filter_lc, &plat_filter, &sort))
        .unwrap_or_default();
    let filtered_count = visible.len();

    // Key the list on the active filter/sort so it REMOUNTS (and thus re-renders) when a
    // platform chip, the text filter, or the sort changes — the reactor's in-place reconcile
    // otherwise repaints the chrome but leaves the deep list subtree stale.
    let list_key = format!("{}|{}|{}", filter.to_lowercase(), plat_filter.clone(), sort.clone());

    // LEFT — stats + chips + filter + sort/copy + header + list
    let set_sel_list = set_selected.clone();
    let set_mode_list = set_mode.clone();
    let list_el: Element = rows
        .view({
            let visible = visible.clone();
            move |_all: &Vec<ListItem>| -> Element {
                if visible.is_empty() {
                    return body("No items. Sign in (top right) if this needs Graph data.")
                        .opacity(0.6)
                        .wrap()
                        .into();
                }
                let sel_index = visible
                    .iter()
                    .position(|r| r.id == selected_id)
                    .map(|i| i as i32)
                    .unwrap_or(-1);
                let ids: Vec<String> = visible.iter().map(|r| r.id.clone()).collect();
                let set_sel = set_sel_list.clone();
                let set_md = set_mode_list.clone();
                list_view(
                    visible.clone(),
                    move |r: &ListItem, _| if has_platform { rich_row(r) } else { row_view(r) },
                )
                .with_key_selector(|r: &ListItem| r.id.clone())
                .selected_index(sel_index)
                .on_selection_changed(move |i| {
                    if let Some(id) = ids.get(i as usize) {
                        set_sel.call(id.clone());
                        set_md.call(String::new());
                    }
                })
                .height(list_height - list_chrome)
                .into()
            }
        })
        .loading(caption("loading…").opacity(0.6))
        .error(|e| error_box(e))
        .into();
    let list_el = list_el.with_key(list_key);

    let new_btn: Element = if writable {
        let set_draft = set_draft.clone();
        let set_mode = set_mode.clone();
        let set_selected = set_selected.clone();
        Element::from(button("+ New").on_click(move || {
            set_selected.call(String::new());
            set_draft.call("{\n  \"@odata.type\": \"\"\n}".to_string());
            set_mode.call("new".to_string());
        }))
    } else {
        Element::Empty
    };

    let stats_header = Element::from(
        caption(if filtered_count == total_rows {
            format!("{total_rows} items")
        } else {
            format!("{filtered_count} of {total_rows} items")
        })
        .foreground(theme::TEXT_3)
        .font_family(theme::FONT_UI),
    );
    // Platform filter chips (only when the surface resolves platforms).
    let chip_row: Element = if has_platform {
        let mut chips: Vec<Element> = Vec::new();
        {
            let active = plat_filter.is_empty();
            let set_pf = set_plat_filter.clone();
            let mut b = button("All").on_click(move || set_pf.call(String::new()));
            if active {
                b = b.accent();
            }
            chips.push(Element::from(b));
        }
        for p in &platforms {
            let active = plat_filter.as_str() == p.as_str();
            let set_pf = set_plat_filter.clone();
            let pv = p.clone();
            let mut b = button(p.clone()).on_click(move || set_pf.call(pv.clone()));
            if active {
                b = b.accent();
            }
            chips.push(Element::from(b));
        }
        Element::from(hstack(chips).spacing(8.0))
    } else {
        Element::Empty
    };
    let header_row: Element = if has_platform { list_header_row() } else { Element::Empty };

    // Sort picker + Copy (the current filtered/sorted view as TSV to the clipboard).
    let sort_row: Element = {
        let sort_keys = ["", "name", "name-desc", "status"];
        let sort_idx = sort_keys.iter().position(|k| *k == sort.as_str()).unwrap_or(0) as i32;
        let sort_combo = ComboBox::new(vec![
            "Sort: default".to_string(),
            "Name ↑".to_string(),
            "Name ↓".to_string(),
            "Status".to_string(),
        ])
        .selected_index(sort_idx)
        .on_selection_changed({
            let set_sort = set_sort.clone();
            move |i: i32| {
                let k = ["", "name", "name-desc", "status"].get(i as usize).copied().unwrap_or("");
                set_sort.call(k.to_string());
            }
        });
        let copy_btn = {
            let vis = visible.clone();
            button("Copy").enabled(!vis.is_empty()).on_click(move || {
                let text = vis
                    .iter()
                    .map(|r| match &r.badge {
                        Some(b) => format!("{}\t{}\t{}", r.title, r.subtitle, b),
                        None => format!("{}\t{}", r.title, r.subtitle),
                    })
                    .collect::<Vec<_>>()
                    .join("\n");
                let _ = clip_text(&text);
            })
        };
        hstack((Element::from(sort_combo), Element::from(copy_btn))).spacing(8.0).into()
    };

    // Export the current filtered/sorted view as an HTML / CSV / Markdown report
    // (native save dialog, then open) — the same rows the Copy button copies.
    let export_row: Element = {
        let mk = |label: &'static str, ext: &'static str| -> Element {
            let vis = visible.clone();
            let title = path.to_string();
            Element::from(button(label).enabled(!vis.is_empty()).on_click(move || {
                let default = format!("{title}.{ext}");
                let flabel = match ext {
                    "html" => "HTML",
                    "csv" => "CSV",
                    _ => "Markdown",
                };
                if let Some(p) = crate::dialogs::pick_save_file(
                    "Save report",
                    &default,
                    &[(flabel, &[ext]), ("All files", &["*"])],
                ) {
                    let content = match ext {
                        "html" => crate::reports::to_html(&title, &vis),
                        "csv" => crate::reports::to_csv(&vis),
                        _ => crate::reports::to_markdown(&title, &vis),
                    };
                    if std::fs::write(&p, content).is_ok() {
                        let _ = crate::dialogs::open_with_default(&p);
                    }
                }
            }))
        };
        hstack((
            Element::from(caption("Export").foreground(theme::TEXT_4).font_family(theme::FONT_UI)),
            mk("HTML", "html"),
            mk("CSV", "csv"),
            mk("Markdown", "md"),
        ))
        .spacing(6.0)
        .into()
    };

    let left = fluid_fill(
        10.0,
        vec![
            stats_header,
            chip_row,
            Element::from(
                hstack((
                    Element::from(
                        auto_suggest_box(filter.clone())
                            .placeholder_text("Filter…".to_string())
                            .on_text_changed(move |t| set_filter.call(t)),
                    ),
                    new_btn,
                ))
                .spacing(8.0),
            ),
            sort_row,
            export_row,
            header_row,
        ],
        list_el,
    );

    // RIGHT — editor (new/edit) or read-only JSON view with actions
    let writing = save.is_loading();
    let right: Element = if mode.as_str() == "new" || mode.as_str() == "edit" {
        let is_new = mode.as_str() == "new";
        let header = if is_new {
            "New item".to_string()
        } else {
            format!("Editing {selected}")
        };
        // Typed quick-edit form for shallow WRITABLE surfaces (M7) — edits the same `draft`,
        // so the preview-diff → confirm → PATCH pipeline below is unchanged. `new` items and
        // the Raw-JSON toggle fall back to the JSON editor.
        let has_typed = !is_new && crate::config_view::has_typed_edit_form(path);
        let typed = if has_typed && !use_raw {
            crate::config_view::typed_edit_form(&draft, path, &set_draft, &set_review)
        } else {
            None
        };
        let form_valid = typed.as_ref().map(|(_, v)| *v).unwrap_or(true);
        let editor: Element = match typed {
            Some((form, _)) => form,
            None => text_box(draft.clone())
                .multiline()
                .height((list_height - 140.0).max(180.0))
                .on_text_changed({
                    let set_draft = set_draft.clone();
                    let set_review = set_review.clone();
                    // Editing invalidates a pending review so a stale diff can't be confirmed.
                    move |t| {
                        set_review.call(false);
                        set_draft.call(t);
                    }
                })
                .into(),
        };
        let controls: Element = if review {
            // Confirm step — show the exact change set this save will apply, using
            // the same drift engine the time-machine uses.
            let diff_box: Element = preview
                .view(|changes: &Vec<DriftChange>| -> Element {
                    if changes.is_empty() {
                        return caption("No changes — the edited JSON matches the current object.")
                            .opacity(0.7)
                            .wrap()
                            .into();
                    }
                    let summary =
                        caption(format!("{} change(s) will be applied:", changes.len()))
                            .opacity(0.8);
                    let list = list_view(changes.clone(), |c: &DriftChange, _| drift_row(c))
                        .with_key_selector(|c: &DriftChange| format!("{:?}:{}", c.kind, c.path))
                        .height(220.0);
                    vstack((summary, list)).spacing(6.0).into()
                })
                .loading(caption("computing changes…").opacity(0.6))
                .error(|e| error_box(e))
                .into();
            let confirm_btn = button(if writing { "Saving…" } else { "Confirm save" })
                .accent()
                .enabled(!writing)
                .on_click({
                    let do_save = do_save.clone();
                    let id = selected.clone();
                    let body = draft.clone();
                    let before = before_for_diff.clone();
                    let name = selected_name.clone();
                    move || {
                        let body = body.clone();
                        let id = id.clone();
                        let before = before.clone();
                        let name = name.clone();
                        if is_new {
                            do_save.fire(move || api().create_item(path, body).map_err(service_err));
                        } else {
                            do_save.fire(move || {
                                // snapshot-on-write: bracket the PATCH with pre/post
                                // snapshots so the edit is itself restorable.
                                let _ = api().capture_snapshot(path, &id, name.as_deref(), &before);
                                api().update_item(path, &id, body.clone()).map_err(service_err)?;
                                let _ = api().capture_snapshot(path, &id, name.as_deref(), &body);
                                Ok(())
                            });
                        }
                    }
                });
            let back_btn = button("Back to editor").enabled(!writing).on_click({
                let set_review = set_review.clone();
                move || set_review.call(false)
            });
            vstack((
                diff_box,
                hstack((Element::from(confirm_btn), Element::from(back_btn))).spacing(8.0),
            ))
            .spacing(8.0)
            .into()
        } else {
            let review_btn = button("Review changes").accent().enabled(form_valid).on_click({
                let set_review = set_review.clone();
                move || set_review.call(true)
            });
            let cancel_btn = button("Cancel").on_click({
                let set_mode = set_mode.clone();
                move || set_mode.call(String::new())
            });
            let raw_toggle: Element = if has_typed {
                button(if use_raw { "Typed form" } else { "Raw JSON" })
                    .on_click({
                        let set_use_raw = set_use_raw.clone();
                        move || set_use_raw.call(!use_raw)
                    })
                    .into()
            } else {
                Element::Empty
            };
            hstack((Element::from(review_btn), Element::from(cancel_btn), raw_toggle))
                .spacing(8.0)
                .into()
        };
        let err: Element = save
            .error()
            .map(|e| {
                caption(format!("save failed: {e}"))
                    .opacity(0.85)
                    .wrap()
                    .into()
            })
            .unwrap_or(Element::Empty);
        let editor_box = border(Element::from(editor))
            .corner_radius(4.0)
            .padding(Thickness::uniform(4.0));
        // The multiline editor takes the Star row so it grows with the window;
        // header/controls (top) and any save error (bottom) stay content-sized.
        grid((
            Element::from(body_strong(header)).grid_row(0),
            controls.grid_row(1),
            Element::from(editor_box).grid_row(2),
            err.grid_row(3),
        ))
        .rows([
            GridLength::Auto,
            GridLength::Auto,
            GridLength::Star(1.0),
            GridLength::Auto,
        ])
        .row_spacing(8.0)
        .into()
    } else if mode.as_str() == "assign" {
        let back = button("← Back").on_click({
            let set_mode = set_mode.clone();
            move || set_mode.call(String::new())
        });
        let cur = assignments.data().cloned().unwrap_or_default();
        let editor = assignment_editor(
            path,
            &selected,
            path == "/apps",
            &cur,
            assignments.is_loading(),
            &working,
            &set_working,
            &asg_save,
            &do_asg_save,
        );
        fluid_fill(
            8.0,
            vec![Element::from(back)],
            scroll_viewer(editor)
                .height((list_height - 60.0).max(160.0))
                .into(),
        )
        .into()
    } else if mode.as_str() == "history" {
        // Version history / restore (undo) — re-apply a past snapshot body via the
        // same PATCH path as an edit, then snapshot the restored state too.
        let back = button("← Back").on_click({
            let set_mode = set_mode.clone();
            move || set_mode.call(String::new())
        });
        let list: Element = history
            .view({
                let do_save = do_save.clone();
                let id = selected.clone();
                let name = selected_name.clone();
                move |snaps: &Vec<SnapshotSummary>| -> Element {
                    if snaps.is_empty() {
                        return body("No saved versions yet. Edit this object to capture its history.")
                            .opacity(0.6)
                            .wrap()
                            .into();
                    }
                    let rows: Vec<Element> = snaps
                        .iter()
                        .enumerate()
                        .map(|(i, s)| {
                            let restore = {
                                let do_save = do_save.clone();
                                let id = id.clone();
                                let name = name.clone();
                                let body = s.body_json.clone();
                                button("Restore").on_click(move || {
                                    let body = body.clone();
                                    let id = id.clone();
                                    let name = name.clone();
                                    do_save.fire(move || {
                                        api().update_item(path, &id, body.clone())
                                            .map_err(service_err)?;
                                        let _ =
                                            api().capture_snapshot(path, &id, name.as_deref(), &body);
                                        Ok(())
                                    });
                                })
                            };
                            let label = if i == 0 {
                                format!("{} · latest", s.captured_utc)
                            } else {
                                s.captured_utc.clone()
                            };
                            border(
                                hstack((caption(label).opacity(0.8), Element::from(restore)))
                                    .spacing(12.0),
                            )
                            .corner_radius(4.0)
                            .padding(Thickness::uniform(8.0))
                            .into()
                        })
                        .collect();
                    vstack(rows).spacing(6.0).into()
                }
            })
            .loading(caption("loading history…").opacity(0.6))
            .error(|e| error_box(e))
            .into();
        fluid_fill(
            8.0,
            vec![
                Element::from(back),
                body_strong("Version history — Restore re-applies a past version").into(),
            ],
            scroll_viewer(list)
                .height((list_height - 60.0).max(160.0))
                .into(),
        )
        .into()
    } else if selected.is_empty() {
        body("Select an item to view it, or + New to create one.")
            .opacity(0.6)
            .wrap()
            .into()
    } else {
        let detail_h = (list_height - 44.0).max(120.0);
        // Resolved current assignments for the read-mode detail panel — only for
        // assignable surfaces (None elsewhere so the panel hides the section). The
        // same resource feeds the editor; here it drives the read-only table.
        let asg_for_view: Option<Vec<Assignment>> = if shows_assignments(path) {
            Some(assignments.data().cloned().unwrap_or_default())
        } else {
            None
        };
        let view: Element = detail
            .view({
                let set_show_system = set_show_system.clone();
                let set_show_raw = set_show_raw.clone();
                let settings_data = settings_data.clone();
                let ca_data = ca_data.clone();
                let asg_for_view = asg_for_view.clone();
                move |json: &String| -> Element {
                    config_view::render_config(
                        path,
                        json,
                        detail_h,
                        show_system,
                        &set_show_system,
                        show_raw,
                        &set_show_raw,
                        settings_data.as_deref(),
                        ca_data.as_ref(),
                        asg_for_view.as_deref(),
                    )
                }
            })
            .loading(caption("loading…").opacity(0.6))
            .error(|e| error_box(e))
            .into();
        // Key the read-mode detail on the selection so it fully remounts per item —
        // the rich panel (status grid + assignment tables) has deep sub-elements that
        // the reactor's in-place reconcile would otherwise leave stale from the
        // previously-selected item (see the nested-component re-render gotcha).
        let view = view.with_key(selected.clone());
        // The Assignments button is independent of `writable`: a surface can be a
        // read-only *object* yet still expose an editable assignment set (e.g.
        // `/apps` — the canonical app-assignment editor). Compute it once and use
        // it in both the writable (Edit/Delete + Assignments) and read-only
        // (read-only surface + Assignments) layouts.
        let assign_btn: Element = if is_assignable(path) {
            let set_mode = set_mode.clone();
            Element::from(
                button("Assignments").on_click(move || set_mode.call("assign".to_string())),
            )
        } else {
            Element::Empty
        };
        let actions: Element = if writable {
            let edit_btn = button("Edit").on_click({
                let set_mode = set_mode.clone();
                let set_draft = set_draft.clone();
                let cur = detail.data().cloned().unwrap_or_default();
                move || {
                    set_draft.call(cur.clone());
                    set_mode.call("edit".to_string());
                }
            });
            let del_btn = button(if del.is_loading() {
                "Deleting…"
            } else {
                "Delete"
            })
            .enabled(!del.is_loading())
            .on_click({
                let do_del = do_del.clone();
                let id = selected.clone();
                move || {
                    let id = id.clone();
                    do_del.fire(move || api().delete_item(path, &id).map_err(service_err));
                }
            });
            // Clone: re-open the editor on a copy of this object with its identity
            // fields stripped, so Save creates a new one (reuses the create path).
            let clone_btn = button("Clone").on_click({
                let set_mode = set_mode.clone();
                let set_draft = set_draft.clone();
                let set_selected = set_selected.clone();
                let cur = detail.data().cloned().unwrap_or_default();
                move || {
                    set_selected.call(String::new());
                    set_draft.call(clone_body(&cur));
                    set_mode.call("new".to_string());
                }
            });
            let history_btn = button("History").on_click({
                let set_mode = set_mode.clone();
                move || set_mode.call("history".to_string())
            });
            hstack((
                Element::from(edit_btn),
                Element::from(clone_btn),
                Element::from(del_btn),
                Element::from(history_btn),
                assign_btn,
            ))
            .spacing(8.0)
            .into()
        } else if is_assignable(path) {
            hstack((caption("read-only surface").opacity(0.5), assign_btn))
                .spacing(12.0)
                .into()
        } else {
            caption("read-only surface").opacity(0.5).into()
        };
        fluid_fill(
            8.0,
            vec![actions],
            border(view)
                .corner_radius(4.0)
                .padding(Thickness::uniform(4.0))
                .into(),
        )
        .into()
    };

    // Key the right pane on (mode, selection) so it fully REMOUNTS whenever the
    // mode flips (read ↔ assign ↔ edit ↔ history) or the selected item changes.
    // Without this the reconciler diffs the old branch against the new in place
    // and leaves stale sub-elements painted on top — e.g. opening Assignments,
    // switching apps, then opening Assignments again overlaid the editor on the
    // previous detail. See the nested-dirty reconcile gotcha.
    let right = right.with_key(format!("{}|{}", mode, selected));
    grid((left.grid_column(0), right.grid_column(1)))
        .columns([GridLength::Star(1.0), GridLength::Star(1.6)])
        .column_spacing(16.0)
        .margin(Thickness::uniform(16.0))
        .into()
}

// Filter (free-text over title/subtitle + platform facet) then sort a row set — shared by
// the "N of M" count, the Copy action, and the rendered list so all three agree. `sort`:
// "" = server order · "name"/"name-desc" = by title · "status" = by badge then title.
fn filter_sort_rows(all: &[ListItem], filter_lc: &str, plat: &str, sort: &str) -> Vec<ListItem> {
    let mut v: Vec<ListItem> = all
        .iter()
        .filter(|r| {
            let text_ok = filter_lc.is_empty()
                || r.title.to_lowercase().contains(filter_lc)
                || r.subtitle.to_lowercase().contains(filter_lc);
            let plat_ok = plat.is_empty() || r.platform.as_deref() == Some(plat);
            text_ok && plat_ok
        })
        .cloned()
        .collect();
    match sort {
        "name" => v.sort_by(|a, b| a.title.to_lowercase().cmp(&b.title.to_lowercase())),
        "name-desc" => v.sort_by(|a, b| b.title.to_lowercase().cmp(&a.title.to_lowercase())),
        "status" => v.sort_by(|a, b| {
            a.badge.as_deref().unwrap_or("").to_lowercase()
                .cmp(&b.badge.as_deref().unwrap_or("").to_lowercase())
                .then_with(|| a.title.to_lowercase().cmp(&b.title.to_lowercase()))
        }),
        _ => {}
    }
    v
}

// Copy text to the Windows clipboard via clip.exe (no clipboard API in the reactor
// bindings; mirrors the helpers in config_view.rs / logview.rs).
fn clip_text(text: &str) -> std::io::Result<()> {
    use std::io::Write;
    use std::process::{Command, Stdio};
    let mut child = Command::new("clip.exe").stdin(Stdio::piped()).spawn()?;
    if let Some(si) = child.stdin.as_mut() {
        si.write_all(text.as_bytes())?;
    }
    child.wait()?;
    Ok(())
}

fn row_view(r: &ListItem) -> Element {
    let title_el = body_strong(r.title.clone())
        .font_family(theme::FONT_UI)
        .foreground(theme::TEXT);
    let heading: Element = if let Some(b) = &r.badge {
        // Badge as a small teal pill (brand: status/active chips lead in teal).
        let pill = border(
            caption(b.clone())
                .foreground(theme::BRAND_BRIGHT)
                .font_family(theme::FONT_UI),
        )
        .background(theme::SURFACE_2)
        .corner_radius(9.0)
        .padding(Thickness::xy(8.0, 1.0));
        hstack((Element::from(title_el), Element::from(pill)))
            .spacing(8.0)
            .into()
    } else {
        Element::from(title_el)
    };
    vstack((
        heading,
        Element::from(caption(r.subtitle.clone()).foreground(theme::TEXT_3)),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

// ── Rich list (Wave 2): multi-column rows for platform-faceted surfaces ──────

// One list row laid out as aligned columns (shared by the header + each row).
fn list_row_grid(name: Element, platform: Element, modified: Element, badge: Element) -> Element {
    grid((
        name.grid_column(0),
        platform.grid_column(1),
        modified.grid_column(2),
        badge.grid_column(3),
    ))
    .columns([
        GridLength::Star(2.0),
        GridLength::Pixel(130.0),
        GridLength::Pixel(110.0),
        GridLength::Pixel(90.0),
    ])
    .column_spacing(8.0)
    .margin(Thickness::xy(8.0, 6.0))
    .into()
}

// A multi-column row: Name (bold) · Platform · Modified · badge pill.
fn rich_row(r: &ListItem) -> Element {
    let badge_el: Element = match &r.badge {
        Some(b) => border(
            caption(b.clone()).foreground(theme::BRAND_BRIGHT).font_family(theme::FONT_UI),
        )
        .background(theme::SURFACE_2)
        .corner_radius(9.0)
        .padding(Thickness::xy(8.0, 1.0))
        .into(),
        None => Element::Empty,
    };
    list_row_grid(
        body_strong(r.title.clone())
            .font_family(theme::FONT_UI)
            .foreground(theme::TEXT)
            .wrap()
            .into(),
        caption(r.platform.clone().unwrap_or_default())
            .foreground(theme::TEXT_3)
            .font_family(theme::FONT_UI)
            .into(),
        caption(r.modified.clone().unwrap_or_default())
            .foreground(theme::TEXT_3)
            .font_family(theme::FONT_MONO)
            .into(),
        badge_el,
    )
}

fn list_header_row() -> Element {
    let h = |t: &str| caption(t.to_string()).foreground(theme::TEXT_4).font_family(theme::FONT_UI);
    list_row_grid(
        h("NAME").into(),
        h("PLATFORM").into(),
        h("MODIFIED").into(),
        Element::Empty,
    )
}

// Strip server-assigned identity/timestamp fields so a fetched object can be
// re-created as a copy (Clone). Best-effort over the top-level object.
fn clone_body(json: &str) -> String {
    let mut v: serde_json::Value =
        serde_json::from_str(json).unwrap_or_else(|_| serde_json::json!({}));
    if let Some(obj) = v.as_object_mut() {
        for k in [
            "id",
            "@odata.etag",
            "@odata.context",
            "createdDateTime",
            "lastModifiedDateTime",
        ] {
            obj.remove(k);
        }
    }
    serde_json::to_string_pretty(&v).unwrap_or_else(|_| json.to_string())
}

// ─── Stub workspace (scaffolded; live screen lands in a later phase) ──────────

fn stub_workspace(tag: &String, _cx: &mut RenderCx) -> Element {
    let title = header_for(tag);
    vstack((
        body_strong(title),
        body(format!("{title} — coming soon.")).opacity(0.75).wrap(),
        caption("Scaffolded in the unified shell; its live screen lands in a later phase.")
            .opacity(0.5)
            .wrap(),
    ))
    .spacing(8.0)
    .margin(Thickness::uniform(24.0))
    .into()
}

// ─── Diagnostics: dsregcmd (device registration) ─────────────────────────────

#[derive(Clone, PartialEq)]
struct DsregView {
    facts: Vec<(String, String)>,
    insights: Vec<(String, String, String)>, // severity, title, summary
}

fn run_dsregcmd() -> std::result::Result<DsregView, String> {
    let out = std::process::Command::new("dsregcmd")
        .arg("/status")
        .output()
        .map_err(|e| format!("couldn't run dsregcmd: {e}"))?;
    let text = String::from_utf8_lossy(&out.stdout);
    let r = dsregcmd::analyze_text(&text)?;
    let d = &r.derived;
    let yn = |b: Option<bool>| match b {
        Some(true) => "yes".to_string(),
        Some(false) => "no".to_string(),
        None => "—".to_string(),
    };
    let facts = vec![
        ("Join type".to_string(), d.join_type_label.clone()),
        ("Phase".to_string(), d.phase_summary.clone()),
        ("MDM enrolled".to_string(), yn(d.mdm_enrolled)),
        ("Azure AD PRT".to_string(), yn(d.azure_ad_prt_present)),
        ("TPM-protected".to_string(), yn(d.tpm_protected)),
        (
            "Cert days remaining".to_string(),
            d.certificate_days_remaining
                .map(|n| n.to_string())
                .unwrap_or_else(|| "—".into()),
        ),
    ];
    let insights = r
        .diagnostics
        .iter()
        .map(|i| {
            (
                format!("{:?}", i.severity),
                i.title.clone(),
                i.summary.clone(),
            )
        })
        .collect();
    Ok(DsregView { facts, insights })
}

fn dsregcmd_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (run, bump) = cx.use_reducer(0_u64);
    let result = cx.use_resource(|_: u64| run_dsregcmd(), run);

    let body_el: Element = result
        .view(move |v: &DsregView| -> Element {
            let facts: Vec<Element> = v
                .facts
                .iter()
                .map(|(k, val)| {
                    hstack((caption(format!("{k}:")).opacity(0.6), body(val.clone())))
                        .spacing(8.0)
                        .into()
                })
                .collect();
            let insights: Vec<Element> = v
                .insights
                .iter()
                .map(|(sev, title, summ)| {
                    border(
                        vstack((
                            hstack((
                                body_strong(title.clone()),
                                caption(sev.clone()).opacity(0.7),
                            ))
                            .spacing(8.0),
                            caption(summ.clone()).opacity(0.85).wrap(),
                        ))
                        .spacing(2.0),
                    )
                    .corner_radius(4.0)
                    .padding(Thickness::uniform(8.0))
                    .into()
                })
                .collect();
            let insight_box: Element = if insights.is_empty() {
                caption("No findings — device registration looks healthy.")
                    .opacity(0.7)
                    .into()
            } else {
                vstack(insights).spacing(8.0).into()
            };
            vstack((
                body_strong("Device registration"),
                vstack(facts).spacing(4.0),
                body_strong("Diagnostics"),
                insight_box,
            ))
            .spacing(12.0)
            .into()
        })
        .loading(caption("running dsregcmd /status…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    fluid_fill(
        12.0,
        vec![Element::from(
            button("Re-run").on_click(move || bump.call(|n| n + 1)),
        )],
        scroll_viewer(body_el)
            .height((list_height(cx) - 60.0).max(160.0))
            .into(),
    )
    .margin(Thickness::uniform(16.0))
    .into()
}

// ─── Diagnostics: Windows/Intune error-code database ─────────────────────────

fn errordb_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (draft, set_draft) = cx.use_state(String::new());
    let (query, set_query) = cx.use_state(String::new());
    let results = cx.use_resource(
        |q: String| -> std::result::Result<Vec<(String, String, String)>, String> {
            if q.trim().is_empty() {
                return Ok(Vec::new());
            }
            Ok(error_db::lookup::search_error_codes(q.trim())
                .into_iter()
                .map(|r| (r.code_hex, r.category, r.description))
                .collect())
        },
        query.clone(),
    );
    let list_height = list_height(cx);
    let list: Element = results
        .view(move |rows: &Vec<(String, String, String)>| -> Element {
            if rows.is_empty() {
                return body(
                    "Enter a Windows/Intune error code (e.g. 0x80070005) or a keyword, then Enter.",
                )
                .opacity(0.6)
                .wrap()
                .into();
            }
            list_view(rows.clone(), |r: &(String, String, String), _| {
                border(
                    vstack((
                        hstack((
                            body(r.0.clone()).font_family("Consolas"),
                            caption(r.1.clone()).opacity(0.7),
                        ))
                        .spacing(8.0),
                        caption(r.2.clone()).opacity(0.85).wrap(),
                    ))
                    .spacing(2.0),
                )
                .corner_radius(4.0)
                .padding(Thickness::uniform(8.0))
            })
            .with_key_selector(|r: &(String, String, String)| r.0.clone())
            .height(list_height - 28.0)
            .into()
        })
        .error(|e| error_box(e))
        .into();

    fluid_fill(
        12.0,
        vec![search_box(draft, "Error code or keyword…", set_draft, set_query)],
        list,
    )
    .margin(Thickness::uniform(16.0))
    .into()
}

// ─── Logs (cmtrace) ──────────────────────────────────────────────────────────

// The Intune Management Extension writes its cmtrace-format logs here; they are
// actively appended on a managed box, which is what makes the live tail useful.
const IME_LOGS_DIR: &str = r"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs";

// Cap how many parsed entries the Intune-diagnostics analyzers below hand to the
// virtualized list (bounds render/reconciliation cost). The full Log Explorer now
// lives in `logview`, parses without this cap, and relies on ListView
// virtualization + tail semantics instead.
const LOG_TAIL_CAP: usize = 800;

pub(crate) fn truncate(s: &str, n: usize) -> String {
    if s.chars().count() <= n {
        s.to_string()
    } else {
        let mut out: String = s.chars().take(n.saturating_sub(1)).collect();
        out.push('…');
        out
    }
}

// ─── Shared bits ───────────────────────────────────────────────────────────

// Controlled search input: `set_draft` tracks every keystroke (so the box keeps
// its text across re-renders), `set_applied` commits on Enter and re-keys the
// owning resource.
pub(crate) fn search_box(
    draft: String,
    placeholder: &str,
    set_draft: SetState<String>,
    set_applied: SetState<String>,
) -> Element {
    auto_suggest_box(draft)
        .placeholder_text(placeholder.to_string())
        .on_text_changed(move |t| set_draft.call(t))
        .on_query_submitted(move |t| set_applied.call(t))
        .into()
}

// Height available to a workspace's content box: window height minus the fixed
// shell chrome above it (nav header + shell bar + status bar + outer margin),
// tracked via `use_inner_size` so the whole workspace reflows on resize. This
// now updates correctly on resize — the reactor fork re-subscribes SizeChanged
// to the live root after the auth gate swaps it, so the value is no longer
// frozen at the sign-in screen's mount size. A WinUI `ListView` only scrolls
// when it is given a *definite* height; a Grid star row does NOT bound it here
// (the constraint never reaches the templated ListView), so the lists/scrollers
// take this as an explicit `.height(..)`, each minus its own in-workspace chrome.
pub(crate) fn list_height(cx: &RenderCx) -> f64 {
    (cx.use_inner_size().height - 170.0).max(220.0)
}

/// Build a vertically-stacked column: each `chrome` element sizes to its own
/// content (an `Auto` row) and the trailing `fill` element takes the last row.
/// The `fill` (a list/scroll) must carry its OWN explicit `.height(..)` to be
/// scrollable — star rows do not bound a templated ListView in this reactor.
pub(crate) fn fluid_fill(spacing: f64, chrome: Vec<Element>, fill: Element) -> Grid {
    let mut rows: Vec<GridLength> = vec![GridLength::Auto; chrome.len()];
    rows.push(GridLength::Star(1.0));
    let last = chrome.len() as i32;
    let mut cells: Vec<Element> = chrome
        .into_iter()
        .enumerate()
        .map(|(i, e)| e.grid_row(i as i32))
        .collect();
    cells.push(fill.grid_row(last));
    grid(cells).rows(rows).row_spacing(spacing)
}

fn val_str(v: &serde_json::Value) -> String {
    match v {
        serde_json::Value::String(s) => s.clone(),
        other => other.to_string(),
    }
}

// Generic error box: renders the message verbatim. Callers own a user-facing
// string — HTTP callers via `service_err` (which classifies unreachable / slow /
// bad-response), and the Logs workspace via its own local file-error strings —
// so this no longer hard-codes a "couldn't reach the service" framing that was
// wrong for local filesystem failures.
pub(crate) fn error_box(e: &str) -> Element {
    border(body(e.to_string()).wrap())
        .corner_radius(4.0)
        .padding(Thickness::uniform(12.0))
        .into()
}

// ═══ Diagnostics workspaces (cmtraceopen-parser) — assembled from subagents ═══

// ─── Intune Diagnostics (cmtraceopen-parser intune::ime_parser) ──────────────
//
// Analyzes the freshest Intune Management Extension (IME) log under
// IME_LOGS_DIR. Reuses the same local file-read + decode path as logs_workspace,
// but parses via the IME-specific analyzer `intune::ime_parser::parse_ime_entries`
// (cmtrace-format aware, with [SubSystem] enrichment) instead of the generic
// parser. Pure local parsing — no sidecar / no api(). A use_reducer "Re-run"
// button re-keys the resource to re-scan + re-parse on demand.

// A lean, comparable projection of the parser's wide `LogEntry`. `LogEntry` does
// not derive `PartialEq` (required by `use_resource`), so we map into this first.
#[derive(Clone, PartialEq)]
struct ImeRow {
    id: u64,
    time: String,
    subsystem: String, // component or [SubSystem] prefix (source_file)
    message: String,
    severity: Severity,
}

// What the fetcher returns: the freshest file's name plus its parsed rows and a
// quick severity tally for the header. All fields are Send + Clone + PartialEq.
#[derive(Clone, PartialEq)]
struct ImeAnalysis {
    file: String,
    rows: Vec<ImeRow>,
    errors: usize,
    warnings: usize,
}

fn intune_diag_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // "Re-run" reducer: bumping the counter re-keys the resource so the dir is
    // re-scanned and the freshest log re-read + re-parsed.
    let (run, rerun) = cx.use_reducer(0_u64);

    let analysis = cx.use_resource(|_: u64| analyze_freshest_ime_log(), run);

    let list_height = list_height(cx);
    let body_el: Element = analysis
        .view(move |a: &ImeAnalysis| -> Element {
            if a.rows.is_empty() {
                return body(
                    "No IME log entries found. This box may not be Intune-managed, \
                     or the freshest log is empty.",
                )
                .opacity(0.6)
                .wrap()
                .into();
            }
            let header = caption(format!(
                "{} · {} entries · {} errors · {} warnings",
                a.file,
                a.rows.len(),
                a.errors,
                a.warnings
            ))
            .opacity(0.7);

            // Newest last in the file; show newest first so recent activity leads.
            let mut rows = a.rows.clone();
            rows.reverse();
            let list: Element = list_view(rows, |r: &ImeRow, _| ime_row(r))
                .with_key_selector(|r: &ImeRow| r.id.to_string())
                .height(list_height - 56.0)
                .into();

            fluid_fill(8.0, vec![header.into()], list).into()
        })
        .loading(caption("scanning IME logs…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    let rerun = rerun.clone();
    let controls = hstack((
        body_strong("Intune Diagnostics".to_string()),
        button("Re-run").on_click(move || rerun.call(|n| n + 1)),
    ))
    .spacing(12.0);

    fluid_fill(12.0, vec![controls.into()], body_el)
        .margin(Thickness::uniform(16.0))
        .into()
}

fn ime_row(r: &ImeRow) -> Element {
    let msg = r.message.replace('\n', " ⏎ ");
    let line = format!("{:<15} {:<22} {}", r.time, truncate(&r.subsystem, 22), msg);
    let tb = body(line).font_family("Consolas").font_size(12.0);
    let colored = match r.severity {
        Severity::Error => tb.foreground(theme::SEV_ERROR),
        Severity::Warning => tb.foreground(theme::SEV_WARN),
        Severity::Info => tb,
    };
    colored.margin(Thickness::xy(8.0, 2.0)).into()
}

// Runs on Reactor's background fetcher thread. Picks the most-recently-modified
// *.log under IME_LOGS_DIR, decodes it, and parses with the IME analyzer.
fn analyze_freshest_ime_log() -> std::result::Result<ImeAnalysis, String> {
    let dir = Path::new(IME_LOGS_DIR);
    let read = match std::fs::read_dir(dir) {
        Ok(r) => r,
        // Missing folder = not Intune-managed; degrade to an empty result rather
        // than an error box (mirrors list_log_sources).
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
            return Ok(ImeAnalysis {
                file: String::new(),
                rows: Vec::new(),
                errors: 0,
                warnings: 0,
            });
        }
        Err(e) => return Err(format!("Couldn't read {}: {e}", dir.display())),
    };

    // Find the freshest .log by modified time.
    let mut freshest: Option<(std::path::PathBuf, u64)> = None;
    for entry in read.flatten() {
        let path = entry.path();
        let is_log = path
            .extension()
            .and_then(|x| x.to_str())
            .is_some_and(|x| x.eq_ignore_ascii_case("log"));
        if !is_log {
            continue;
        }
        let modified = entry
            .metadata()
            .ok()
            .and_then(|m| m.modified().ok())
            .and_then(|m| m.duration_since(SystemTime::UNIX_EPOCH).ok())
            .map(|d| d.as_secs())
            .unwrap_or(0);
        if freshest.as_ref().is_none_or(|(_, m)| modified > *m) {
            freshest = Some((path, modified));
        }
    }

    let Some((path, _)) = freshest else {
        return Ok(ImeAnalysis {
            file: String::new(),
            rows: Vec::new(),
            errors: 0,
            warnings: 0,
        });
    };

    let path_str = path.to_string_lossy().into_owned();
    let bytes = std::fs::read(&path).map_err(|e| format!("Couldn't read {path_str}: {e}"))?;
    let encoding = parser::detect_encoding(&bytes);
    let text = parser::decode_bytes(&bytes, encoding)?;

    // IME-specific analyzer: cmtrace-aware, enriches [SubSystem] into source_file.
    let (entries, _parse_errors) =
        cmtraceopen_parser::intune::ime_parser::parse_ime_entries(&text, &path_str);

    if entries.len() > LOG_TAIL_CAP {
        let drop = entries.len() - LOG_TAIL_CAP;
        let entries: Vec<LogEntry> = entries.into_iter().skip(drop).collect();
        return Ok(summarize_ime(&path, entries));
    }
    Ok(summarize_ime(&path, entries))
}

fn summarize_ime(path: &Path, entries: Vec<LogEntry>) -> ImeAnalysis {
    let file = path
        .file_name()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_default();
    let mut errors = 0usize;
    let mut warnings = 0usize;
    let rows = entries
        .into_iter()
        .map(|e| {
            match e.severity {
                Severity::Error => errors += 1,
                Severity::Warning => warnings += 1,
                Severity::Info => {}
            }
            to_ime_row(e)
        })
        .collect();
    ImeAnalysis {
        file,
        rows,
        errors,
        warnings,
    }
}

fn to_ime_row(e: LogEntry) -> ImeRow {
    // Keep just the time-of-day if the display carries a date too.
    let time = e.timestamp_display.unwrap_or_default();
    let time = time
        .split_whitespace()
        .last()
        .unwrap_or(time.as_str())
        .to_string();
    // Prefer the explicit component; fall back to the [SubSystem] the IME parser
    // lifted into source_file.
    let subsystem = e
        .component
        .filter(|c| !c.is_empty())
        .or(e.source_file)
        .unwrap_or_default();
    ImeRow {
        id: e.id,
        time,
        subsystem,
        message: e.message,
        severity: e.severity,
    }
}

// ─── Software Deployment workspace ──────────────────────────────────────────
// Ports cmtraceopen's deployment-log diagnostics (PSADT / MSI / Burn, auto-
// detected by parser::parse_content) into the unified app. Pure-local parse via
// the cmtraceopen-parser path dep — no sidecar, no api(). Mirrors the
// logs_workspace file-read pattern: std::fs::read -> detect_encoding ->
// decode_bytes -> parse_content -> project LogEntry into a PartialEq row.

// Default deployment-log location: PSADT/MSI installs land here under SCCM/Intune
// app-deployment. A sensible starting point the user can override in the text box.
const DEPLOY_LOG_DEFAULT: &str = r"C:\Windows\Logs\Software\PSAppDeployToolkit_Deploy.log";

// Cap rows so a huge install log stays virtualized-list friendly.
const DEPLOY_ROW_CAP: usize = 2000;

// LogEntry doesn't derive PartialEq and carries ~40 format-specific fields, so we
// project to a small Clone+PartialEq row before crossing the use_resource boundary
// (its T must be Send + Clone + PartialEq). Severity is Copy + PartialEq already.
#[derive(Clone, PartialEq)]
struct DeployRow {
    id: u64,
    time: String,
    component: String,
    message: String,
    severity: Severity,
}

// What the parse fetcher returns: the detected format label (for the header) plus
// the projected rows. Both halves are Clone + PartialEq so the tuple is too.
fn deployment_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // Path the user is editing (every keystroke) vs. the path actually parsed.
    let (draft, set_draft) = cx.use_state(String::from(DEPLOY_LOG_DEFAULT));
    let (path, set_path) = cx.use_state(String::from(DEPLOY_LOG_DEFAULT));

    // "Re-run" bumps a generation counter that re-keys the parse resource, so the
    // same path is re-read and re-parsed on demand (e.g. after an install reruns).
    let (generation, rerun) = cx.use_reducer(0_u64);

    let parsed = cx.use_resource(
        |key: (String, u64)| -> std::result::Result<(String, Vec<DeployRow>), String> {
            let (path, _gen) = key;
            if path.trim().is_empty() {
                return Ok((String::new(), Vec::new()));
            }
            parse_deployment_log(path.trim())
        },
        (path.clone(), generation),
    );


    let list_height = list_height(cx);
    // ── Controls: editable path + parse/re-run trigger ──────────────────────
    let path_box = {
        let set_draft = set_draft.clone();
        text_box(draft.clone())
            .placeholder_text("Path to a PSADT / MSI / Burn deployment log…".to_string())
            .on_text_changed(move |t| set_draft.call(t))
    };
    let parse_btn = {
        let draft = draft.clone();
        let set_path = set_path.clone();
        let rerun = rerun.clone();
        button("Parse").on_click(move || {
            set_path.call(draft.clone());
            rerun.call(|n| n + 1);
        })
    };
    let controls = hstack((Element::from(path_box), Element::from(parse_btn))).spacing(8.0);

    // ── Header: detected format + entry count, or the resolved file name ─────
    let header: Element = parsed
        .view(move |(fmt, rows): &(String, Vec<DeployRow>)| -> Element {
            if rows.is_empty() {
                return caption("No deployment entries parsed.").opacity(0.7).into();
            }
            let n = rows.len();
            let errs = rows
                .iter()
                .filter(|r| r.severity == Severity::Error)
                .count();
            let warns = rows
                .iter()
                .filter(|r| r.severity == Severity::Warning)
                .count();
            caption(format!(
                "detected {fmt} · {n} entries · {errs} errors · {warns} warnings"
            ))
            .opacity(0.7)
            .into()
        })
        .loading(caption("parsing deployment log…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    // ── Entry list with per-row severity coloring ───────────────────────────
    let list: Element = parsed
        .view(move |(_fmt, rows): &(String, Vec<DeployRow>)| -> Element {
            if rows.is_empty() {
                return body(
                    "Pick a deployment log above and press Parse. PSADT, MSI (msiexec /l*v) \
                     and Burn (bundle) logs are auto-detected.",
                )
                .opacity(0.6)
                .wrap()
                .into();
            }
            list_view(rows.clone(), |r: &DeployRow, _| deploy_row(r))
                .with_key_selector(|r: &DeployRow| r.id.to_string())
                .height(list_height - 96.0)
                .into()
        })
        .error(|e| error_box(e))
        .into();

    fluid_fill(12.0, vec![controls.into(), header], list)
        .margin(Thickness::uniform(16.0))
        .into()
}

// One deployment-log line: monospaced time + component + message, severity-tinted.
fn deploy_row(r: &DeployRow) -> Element {
    let msg = r.message.replace('\n', " ⏎ ");
    let line = format!("{:<14} {:<20} {}", r.time, truncate(&r.component, 20), msg);
    let tb = body(line).font_family("Consolas").font_size(12.0);
    let colored = match r.severity {
        Severity::Error => tb.foreground(theme::SEV_ERROR),
        Severity::Warning => tb.foreground(theme::SEV_WARN),
        Severity::Info => tb,
    };
    border(colored)
        .corner_radius(4.0)
        .padding(Thickness::xy(8.0, 2.0))
        .into()
}

// Read + decode + auto-detect-parse a deployment log on Reactor's background
// fetcher thread. Returns (detected-format-label, projected rows). Mirrors
// read_log_tail; parse_content auto-selects PSADT / MSI / Burn via detect.
fn parse_deployment_log(path: &str) -> std::result::Result<(String, Vec<DeployRow>), String> {
    let bytes = match std::fs::read(path) {
        Ok(b) => b,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
            return Err(format!("Deployment log not found: {path}"));
        }
        Err(e) => return Err(format!("Couldn't read {path}: {e}")),
    };
    let size = bytes.len() as u64;
    let encoding = parser::detect_encoding(&bytes);
    let text = parser::decode_bytes(&bytes, encoding)?;
    let (result, selection) = parser::parse_content(&text, path, size);

    let fmt = format!("{:?}", selection.compatibility_format());
    let mut entries = result.entries;
    if entries.len() > DEPLOY_ROW_CAP {
        let drop = entries.len() - DEPLOY_ROW_CAP;
        entries.drain(0..drop);
    }
    let rows: Vec<DeployRow> = entries.into_iter().map(to_deploy_row).collect();
    Ok((fmt, rows))
}

fn to_deploy_row(e: LogEntry) -> DeployRow {
    // Keep just the time-of-day component when the display carries a date too.
    let time = e.timestamp_display.unwrap_or_default();
    let time = time
        .split_whitespace()
        .last()
        .unwrap_or(time.as_str())
        .to_string();
    DeployRow {
        id: e.id,
        time,
        component: e.component.unwrap_or_default(),
        message: e.message,
        severity: e.severity,
    }
}

/// Local DNS-debug-log diagnostics ("DNS / DHCP" feature).
///
/// Parses a Windows DNS Server debug log (`dns.log`) entirely in-process via
/// `cmtraceopen-parser` — no sidecar, no auth. Either a default on-box path is
/// read, or the user pastes raw log text into the box. Parsed PACKET records are
/// projected into a small `Send + Clone + PartialEq` row (`LogEntry` itself does
/// not derive `PartialEq`, and the `use_resource` fetcher requires it) and shown
/// as a virtualized list.
const DNS_DEFAULT_PATH: &str = r"C:\Windows\System32\dns\dns.log";

#[derive(Clone, PartialEq)]
struct DnsRow {
    id: u64,
    time: String,
    direction: String,
    protocol: String,
    name: String,
    qtype: String,
    rcode: String,
    remote: String,
    severity: Severity,
}

// Source for a parse run: pasted text wins over the default path. Carried as the
// resource key so editing the path or pasting re-keys (and re-parses) the list.
#[derive(Clone, PartialEq)]
struct DnsSource {
    path: String,
    pasted: String,
}

fn parse_dns_source(src: &DnsSource) -> std::result::Result<Vec<DnsRow>, String> {
    // Prefer pasted text; fall back to reading the file at `path`.
    let (text, label) = if !src.pasted.trim().is_empty() {
        (src.pasted.clone(), "(pasted)".to_string())
    } else if !src.path.trim().is_empty() {
        let bytes =
            std::fs::read(&src.path).map_err(|e| format!("Couldn't read {}: {e}", src.path))?;
        let encoding = parser::detect_encoding(&bytes);
        let text = parser::decode_bytes(&bytes, encoding)?;
        (text, src.path.clone())
    } else {
        return Ok(Vec::new());
    };

    let size = text.len() as u64;
    let (result, _selection) = parser::parse_content(&text, &label, size);

    // Keep only records the DNS debug parser actually populated (PACKET rows carry
    // a query name / type); skip any plain-text fallback lines.
    let rows: Vec<DnsRow> = result
        .entries
        .into_iter()
        .filter(|e| e.query_name.is_some() || e.query_type.is_some())
        .map(|e| {
            // Trim the date off the display so the column stays tight (parser emits
            // "2026-04-11 15:29:17"); fall back to the whole string if there's no space.
            let time = e
                .timestamp_display
                .as_deref()
                .map(|t| t.split_whitespace().last().unwrap_or(t).to_string())
                .unwrap_or_default();
            DnsRow {
                id: e.id,
                time,
                direction: e.dns_direction.unwrap_or_default(),
                protocol: e.dns_protocol.unwrap_or_default(),
                name: e.query_name.unwrap_or_default(),
                qtype: e.query_type.unwrap_or_default(),
                rcode: e.response_code.unwrap_or_default(),
                remote: e.source_ip.unwrap_or_default(),
                severity: e.severity,
            }
        })
        .collect();

    Ok(rows)
}

fn dns_row(r: &DnsRow) -> Element {
    // Header line: "Rcv UDP  home.gell.one (SOA)"
    let dir_proto = format!("{} {}", r.direction, r.protocol);
    let head = format!("{:<8} {} ({})", dir_proto, r.name, r.qtype);
    let head_tb = body_strong(head).font_family("Consolas");
    let head_colored = match r.severity {
        Severity::Error => head_tb.foreground(theme::SEV_ERROR),
        Severity::Warning => head_tb.foreground(theme::SEV_WARN),
        Severity::Info => head_tb,
    };
    // Detail line: "15:29:17  127.0.0.1  →  NOERROR"
    let detail = format!("{}  {}  \u{2192}  {}", r.time, r.remote, r.rcode);
    border(
        vstack((
            head_colored,
            caption(detail).opacity(0.8).font_family("Consolas"),
        ))
        .spacing(2.0),
    )
    .corner_radius(4.0)
    .padding(Thickness::uniform(8.0))
    .into()
}

fn dns_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // Editable file path (defaults to the on-box DNS debug log) and a paste box.
    let (path_draft, set_path) = cx.use_state(String::from(DNS_DEFAULT_PATH));
    let (paste_draft, set_paste) = cx.use_state(String::new());

    // Re-run button bumps a reducer; the parse resource is keyed on it (plus the
    // current inputs) so clicking re-reads the file / re-parses the pasted text.
    let (run, bump) = cx.use_reducer(0_u64);

    let source = DnsSource {
        path: path_draft.clone(),
        pasted: paste_draft.clone(),
    };
    let rows = cx.use_resource(
        |key: (DnsSource, u64)| -> std::result::Result<Vec<DnsRow>, String> {
            parse_dns_source(&key.0)
        },
        (source, run),
    );


    let list_height = list_height(cx);
    let path_box = text_box(path_draft)
        .placeholder_text("Path to dns.log…")
        .on_text_changed(move |t| set_path.call(t));
    let paste_box = text_box(paste_draft)
        .multiline()
        .placeholder_text("…or paste DNS debug log text here")
        .on_text_changed(move |t| set_paste.call(t));
    let run_button = button("Re-run").on_click(move || bump.call(|n| n + 1));

    let controls = vstack((
        hstack((Element::from(path_box), Element::from(run_button))).spacing(8.0),
        paste_box,
    ))
    .spacing(8.0);

    let list: Element = rows
        .view(move |rows: &Vec<DnsRow>| -> Element {
            if rows.is_empty() {
                return body(
                    "No DNS PACKET records found. Check the path or paste a dns.log, then Re-run.",
                )
                .opacity(0.6)
                .wrap()
                .into();
            }
            let count = caption(format!("{} query records", rows.len())).opacity(0.7);
            let lv = list_view(rows.clone(), |r: &DnsRow, _| dns_row(r))
                .with_key_selector(|r: &DnsRow| r.id.to_string())
                .height(list_height - 56.0);
            fluid_fill(8.0, vec![count.into()], lv.into()).into()
        })
        .loading(caption("parsing dns.log…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    fluid_fill(12.0, vec![controls.into()], list)
        .margin(Thickness::uniform(16.0))
        .into()
}

// ─── Registry Viewer ─────────────────────────────────────────────────────────

// A flat, comparable projection of the registry parser's nested key/value model.
// `RegistryParseResult`/`RegistryKey`/`RegistryValue` don't derive `PartialEq`
// (which `use_resource` requires), so we flatten into our own rows: one synthetic
// header row per key path, followed by one row per value, so the list reads
// top-down like a regedit pane.
#[derive(Clone, PartialEq)]
struct RegRow {
    key: String,     // full key path this row belongs to
    is_header: bool, // true => this row is the key path itself, no value
    name: String,    // value name ("(Default)", etc.); the header text for headers
    detail: String,  // value data + kind tag; empty for headers
}

const REGISTRY_DEFAULT_PATH: &str = r"C:\ProgramData\cmProjectX\samples\export.reg";

// Read + decode + parse a .reg file (or parse pasted text when `pasted` is set),
// then flatten to comparable rows and filter by a case-insensitive key substring.
// Runs on the use_resource background thread, so it returns Result<_, String>.
fn load_registry_rows(
    path: String,
    pasted: String,
    filter: String,
) -> std::result::Result<Vec<RegRow>, String> {
    let (text, src_path, size) = if !pasted.trim().is_empty() {
        let size = pasted.len() as u64;
        (pasted, "(pasted)".to_string(), size)
    } else {
        let bytes = std::fs::read(&path).map_err(|e| format!("Couldn't read {path}: {e}"))?;
        let size = bytes.len() as u64;
        let encoding = parser::detect_encoding(&bytes);
        let text = parser::decode_bytes(&bytes, encoding)?;
        (text, path, size)
    };

    // Returns RegistryParseResult by value (not a Result), so no `?` here.
    let result = parser::registry::parse_registry_content(&text, &src_path, size);
    let needle = filter.trim().to_lowercase();

    let mut rows: Vec<RegRow> = Vec::new();
    for key in &result.keys {
        if !needle.is_empty() && !key.path.to_lowercase().contains(&needle) {
            continue;
        }
        let header = if key.is_delete {
            format!("{} (delete key)", key.path)
        } else {
            key.path.clone()
        };
        rows.push(RegRow {
            key: key.path.clone(),
            is_header: true,
            name: header,
            detail: String::new(),
        });
        for v in &key.values {
            // RegistryValueKind is Debug; show data plus its kind tag.
            rows.push(RegRow {
                key: key.path.clone(),
                is_header: false,
                name: v.name.clone(),
                detail: format!("{} · {:?}", v.data, v.kind),
            });
        }
    }
    Ok(rows)
}

fn reg_row(r: &RegRow) -> Element {
    if r.is_header {
        return border(body_strong(r.name.clone()).font_family("Consolas").wrap())
            .corner_radius(4.0)
            .padding(Thickness::xy(8.0, 4.0))
            .into();
    }
    vstack((
        body(r.name.clone()).font_family("Consolas").wrap(),
        caption(r.detail.clone()).opacity(0.8).wrap(),
    ))
    .spacing(2.0)
    .margin(Thickness::xy(12.0, 2.0))
    .into()
}

fn registry_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (path, set_path) = cx.use_state(String::from(REGISTRY_DEFAULT_PATH));
    let (pasted, set_pasted) = cx.use_state(String::new());
    let (draft, set_draft) = cx.use_state(String::new());
    let (filter, set_filter) = cx.use_state(String::new());

    // A "Re-run" reducer: bumping it re-keys the resource so the same path/text is
    // re-read and re-parsed on demand (mirrors the logs tab's tick re-keying).
    let (run, rerun) = cx.use_reducer(0_u64);

    let rows = cx.use_resource(
        |key: (String, String, String, u64)| -> std::result::Result<Vec<RegRow>, String> {
            let (path, pasted, filter, _run) = key;
            load_registry_rows(path, pasted, filter)
        },
        (path.clone(), pasted.clone(), filter.clone(), run),
    );

    let lh = list_height(cx);
    let controls: Element = {
        let rerun = rerun.clone();
        let set_path = set_path.clone();
        vstack((
            caption("Registry export (.reg) — set a file path, or paste content below.")
                .opacity(0.7)
                .wrap(),
            text_box(path.clone()).on_text_changed(move |t| set_path.call(t)),
            text_box(pasted.clone())
                .multiline()
                .on_text_changed(move |t| set_pasted.call(t)),
            hstack((
                Element::from(button("Re-run").on_click(move || rerun.call(|n| n + 1))),
                Element::from(search_box(
                    draft,
                    "Filter by key path…",
                    set_draft,
                    set_filter,
                )),
            ))
            .spacing(8.0),
        ))
        .spacing(8.0)
    }
    .into();

    let list: Element = rows
        .view(move |rs: &Vec<RegRow>| -> Element {
            if rs.is_empty() {
                return body(
                    "No keys parsed. Check the path, paste a .reg export, or adjust the filter.",
                )
                .opacity(0.6)
                .wrap()
                .into();
            }
            list_view(rs.clone(), |r: &RegRow, _| reg_row(r))
                .with_key_selector(|r: &RegRow| {
                    if r.is_header {
                        format!("k:{}", r.key)
                    } else {
                        format!("v:{}|{}", r.key, r.name)
                    }
                })
                .height(lh - 140.0)
                .into()
        })
        .loading(caption("parsing registry export…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    fluid_fill(12.0, vec![controls], list)
        .margin(Thickness::uniform(16.0))
        .into()
}
