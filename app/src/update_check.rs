//! Update check + self-update (M12 auto-updater).
//!
//! Increment 1 (notify): on launch the shell asks the release repo's GitHub API for the
//! latest published version and, if it's newer, surfaces an "Update available" pill.
//!
//! Increment 2 (download + apply, this slice): [`apply_update`] resolves THIS arch's
//! release bundle (`IntuneCommander-<ver>-win-<arch>.zip`, produced by scripts/release.ps1),
//! streams it down with a fail-closed size check, writes a detached PowerShell swap helper,
//! and spawns it. The helper waits for this process to exit, backs the current install up to
//! `<install>.bak`, extracts + copies the new bundle over it, and relaunches. It is only ever
//! invoked by an explicit human action (never automatic), and the app must exit right after a
//! successful spawn so the helper can replace the now-unlocked files.
//!
//! Testability + honesty: the pure pieces (version compare, arch token, asset selection, swap-
//! script generation) are unit-tested; the effectful pieces (GET, spawn) are bounded and fail-
//! closed but are NOT exercised against a live signed feed here — first real cut validates them.

use std::io::Write;
use std::path::{Path, PathBuf};

/// This build's version (from Cargo).
pub const CURRENT: &str = env!("CARGO_PKG_VERSION");

/// The release repository the packaged app is published to (see the production-release
/// topology). Only its *latest* published tag is read — no auth, no write.
const RELEASE_API: &str =
    "https://api.github.com/repos/gellorg/intunecommander-release/releases/latest";

/// Parse "v1.2.3" / "1.2.3" / "1.2.3-beta" into (major, minor, patch), ignoring any
/// pre-release/build suffix. Missing minor/patch default to 0.
fn parse(v: &str) -> Option<(u32, u32, u32)> {
    let core = v.trim().trim_start_matches('v').split(['-', '+']).next().unwrap_or("");
    let mut p = core.split('.');
    let maj = p.next()?.parse().ok()?;
    let min = p.next().and_then(|s| s.parse().ok()).unwrap_or(0);
    let pat = p.next().and_then(|s| s.parse().ok()).unwrap_or(0);
    Some((maj, min, pat))
}

/// True when `latest` is a strictly-newer release than `current`. Pure; unit-tested.
pub fn update_available(current: &str, latest: &str) -> bool {
    match (parse(current), parse(latest)) {
        (Some(c), Some(l)) => l > c,
        _ => false, // unparseable ⇒ never nag
    }
}

/// One downloadable asset attached to a release.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ReleaseAsset {
    pub name: String,
    /// `browser_download_url` — the direct, unauthenticated download link.
    pub url: String,
    /// Advertised byte size (0 if the API omitted it); used as a fail-closed integrity check.
    pub size: u64,
}

/// The latest release's version tag + its assets, or None on any failure (offline,
/// rate-limited, no releases yet). Bounded to a short timeout. One fetch feeds both the
/// notify pill (version only) and the self-update path (assets).
pub fn latest_release() -> Option<(String, Vec<ReleaseAsset>)> {
    let client = reqwest::blocking::Client::builder()
        .timeout(std::time::Duration::from_secs(8))
        .user_agent(concat!("cmprojectx/", env!("CARGO_PKG_VERSION")))
        .build()
        .ok()?;
    let resp = client.get(RELEASE_API).send().ok()?;
    if !resp.status().is_success() {
        return None;
    }
    let json: serde_json::Value = resp.json().ok()?;
    let version = json.get("tag_name")?.as_str()?.to_string();
    let assets = json
        .get("assets")
        .and_then(|a| a.as_array())
        .map(|arr| {
            arr.iter()
                .filter_map(|a| {
                    Some(ReleaseAsset {
                        name: a.get("name")?.as_str()?.to_string(),
                        url: a.get("browser_download_url")?.as_str()?.to_string(),
                        size: a.get("size").and_then(|s| s.as_u64()).unwrap_or(0),
                    })
                })
                .collect()
        })
        .unwrap_or_default();
    Some((version, assets))
}

/// The latest published release version, or None. (Kept for the notify-only pill.)
pub fn latest_release_version() -> Option<String> {
    latest_release().map(|(v, _)| v)
}

/// The newer version if an update is available, else None. Best-effort (network).
pub fn check_for_update() -> Option<String> {
    let latest = latest_release_version()?;
    update_available(CURRENT, &latest).then_some(latest)
}

/// This build's release-asset arch token — matches scripts/release.ps1's
/// `IntuneCommander-<ver>-win-<arch>.zip` naming. Pure.
pub fn arch_token() -> &'static str {
    match std::env::consts::ARCH {
        "aarch64" => "arm64",
        "x86_64" => "x64",
        other => other, // unknown arch → no asset will match (fail-closed)
    }
}

/// Choose the release bundle for `arch`: the `*-win-<arch>.zip` asset. Pure; unit-tested.
pub fn select_asset<'a>(assets: &'a [ReleaseAsset], arch: &str) -> Option<&'a ReleaseAsset> {
    let suffix = format!("-win-{arch}.zip").to_ascii_lowercase();
    assets.iter().find(|a| a.name.to_ascii_lowercase().ends_with(&suffix))
}

/// Hard ceiling on a self-update download — defends against a wrong/hostile asset. The
/// signed bundle (client + sidecar + offline PS-module cache) is a few hundred MB; 1 GiB
/// is comfortable headroom.
const MAX_DOWNLOAD_BYTES: u64 = 1_024 * 1_024 * 1_024;

/// Download `asset` (streaming) into `dest_dir`, returning the written file path. Fail-closed
/// integrity: rejects an HTTP error, an oversized advertised size, an empty body, or — when the
/// release advertised a size — a byte count that doesn't match. Effectful; longer timeout since
/// the bundle is large. Not exercised against a live signed feed (see module docs).
pub fn download_asset(asset: &ReleaseAsset, dest_dir: &Path) -> Result<PathBuf, String> {
    if asset.size > MAX_DOWNLOAD_BYTES {
        return Err(format!("advertised size {} exceeds ceiling", asset.size));
    }
    let client = reqwest::blocking::Client::builder()
        .timeout(std::time::Duration::from_secs(600))
        .user_agent(concat!("cmprojectx/", env!("CARGO_PKG_VERSION")))
        .build()
        .map_err(|e| e.to_string())?;
    let mut resp = client.get(&asset.url).send().map_err(|e| e.to_string())?;
    if !resp.status().is_success() {
        return Err(format!("download failed: HTTP {}", resp.status()));
    }
    std::fs::create_dir_all(dest_dir).map_err(|e| e.to_string())?;
    let path = dest_dir.join(&asset.name);
    let mut file = std::fs::File::create(&path).map_err(|e| e.to_string())?;
    let written = resp.copy_to(&mut file).map_err(|e| e.to_string())?;
    file.flush().ok();
    drop(file);
    if written == 0 || written > MAX_DOWNLOAD_BYTES {
        let _ = std::fs::remove_file(&path);
        return Err(format!("download produced {written} bytes"));
    }
    if asset.size != 0 && written != asset.size {
        let _ = std::fs::remove_file(&path);
        return Err(format!("size mismatch: got {written}, expected {}", asset.size));
    }
    Ok(path)
}

/// Generate the detached self-update helper (PowerShell) as a STRING so the swap logic is
/// unit-testable without running it. The helper: waits for `pid` to exit, extracts `zip`
/// into `stage_dir`, backs the current `install_dir` up to `<install>.bak`, copies the
/// bundle's single top-level folder over `install_dir`, then relaunches `client_exe`.
/// Fail-safe: the `.bak` is retained so a bad update stays recoverable by hand. Pure.
pub fn swap_script(pid: u32, zip: &Path, stage_dir: &Path, install_dir: &Path, client_exe: &Path) -> String {
    // Single-quoted PowerShell literals; embedded single quotes are doubled per PS rules.
    fn q(p: &Path) -> String {
        format!("'{}'", p.display().to_string().replace('\'', "''"))
    }
    format!(
        "$ErrorActionPreference = 'Stop'\n\
         try {{ Wait-Process -Id {pid} -Timeout 180 -ErrorAction SilentlyContinue }} catch {{}}\n\
         Start-Sleep -Milliseconds 800\n\
         $zip = {zip}\n\
         $stage = {stage}\n\
         $install = {install}\n\
         $client = {client}\n\
         if (Test-Path $stage) {{ Remove-Item $stage -Recurse -Force }}\n\
         Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force\n\
         $root = Get-ChildItem $stage -Directory | Select-Object -First 1\n\
         if (-not $root) {{ $root = Get-Item $stage }}\n\
         $backup = \"$install.bak\"\n\
         if (Test-Path $backup) {{ Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue }}\n\
         Copy-Item $install $backup -Recurse -Force\n\
         Copy-Item (Join-Path $root.FullName '*') $install -Recurse -Force\n\
         Start-Process -FilePath $client\n",
        pid = pid,
        zip = q(zip),
        stage = q(stage_dir),
        install = q(install_dir),
        client = q(client_exe),
    )
}

/// Spawn the PowerShell helper fully DETACHED so it outlives this process — the whole point,
/// since it must run after we exit to swap our (until-then locked) files.
#[cfg(windows)]
fn spawn_detached_helper(script: &Path) -> Result<(), String> {
    use std::os::windows::process::CommandExt;
    const DETACHED_PROCESS: u32 = 0x0000_0008;
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;
    const CREATE_NEW_PROCESS_GROUP: u32 = 0x0000_0200;
    std::process::Command::new("powershell.exe")
        .args(["-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File"])
        .arg(script)
        .creation_flags(DETACHED_PROCESS | CREATE_NO_WINDOW | CREATE_NEW_PROCESS_GROUP)
        .spawn()
        .map(|_| ())
        .map_err(|e| e.to_string())
}

#[cfg(not(windows))]
fn spawn_detached_helper(_script: &Path) -> Result<(), String> {
    Err("self-update is Windows-only".into())
}

/// Human-triggered self-update: resolve this arch's bundle from the latest release,
/// download + verify it, write the swap helper, and spawn it DETACHED. On `Ok(())` the
/// caller MUST exit the process promptly so the helper can replace the now-unlocked files;
/// the helper relaunches the new build. Never call this automatically. Effectful; the swap
/// itself runs after we exit (see module docs — unexercised against a live signed feed).
pub fn apply_update() -> Result<(), String> {
    let (version, assets) = latest_release().ok_or("no release metadata")?;
    if !update_available(CURRENT, &version) {
        return Err(format!("already up to date ({CURRENT})"));
    }
    let arch = arch_token();
    let asset = select_asset(&assets, arch).ok_or_else(|| format!("no {arch} bundle in release {version}"))?;

    let work = std::env::temp_dir().join("cmprojectx-update");
    let zip = download_asset(asset, &work)?;
    let exe = std::env::current_exe().map_err(|e| e.to_string())?;
    let install = exe.parent().ok_or("no install directory")?.to_path_buf();
    let stage = work.join("stage");
    let helper = work.join("apply-update.ps1");
    let script = swap_script(std::process::id(), &zip, &stage, &install, &exe);
    std::fs::write(&helper, script).map_err(|e| e.to_string())?;
    spawn_detached_helper(&helper)
}

#[cfg(test)]
mod tests {
    use super::{arch_token, select_asset, swap_script, update_available, ReleaseAsset};
    use std::path::Path;

    #[test]
    fn detects_newer() {
        assert!(update_available("0.1.0", "0.1.1")); // patch
        assert!(update_available("0.1.9", "0.2.0")); // minor
        assert!(update_available("0.9.9", "1.0.0")); // major
        assert!(update_available("v0.1.0", "v0.2.0")); // v-prefix both sides
        assert!(update_available("0.1.0", "0.2.0-beta")); // pre-release suffix ignored
    }

    #[test]
    fn ignores_same_or_older() {
        assert!(!update_available("0.2.0", "0.2.0"));
        assert!(!update_available("0.2.0", "0.1.9"));
        assert!(!update_available("1.0.0", "0.9.9"));
    }

    #[test]
    fn unparseable_never_nags() {
        assert!(!update_available("0.1.0", "nightly"));
        assert!(!update_available("not-a-version", "0.2.0"));
    }

    // ── self-update (M12 increment 2) — pure pieces ──

    #[test]
    fn arch_token_is_known_on_this_host() {
        // On the arm64/x64 dev + CI hosts the token is one of the two release arches.
        assert!(matches!(arch_token(), "arm64" | "x64"));
    }

    #[test]
    fn selects_the_matching_arch_bundle() {
        let assets = vec![
            ReleaseAsset { name: "IntuneCommander-0.2.0-win-x64.zip".into(), url: "u-x64".into(), size: 10 },
            ReleaseAsset { name: "IntuneCommander-0.2.0-win-arm64.zip".into(), url: "u-arm64".into(), size: 20 },
            ReleaseAsset { name: "IntuneCommander-0.2.0-notes.txt".into(), url: "u-txt".into(), size: 1 },
        ];
        assert_eq!(select_asset(&assets, "arm64").unwrap().url, "u-arm64");
        assert_eq!(select_asset(&assets, "x64").unwrap().url, "u-x64");
        // No bundle for an arch we didn't publish → None (fail-closed, no wrong download).
        assert!(select_asset(&assets, "x86").is_none());
        assert!(select_asset(&[], "arm64").is_none());
    }

    #[test]
    fn swap_script_waits_extracts_backs_up_and_relaunches() {
        let s = swap_script(
            4242,
            Path::new(r"C:\tmp\u\IntuneCommander-0.2.0-win-arm64.zip"),
            Path::new(r"C:\tmp\u\stage"),
            Path::new(r"C:\Program Files\IntuneCommander"),
            Path::new(r"C:\Program Files\IntuneCommander\IntuneCommander.exe"),
        );
        assert!(s.contains("Wait-Process -Id 4242"), "must wait for the app to exit first");
        assert!(s.contains("Expand-Archive"), "must extract the downloaded bundle");
        assert!(s.contains("$install.bak"), "must back up the current install (recoverable)");
        assert!(s.contains("Copy-Item $install $backup"), "backup precedes overwrite");
        assert!(s.contains("Start-Process -FilePath"), "must relaunch the new build");
        assert!(s.contains("IntuneCommander-0.2.0-win-arm64.zip"), "references the downloaded zip");
    }

    #[test]
    fn swap_script_escapes_single_quotes_in_paths() {
        // A path containing a single quote must be doubled so the PS literal stays valid.
        let s = swap_script(
            1,
            Path::new(r"C:\o'brien\u.zip"),
            Path::new(r"C:\o'brien\stage"),
            Path::new(r"C:\o'brien\app"),
            Path::new(r"C:\o'brien\app\IntuneCommander.exe"),
        );
        assert!(s.contains("'C:\\o''brien\\u.zip'"), "single quote doubled in PS literal");
    }
}
