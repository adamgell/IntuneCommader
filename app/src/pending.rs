//! "Pending AI changes" inbox (M13.2) — the human-in-the-loop approval queue for
//! writes an MCP client proposed over the sidecar's MCP server. We chose an
//! expose-only model (no in-app chat): the operator's preferred MCP client is the
//! chat, and THIS app is the approval authority. Each proposed write is parked
//! server-side; this screen polls `/pending-changes`, shows the proposed diff with
//! the same `drift_row` panel a human edit uses, and Approve / Reject it. Approve
//! replays the write server-side (snapshot-on-write + audit); Reject records it.

use std::time::Duration;

use api_types::PendingChange;
use windows_reactor::*;

use crate::api_client::{api, service_err};
use crate::{drift_row, error_box, fluid_fill, list_height};

pub fn pending_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // Poll cadence: a 2s timer bumps `tick`, which re-keys (refetches) the list. (The
    // root health poll already re-renders this every 2s; the local tick is what makes
    // the resource refetch.) Mirrors the Logs live-tail pattern.
    let (tick, bump) = cx.use_reducer(0_u64);
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

    let pending = cx.use_resource(|_: u64| api().list_pending().map_err(service_err), tick);

    let (approve, do_approve) = cx.use_mutation::<()>();
    let (reject, do_reject) = cx.use_mutation::<()>();

    // After a decision lands, refetch at once and reset the mutations so the next
    // decision can fire.
    let decided = approve.data().is_some() || reject.data().is_some();
    {
        let bump = bump.clone();
        let do_approve = do_approve.clone();
        let do_reject = do_reject.clone();
        cx.use_effect(decided, move || {
            if decided {
                bump.call(|n| n + 1);
                do_approve.reset();
                do_reject.reset();
            }
        });
    }

    let lh = list_height(cx);
    let busy = approve.is_loading() || reject.is_loading();
    let err: Element = approve
        .error()
        .or_else(|| reject.error())
        .map(|e| {
            caption(format!("action failed: {e}"))
                .opacity(0.85)
                .wrap()
                .into()
        })
        .unwrap_or(Element::Empty);

    let list: Element = pending
        .view({
            let do_approve = do_approve.clone();
            let do_reject = do_reject.clone();
            move |items: &Vec<PendingChange>| -> Element {
                if items.is_empty() {
                    return body(
                        "Nothing waiting. Proposals from your MCP client (Claude Desktop, Cursor, \
                         Copilot…) appear here for review.",
                    )
                    .opacity(0.6)
                    .wrap()
                    .into();
                }
                let do_approve = do_approve.clone();
                let do_reject = do_reject.clone();
                list_view(items.clone(), move |c: &PendingChange, _| {
                    pending_card(c, &do_approve, &do_reject, busy)
                })
                .with_key_selector(|c: &PendingChange| c.id.clone())
                .height(lh - 64.0)
                .into()
            }
        })
        .loading(caption("loading pending changes…").opacity(0.6))
        .error(|e| error_box(e))
        .into();

    fluid_fill(
        12.0,
        vec![
            body_strong("Pending AI changes").into(),
            caption(
                "Writes proposed over MCP. Review the diff, then Approve (replays the write with \
                 snapshot + audit) or Reject.",
            )
            .opacity(0.7)
            .wrap()
            .into(),
            err,
        ],
        list,
    )
    .margin(Thickness::uniform(16.0))
    .into()
}

// One queued change: header (what + where), provenance, the proposed diff, and the
// Approve / Reject actions. `Approve`/`Reject` fire the parent's mutations by id.
fn pending_card(
    c: &PendingChange,
    do_approve: &MutationTrigger<()>,
    do_reject: &MutationTrigger<()>,
    busy: bool,
) -> Element {
    let title = c
        .object_name
        .clone()
        .or_else(|| c.object_id.clone())
        .unwrap_or_else(|| c.path.clone());
    let header = hstack((
        body_strong(format!("{} · {}", kind_label(&c.kind), title)),
        caption(c.path.clone()).opacity(0.55),
    ))
    .spacing(8.0);
    let meta = caption(format!("proposed by {} · {}", c.proposer, c.created_utc)).opacity(0.5);

    let diff: Element = if c.changes.is_empty() {
        caption("(no field-level diff to preview)").opacity(0.6).into()
    } else {
        let rows: Vec<Element> = c.changes.iter().map(drift_row).collect();
        vstack(rows).spacing(4.0).into()
    };

    let approve_btn = button(if busy { "Working…" } else { "Approve" })
        .accent()
        .enabled(!busy)
        .on_click({
            let m = do_approve.clone();
            let id = c.id.clone();
            move || {
                let id = id.clone();
                m.fire(move || api().approve_change(&id).map_err(service_err));
            }
        });
    let reject_btn = button("Reject").enabled(!busy).on_click({
        let m = do_reject.clone();
        let id = c.id.clone();
        move || {
            let id = id.clone();
            m.fire(move || api().reject_change(&id).map_err(service_err));
        }
    });

    border(
        vstack((
            header,
            meta,
            blast_header(c),
            diff,
            hstack((Element::from(approve_btn), Element::from(reject_btn))).spacing(8.0),
        ))
        .spacing(8.0),
    )
    .corner_radius(6.0)
    .padding(Thickness::uniform(12.0))
    .margin(Thickness::xy(0.0, 4.0))
    .into()
}

// M16 — the blast-radius header: severity + one-line summary + affected counts,
// shown above the field-level diff (impact-scale before delta detail), mirroring the
// M6 confirm gate. Empty when the proposal carried no simulation.
fn blast_header(c: &PendingChange) -> Element {
    let Some(br) = c.blast_radius.as_ref() else {
        return Element::Empty;
    };
    let counts = format!(
        "{} user(s) · {} device(s) newly affected{}",
        br.affected_user_count,
        br.affected_device_count,
        if br.no_longer_affected_count > 0 {
            format!(" · {} no longer targeted", br.no_longer_affected_count)
        } else {
            String::new()
        },
    );
    let mut lines: Vec<Element> = vec![
        hstack((
            body_strong(format!("Blast radius: {}", br.severity.to_uppercase())),
            caption(counts).opacity(0.7),
        ))
        .spacing(8.0)
        .into(),
        caption(br.summary.clone()).opacity(0.8).wrap().into(),
    ];
    if !br.conflicts.is_empty() {
        lines.push(
            caption(format!("{} conflict(s) detected", br.conflicts.len()))
                .opacity(0.75)
                .into(),
        );
    }
    border(vstack(lines).spacing(4.0))
        .corner_radius(6.0)
        .padding(Thickness::uniform(8.0))
        .into()
}

fn kind_label(kind: &str) -> &'static str {
    match kind {
        "create" => "Create",
        "update" => "Update",
        "delete" => "Delete",
        "assign" => "Set assignments",
        _ => "Change",
    }
}
