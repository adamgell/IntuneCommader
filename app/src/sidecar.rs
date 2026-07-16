//! Sidecar lifecycle — the client owns the .NET service process.
//!
//! On launch the client ensures a sidecar is reachable on 127.0.0.1:5099: if one
//! is already running (a dev session, or a prior instance), it reuses it and owns
//! nothing; otherwise it spawns the bundled sidecar and returns a guard that kills
//! it when the app exits. This removes the "start the sidecar yourself" step — the
//! app is the only thing you run.
//!
//! Resolution order for the sidecar binary:
//!   1. a bundled native/self-contained `Api.exe` next to this exe (the shipping
//!      path — no machine .NET install, no JIT cold-start; see ROADMAP M12),
//!   2. else `dotnet <repo>/service/Api/bin/<cfg>/net10.0/Api.dll` (dev).
//!
//! Child stdout/stderr go to NULL (not a drained pipe): the sidecar logs every
//! request, so an undrained pipe would fill its ~64KB buffer and deadlock it — we
//! don't need its logs in the client, so null is the simplest safe sink.

use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::time::Duration;

const CREATE_NO_WINDOW: u32 = 0x0800_0000;
const HEALTH_URL: &str = "http://127.0.0.1:5099/health";

/// Owns the spawned sidecar process (if we started one) and kills it on drop.
/// `child == None` means a sidecar was already running and we're just borrowing it.
pub struct SidecarGuard {
    child: Option<Child>,
}

impl Drop for SidecarGuard {
    fn drop(&mut self) {
        if let Some(mut c) = self.child.take() {
            let _ = c.kill();
            let _ = c.wait();
        }
    }
}

/// Ensure the sidecar is reachable; spawn it if not. Returns immediately (does not
/// block on readiness) — the UI's `/health` poll already renders a "connecting to
/// service…" state while it warms up, so the window appears at once.
pub fn ensure_running() -> SidecarGuard {
    if health_ok() {
        return SidecarGuard { child: None }; // reuse a sidecar we don't own
    }
    SidecarGuard { child: spawn_sidecar() }
}

fn health_ok() -> bool {
    reqwest::blocking::Client::builder()
        .timeout(Duration::from_millis(500))
        .build()
        .ok()
        .and_then(|c| c.get(HEALTH_URL).send().ok())
        .map(|r| r.status().is_success())
        .unwrap_or(false)
}

fn spawn_sidecar() -> Option<Child> {
    let (program, args, work_dir) = sidecar_command()?;
    let mut cmd = Command::new(program);
    cmd.args(&args)
        .creation_flags(CREATE_NO_WINDOW)
        .stdin(Stdio::null())
        .stdout(Stdio::null()) // discard — avoids the pipe-buffer deadlock
        .stderr(Stdio::null());
    if let Some(dir) = work_dir {
        cmd.current_dir(dir);
    }
    cmd.spawn().ok()
}

/// (program, args, working_dir) to launch the sidecar — bundled exe if present,
/// else `dotnet <Api.dll>` for dev.
fn sidecar_command() -> Option<(String, Vec<String>, Option<PathBuf>)> {
    let exe_dir = std::env::current_exe().ok()?.parent()?.to_path_buf();

    // (1) Shipping: a self-contained `Api.exe` next to app.exe.
    let bundled = exe_dir.join("Api.exe");
    if bundled.exists() {
        return Some((bundled.to_string_lossy().into_owned(), vec![], Some(exe_dir)));
    }

    // (1b) Shipping (release bundle layout): the self-contained sidecar lives in a
    // `sidecar/` subfolder next to the client exe (see the release stage step). The
    // sidecar's working dir must be that folder so it resolves its own DLLs/psmodules.
    let sub = exe_dir.join("sidecar").join("Api.exe");
    if sub.exists() {
        let work = sub.parent().map(Path::to_path_buf);
        return Some((sub.to_string_lossy().into_owned(), vec![], work));
    }

    // (2) Dev: the framework-dependent build output, run via `dotnet`.
    let dll = find_api_dll(&exe_dir)?;
    let work = dll.parent().map(Path::to_path_buf);
    Some(("dotnet".to_string(), vec![dll.to_string_lossy().into_owned()], work))
}

/// Walk up from `start` to the repo root, then locate the built sidecar dll.
fn find_api_dll(start: &Path) -> Option<PathBuf> {
    let mut dir = Some(start.to_path_buf());
    while let Some(d) = dir {
        let api = d.join("service").join("Api");
        if api.is_dir() {
            for cfg in ["Debug", "Release"] {
                let dll = api.join("bin").join(cfg).join("net10.0").join("Api.dll");
                if dll.exists() {
                    return Some(dll);
                }
            }
        }
        dir = d.parent().map(Path::to_path_buf);
    }
    None
}
