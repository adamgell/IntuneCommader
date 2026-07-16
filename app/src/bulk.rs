//! M8 Bulk & lifecycle workspaces: Backup / Export and Restore / Import.
//!
//! Thin clients over the sidecar's bulk endpoints (which wire the forked Core
//! Export/Import engines). Export downloads a backup `.zip` to the Desktop; Import
//! is a dry-run that previews what a bundle contains (restore-to-tenant is gated
//! off in the prototype). Both use the proven resource-gated-by-a-flag idiom.

use crate::api_client::{api, service_err};
use crate::dialogs::{open_with_default, pick_open_file, pick_save_file, reveal_in_explorer};
use crate::theme;
use api_types::ImportRestoreResult;
use windows_reactor::*;

// Prefer the Desktop for the finished zip (easy to find in a demo); fall back to
// the temp dir if the profile/Desktop isn't resolvable.
fn desktop_or_temp(file: &str) -> std::path::PathBuf {
    if let Ok(up) = std::env::var("USERPROFILE") {
        let d = std::path::Path::new(&up).join("Desktop");
        if d.is_dir() {
            return d.join(file);
        }
    }
    std::env::temp_dir().join(file)
}

fn run_export_to(path: &str) -> std::result::Result<String, String> {
    let bytes = api().export_backup().map_err(service_err)?;
    std::fs::write(path, &bytes).map_err(|e| format!("couldn't write the backup: {e}"))?;
    Ok(path.to_string())
}

fn run_ca_pptx() -> std::result::Result<String, String> {
    let bytes = api().export_ca_pptx().map_err(service_err)?;
    let path = desktop_or_temp("conditional-access.pptx");
    std::fs::write(&path, &bytes).map_err(|e| format!("couldn't write the presentation: {e}"))?;
    Ok(path.to_string_lossy().into_owned())
}

pub fn ca_pptx_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (started, set_started) = cx.use_state(false);
    let (tick, bump) = cx.use_reducer(0_u64);
    let result = cx.use_resource(
        |key: (bool, u64)| -> std::result::Result<Option<String>, String> {
            let (started, _) = key;
            if !started {
                return Ok(None);
            }
            run_ca_pptx().map(Some)
        },
        (started, tick),
    );

    let busy = result.is_loading();
    let btn = button(if busy { "Exporting…" } else { "Export CA to PowerPoint (.pptx)" })
        .accent()
        .enabled(!busy)
        .on_click({
            let set_started = set_started.clone();
            move || {
                set_started.call(true);
                bump.call(|n| n + 1);
            }
        });

    let intro = caption(
        "Generates a PowerPoint with one slide per Conditional Access policy from the signed-in \
         tenant (GUIDs resolved to names), saved to your Desktop.",
    )
    .opacity(0.7)
    .wrap();

    let body_el: Element = result
        .view(|r: &Option<String>| -> Element {
            match r {
                None => caption("Sign in, then Export to generate the presentation.").opacity(0.6).into(),
                Some(p) => {
                    let p_open = p.clone();
                    let p_reveal = p.clone();
                    vstack((
                        Element::from(body_strong("Presentation created")),
                        Element::from(
                            caption(format!("Saved to: {p}"))
                                .font_family("Consolas")
                                .opacity(0.85)
                                .wrap(),
                        ),
                        Element::from(
                            hstack((
                                Element::from(button("Open presentation").accent().on_click(
                                    move || {
                                        let _ = open_with_default(&p_open);
                                    },
                                )),
                                Element::from(button("Show in folder").on_click(move || {
                                    let _ = reveal_in_explorer(&p_reveal);
                                })),
                            ))
                            .spacing(8.0),
                        ),
                    ))
                    .spacing(8.0)
                    .into()
                }
            }
        })
        .loading(caption("building the presentation from the tenant…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    vstack((Element::from(btn), Element::from(intro), body_el))
        .spacing(12.0)
        .margin(Thickness::uniform(16.0))
        .into()
}

pub fn export_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (dest, set_dest) = cx.use_state(String::new()); // chosen save path ("" = none yet)
    let (tick, bump) = cx.use_reducer(0_u64);
    let result = cx.use_resource(
        |key: (String, u64)| -> std::result::Result<Option<String>, String> {
            let (d, _) = key;
            if d.is_empty() {
                return Ok(None);
            }
            run_export_to(&d).map(Some)
        },
        (dest.clone(), tick),
    );

    let busy = result.is_loading();
    let btn = button(if busy { "Exporting…" } else { "Export backup…" })
        .accent()
        .enabled(!busy)
        .on_click({
            let sd = set_dest.clone();
            move || {
                if let Some(p) = pick_save_file(
                    "Save tenant backup",
                    "intunecommander-backup.zip",
                    &[("Backup zip", &["zip"]), ("All files", &["*"])],
                ) {
                    sd.call(p);
                    bump.call(|n| n + 1);
                }
            }
        });

    let intro = caption(
        "Backs up every supported surface from the signed-in tenant — device configs, compliance, \
         settings catalog, apps, endpoint security, scripts, identity, tenant-admin and more — to a \
         .zip you choose (one folder per type + a migration table).",
    )
    .opacity(0.7)
    .wrap();

    let body_el: Element = result
        .view(|r: &Option<String>| -> Element {
            match r {
                None => caption("Sign in, then Export to create a backup.").opacity(0.6).into(),
                Some(p) => {
                    let p_reveal = p.clone();
                    vstack((
                        Element::from(body_strong("Backup created")),
                        Element::from(
                            caption(format!("Saved to: {p}"))
                                .font_family("Consolas")
                                .opacity(0.85)
                                .wrap(),
                        ),
                        Element::from(button("Show in folder").on_click(move || {
                            let _ = reveal_in_explorer(&p_reveal);
                        })),
                    ))
                    .spacing(6.0)
                    .into()
                }
            }
        })
        .loading(caption("exporting from the tenant…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    vstack((Element::from(btn), Element::from(intro), body_el))
        .spacing(12.0)
        .margin(Thickness::uniform(16.0))
        .into()
}

pub fn import_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (path, set_path) = cx.use_state(String::new());
    let (submitted, set_submitted) = cx.use_state(String::new()); // path actually previewed
    let (preview_gen, bump) = cx.use_reducer(0_u64);
    let (confirm, set_confirm) = cx.use_state(false); // two-step gate before a live restore
    let (restore, do_restore) = cx.use_mutation::<ImportRestoreResult>();
    let result = cx.use_resource(
        |key: (String, u64)| -> std::result::Result<Option<String>, String> {
            let (p, _) = key;
            if p.is_empty() {
                return Ok(None);
            }
            let bytes = std::fs::read(&p).map_err(|e| format!("couldn't read {p}: {e}"))?;
            api().import_preview(bytes).map(Some).map_err(service_err)
        },
        (submitted.clone(), preview_gen),
    );

    let busy = result.is_loading();
    let restoring = restore.is_loading();
    let path_tb = text_box(path.clone())
        .placeholder_text("Path to a backup .zip".to_string())
        .on_text_changed({
            let s = set_path.clone();
            move |t| s.call(t)
        });
    let preview_btn = button(if busy { "Reading…" } else { "Preview" })
        .accent()
        .enabled(!busy && !restoring && !path.is_empty())
        .on_click({
            let set_submitted = set_submitted.clone();
            let set_confirm = set_confirm.clone();
            let do_restore = do_restore.clone();
            let path = path.clone();
            move || {
                set_confirm.call(false);
                do_restore.reset(); // clear any prior restore result/error
                set_submitted.call(path.clone());
                bump.call(|n| n + 1);
            }
        });
    let browse_btn = button("Browse…").enabled(!busy && !restoring).on_click({
        let s = set_path.clone();
        move || {
            if let Some(p) = pick_open_file(
                "Choose a backup .zip",
                &[("Backup", &["zip"]), ("All files", &["*"])],
            ) {
                s.call(p);
            }
        }
    });

    let intro = caption(
        "Preview lists what a backup .zip contains. Restore creates every object in the bundle in the \
         signed-in tenant (fresh copies, new ids).",
    )
    .opacity(0.7)
    .wrap();

    let body_el: Element = result
        .view(|r: &Option<String>| -> Element {
            match r {
                None => caption("Enter a backup .zip path and Preview.").opacity(0.6).into(),
                Some(json) => text_box(json.clone()).multiline().enabled(false).into(),
            }
        })
        .loading(caption("reading bundle…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    // A preview is showing → offer the (gated) live restore.
    let previewed = !submitted.is_empty() && result.data().map(|o| o.is_some()).unwrap_or(false);
    let restore_row: Element = if previewed {
        let btn = if confirm {
            Element::from(
                button(if restoring {
                    "Restoring… (writing to the live tenant)"
                } else {
                    "Confirm — restore into the LIVE tenant"
                })
                .accent()
                .enabled(!restoring)
                .on_click({
                    let m = do_restore.clone();
                    let sc = set_confirm.clone();
                    let p = submitted.clone();
                    move || {
                        let p = p.clone();
                        m.fire(move || {
                            let bytes = std::fs::read(&p).map_err(|e| format!("couldn't read {p}: {e}"))?;
                            api().import_apply(bytes).map_err(service_err)
                        });
                        sc.call(false);
                    }
                }),
            )
        } else {
            Element::from(
                button("Restore to tenant…").enabled(!restoring).on_click({
                    let sc = set_confirm.clone();
                    move || sc.call(true)
                }),
            )
        };
        let hint = if confirm {
            caption(
                "This creates every object in the bundle in the signed-in tenant. Click again to \
                 proceed, or Preview to cancel.",
            )
            .foreground(theme::WARN)
            .wrap()
        } else {
            caption("Restore the bundle into the signed-in tenant (creates fresh copies).")
                .opacity(0.6)
                .wrap()
        };
        vstack((btn, Element::from(hint))).spacing(6.0).into()
    } else {
        Element::Empty
    };

    let restore_result: Element = if let Some(r) = restore.data() {
        let mut rows: Vec<Element> = vec![Element::from(body_strong(format!(
            "Restore complete — {} created, {} failed",
            r.total_created, r.total_failed
        )))];
        for grp in &r.groups {
            let line = if grp.failed > 0 {
                format!("{}: {} created, {} failed", grp.kind, grp.created, grp.failed)
            } else {
                format!("{}: {} created", grp.kind, grp.created)
            };
            rows.push(Element::from(
                caption(line).font_family("Consolas").opacity(0.85),
            ));
        }
        border(vstack(rows).spacing(3.0))
            .corner_radius(4.0)
            .padding(Thickness::uniform(12.0))
            .into()
    } else if let Some(e) = restore.error() {
        crate::error_box(&format!("restore failed: {e}"))
    } else if restoring {
        caption("restoring the bundle into the tenant…").opacity(0.6).into()
    } else {
        Element::Empty
    };

    vstack((
        hstack((
            Element::from(path_tb),
            Element::from(browse_btn),
            Element::from(preview_btn),
        ))
        .spacing(8.0),
        Element::from(intro),
        body_el,
        restore_row,
        restore_result,
    ))
    .spacing(12.0)
    .margin(Thickness::uniform(16.0))
    .into()
}
