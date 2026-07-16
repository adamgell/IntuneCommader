//! Diagnostics: Secure Boot Certs — a LOCAL Windows diagnostics workspace.
//!
//! Pure Rust, no sidecar, no auth (mirrors the dsregcmd / registry diagnostics
//! workspaces). Shells `powershell -NoProfile` on Reactor's background fetcher
//! thread to gather Secure Boot facts: the enabled state via
//! `Confirm-SecureBootUEFI`, and the presence + byte-size of the UEFI signature
//! databases (db / KEK / PK) via `Get-SecureBootUEFI`. Best-effort: extracts
//! X.509 subject strings from the `db` variable when feasible. The PowerShell
//! script emits a single JSON object (ConvertTo-Json of a hashtable) that we
//! parse here. Degrades gracefully on non-UEFI / unsupported boxes — never
//! panics. A use_reducer "Re-run" button re-keys the resource to re-scan.

use windows_reactor::*;

// A comparable projection of the gathered Secure Boot facts. `use_resource`
// requires `PartialEq`, so we map straight into this lean, owned type.
#[derive(Clone, PartialEq)]
struct SecureBootView {
    // None => the box is non-UEFI / Secure Boot state could not be determined.
    enabled: Option<bool>,
    // Raw label for the state when `enabled` is None (e.g. "Unsupported/Unavailable").
    state_label: String,
    // (variable name, byte size or -1 when absent/inaccessible).
    sizes: Vec<(String, i64)>,
    // Best-effort X.509 subject strings extracted from the `db` variable.
    subjects: Vec<String>,
}

// PowerShell script: gather Secure Boot facts and emit one JSON object. Every
// probe is wrapped in try/catch so a non-UEFI or locked-down box yields sane
// sentinels ('Unsupported/Unavailable', -1) instead of a hard failure.
const PS_SCRIPT: &str = r#"
$enabled = try { (Confirm-SecureBootUEFI).ToString() } catch { 'Unsupported/Unavailable' }
$db  = try { (Get-SecureBootUEFI db).bytes.Length }  catch { -1 }
$kek = try { (Get-SecureBootUEFI KEK).bytes.Length } catch { -1 }
$pk  = try { (Get-SecureBootUEFI PK).bytes.Length }  catch { -1 }
$subjects = @()
try {
  $bytes = (Get-SecureBootUEFI db).bytes
  $text = [System.Text.Encoding]::ASCII.GetString($bytes)
  $subjects = [regex]::Matches($text, 'CN=[^,\x00]{1,128}') |
    ForEach-Object { $_.Value } | Select-Object -Unique
} catch { $subjects = @() }
ConvertTo-Json -Compress @{ enabled = $enabled; db = $db; kek = $kek; pk = $pk; subjects = @($subjects) }
"#;

fn run_secureboot() -> std::result::Result<SecureBootView, String> {
    let out = std::process::Command::new("powershell")
        .args(["-NoProfile", "-NonInteractive", "-Command", PS_SCRIPT])
        .output()
        .map_err(|e| format!("couldn't run powershell: {e}"))?;
    let text = String::from_utf8_lossy(&out.stdout);
    let trimmed = text.trim();
    if trimmed.is_empty() {
        let err = String::from_utf8_lossy(&out.stderr);
        return Err(format!(
            "powershell returned no output{}",
            if err.trim().is_empty() {
                String::new()
            } else {
                format!(": {}", err.trim())
            }
        ));
    }
    let json: serde_json::Value = serde_json::from_str(trimmed)
        .map_err(|e| format!("couldn't parse Secure Boot data: {e}"))?;

    let state_label = json
        .get("enabled")
        .and_then(|v| v.as_str())
        .unwrap_or("Unknown")
        .to_string();
    let enabled = match state_label.to_ascii_lowercase().as_str() {
        "true" => Some(true),
        "false" => Some(false),
        _ => None,
    };

    let size_of = |key: &str| -> i64 {
        json.get(key).and_then(|v| v.as_i64()).unwrap_or(-1)
    };
    let sizes = vec![
        ("db (signature database)".to_string(), size_of("db")),
        ("KEK (key exchange keys)".to_string(), size_of("kek")),
        ("PK (platform key)".to_string(), size_of("pk")),
    ];

    let subjects: Vec<String> = match json.get("subjects") {
        // ConvertTo-Json collapses a single-element array to a scalar string.
        Some(serde_json::Value::Array(arr)) => arr
            .iter()
            .filter_map(|v| v.as_str().map(|s| s.to_string()))
            .collect(),
        Some(serde_json::Value::String(s)) => vec![s.clone()],
        _ => Vec::new(),
    };

    Ok(SecureBootView {
        enabled,
        state_label,
        sizes,
        subjects,
    })
}

fn size_label(bytes: i64) -> String {
    if bytes < 0 {
        "absent / inaccessible".to_string()
    } else {
        format!("{bytes} bytes")
    }
}

pub fn secureboot_workspace(_: &(), cx: &mut RenderCx) -> Element {
    let (run, bump) = cx.use_reducer(0_u64);
    let result = cx.use_resource(|_: u64| run_secureboot(), run);

    let body_el: Element = result
        .view(move |v: &SecureBootView| -> Element {
            // Prominent colored header reflecting the Secure Boot state.
            let (head_text, head_color) = match v.enabled {
                Some(true) => (
                    "Secure Boot: Enabled".to_string(),
                    crate::theme::OK,
                ),
                Some(false) => (
                    "Secure Boot: Disabled".to_string(),
                    crate::theme::SEV_WARN,
                ),
                None => (
                    format!("Secure Boot: {}", v.state_label),
                    crate::theme::SEV_ERROR,
                ),
            };
            let header: Element = border(
                body_strong(head_text)
                    .font_size(18.0)
                    .foreground(head_color),
            )
            .corner_radius(4.0)
            .padding(Thickness::uniform(12.0))
            .into();

            // Fact rows for the UEFI signature-database sizes.
            let size_rows: Vec<Element> = v
                .sizes
                .iter()
                .map(|(k, bytes)| {
                    hstack((
                        caption(format!("{k}:")).opacity(0.6),
                        body(size_label(*bytes)),
                    ))
                    .spacing(8.0)
                    .into()
                })
                .collect();

            // Best-effort certificate subjects (graceful when none found).
            let subjects_box: Element = if v.subjects.is_empty() {
                caption("No certificate subjects extracted from the db variable.")
                    .opacity(0.6)
                    .wrap()
                    .into()
            } else {
                let subj_rows: Vec<Element> = v
                    .subjects
                    .iter()
                    .map(|s| {
                        body(crate::truncate(s, 160))
                            .font_family("Consolas")
                            .opacity(0.9)
                            .wrap()
                            .into()
                    })
                    .collect();
                vstack(subj_rows).spacing(4.0).into()
            };

            vstack((
                header,
                body_strong("UEFI signature databases"),
                vstack(size_rows).spacing(4.0),
                body_strong("Certificate subjects (db, best-effort)"),
                subjects_box,
            ))
            .spacing(12.0)
            .into()
        })
        .loading(caption("querying Secure Boot state…").opacity(0.6))
        .error(|e| crate::error_box(e))
        .into();

    vstack((
        Element::from(button("Re-run").on_click(move || bump.call(|n| n + 1))),
        body_el,
    ))
    .spacing(12.0)
    .margin(Thickness::uniform(16.0))
    .into()
}
