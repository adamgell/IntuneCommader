//! Diagnostics: Collector — one-click support bundle.
//!
//! Gathers the key LOCAL diagnostics (device registration, Event Logs, Sysmon,
//! system info) into a single `.zip` on the Desktop, ready to attach to a support
//! ticket. Pure Rust, NO sidecar / NO auth: it shells the same tools the other
//! diagnostics workspaces use, writes each output to a temp folder, then zips it
//! with PowerShell's `Compress-Archive` (so there is no zip-crate dependency).

use windows_reactor::*;

// Result of one collection run: where the bundle landed + what went into it.
#[derive(Clone, PartialEq)]
struct Collected {
    zip_path: String,
    items: Vec<String>,
}

// Run a command and capture stdout (+ stderr, appended) as text. A spawn failure
// becomes an inline note rather than aborting the whole bundle, so one missing
// tool never blocks the rest.
fn run_text(program: &str, args: &[&str]) -> String {
    match std::process::Command::new(program).args(args).output() {
        Ok(o) => {
            let mut s = String::from_utf8_lossy(&o.stdout).into_owned();
            if !o.stderr.is_empty() {
                s.push_str("\n--- stderr ---\n");
                s.push_str(&String::from_utf8_lossy(&o.stderr));
            }
            s
        }
        Err(e) => format!("(failed to run {program}: {e})"),
    }
}

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

fn winevent_cmd(log: &str) -> String {
    format!(
        "Get-WinEvent -LogName '{log}' -MaxEvents 200 -ErrorAction SilentlyContinue | \
         Format-Table TimeCreated,Id,LevelDisplayName,ProviderName -Auto | Out-String -Width 240"
    )
}

fn collect() -> std::result::Result<Collected, String> {
    let base = std::env::temp_dir().join("cmprojectx-diag");
    let _ = std::fs::remove_dir_all(&base);
    std::fs::create_dir_all(&base).map_err(|e| format!("create temp dir: {e}"))?;

    // (file, human label, captured content) — building the array runs each tool.
    let artifacts: [(&str, &str, String); 5] = [
        (
            "dsregcmd-status.txt",
            "Device registration (dsregcmd /status)",
            run_text("dsregcmd", &["/status"]),
        ),
        (
            "eventlog-system.txt",
            "Event Log — System (last 200)",
            run_text("powershell", &["-NoProfile", "-Command", &winevent_cmd("System")]),
        ),
        (
            "eventlog-application.txt",
            "Event Log — Application (last 200)",
            run_text("powershell", &["-NoProfile", "-Command", &winevent_cmd("Application")]),
        ),
        (
            "sysmon.txt",
            "Sysmon operational (last 200, if installed)",
            run_text(
                "powershell",
                &["-NoProfile", "-Command", &winevent_cmd("Microsoft-Windows-Sysmon/Operational")],
            ),
        ),
        (
            "systeminfo.txt",
            "System information",
            run_text("systeminfo", &[]),
        ),
    ];

    let mut items: Vec<String> = Vec::new();
    for (name, label, content) in artifacts {
        if std::fs::write(base.join(name), content).is_ok() {
            items.push(label.to_string());
        }
    }
    if items.is_empty() {
        return Err("Couldn't gather any diagnostics on this device.".into());
    }

    let zip = desktop_or_temp("IntuneCommander-diagnostics.zip");
    let _ = std::fs::remove_file(&zip);
    let zip_str = zip.to_string_lossy().into_owned();
    let base_glob = format!("{}\\*", base.to_string_lossy());
    let archive = run_text(
        "powershell",
        &[
            "-NoProfile",
            "-Command",
            &format!("Compress-Archive -Path '{base_glob}' -DestinationPath '{zip_str}' -Force"),
        ],
    );
    if !zip.exists() {
        return Err(format!("Compress-Archive failed: {}", archive.trim()));
    }
    Ok(Collected { zip_path: zip_str, items })
}

pub fn collector_workspace(_: &(), cx: &mut RenderCx) -> Element {
    // `started` gates the (side-effecting) collect resource so nothing runs until
    // the button is pressed; bumping `tick` re-collects on subsequent presses.
    let (started, set_started) = cx.use_state(false);
    let (tick, bump) = cx.use_reducer(0_u64);
    let result = cx.use_resource(
        |key: (bool, u64)| -> std::result::Result<Option<Collected>, String> {
            let (started, _) = key;
            if !started {
                return Ok(None);
            }
            collect().map(Some)
        },
        (started, tick),
    );

    let collecting = result.is_loading();
    let collect_btn = button(if collecting { "Collecting…" } else { "Collect diagnostics" })
        .accent()
        .enabled(!collecting)
        .on_click({
            let set_started = set_started.clone();
            move || {
                set_started.call(true);
                bump.call(|n| n + 1);
            }
        });

    let intro = caption(
        "Gathers device registration, Event Logs, Sysmon, and system info into a single \
         .zip you can attach to a support ticket.",
    )
    .opacity(0.7)
    .wrap();

    let body_el: Element = result
        .view(|r: &Option<Collected>| -> Element {
            match r {
                None => caption("Click Collect diagnostics to build a bundle.")
                    .opacity(0.6)
                    .into(),
                Some(c) => {
                    let header = body_strong("Diagnostics bundle ready");
                    let path = caption(format!("Saved to: {}", c.zip_path))
                        .font_family("Consolas")
                        .opacity(0.85)
                        .wrap();
                    let items: Vec<Element> = c
                        .items
                        .iter()
                        .map(|i| caption(format!("• {i}")).opacity(0.8).into())
                        .collect();
                    vstack((
                        Element::from(header),
                        Element::from(path),
                        Element::from(vstack(items).spacing(2.0)),
                    ))
                    .spacing(8.0)
                    .into()
                }
            }
        })
        .loading(caption("collecting diagnostics… (this can take a few seconds)").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    vstack((Element::from(collect_btn), Element::from(intro), body_el))
        .spacing(12.0)
        .margin(Thickness::uniform(16.0))
        .into()
}
