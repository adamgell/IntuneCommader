//! Flat, dependency-free catalog of known log sources.
//!
//! Ported from cmtraceopen
//! (`src-tauri/src/commands/known_sources.rs`). The original builds a richer
//! `KnownSourceMetadata` model wired to cmtrace's `LogSource` types; this is a
//! flattened static table the cmProjectX client can consume without pulling in
//! any cmtrace types. `family` maps to the source's `group_label`, `path` to the
//! `default_path`, and `folder` to whether the path kind is `Folder`.
//!
//! Credit: cmtraceopen known-log-source catalog.

/// A single known log source entry.
pub struct KnownSource {
    /// Grouping label (the original `group_label`), e.g. "Intune IME".
    pub family: &'static str,
    /// Human-readable entry label. Carried for future source-picker tooltips.
    #[allow(dead_code)]
    pub label: &'static str,
    /// Concise description of what the source contains. Carried for tooltips.
    #[allow(dead_code)]
    pub description: &'static str,
    /// Default path on disk (raw Windows/Unix path).
    pub path: &'static str,
    /// `true` if the path points at a folder, `false` if a single file.
    pub folder: bool,
}

/// All known log sources, grouped and ordered as in the cmtraceopen source.
pub static KNOWN_SOURCES: &[KnownSource] = &[
    // ── Intune IME ──────────────────────────────────────────────────────
    KnownSource {
        family: "Intune IME",
        label: "Intune IME Logs Folder",
        description:
            "Known log source for Intune Management Extension (IME) app and script diagnostics.",
        path: r"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs",
        folder: true,
    },
    KnownSource {
        family: "Intune IME",
        label: "Intune IME: IntuneManagementExtension.log",
        description: "Primary IME log for check-ins, policy processing, and app orchestration.",
        path: r"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\IntuneManagementExtension.log",
        folder: false,
    },
    KnownSource {
        family: "Intune IME",
        label: "Intune IME: AppWorkload.log",
        description: "Win32 and WinGet app download/staging/install diagnostics.",
        path: r"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\AppWorkload.log",
        folder: false,
    },
    KnownSource {
        family: "Intune IME",
        label: "Intune IME: AgentExecutor.log",
        description: "Script execution and remediation output with exit code tracking.",
        path: r"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\AgentExecutor.log",
        folder: false,
    },
    // ── MDM and Enrollment ──────────────────────────────────────────────
    KnownSource {
        family: "MDM and Enrollment",
        label: "DMClient Local Logs",
        description: "MDM DMClient log folder used for local sync diagnostics.",
        path: r"C:\Windows\System32\config\systemprofile\AppData\Local\mdm",
        folder: true,
    },
    // ── ConfigMgr Logs ──────────────────────────────────────────────────
    KnownSource {
        family: "ConfigMgr Logs",
        label: "CCM Logs Folder",
        description:
            "ConfigMgr client operational logs (policy, inventory, software distribution).",
        path: r"C:\Windows\CCM\Logs",
        folder: true,
    },
    KnownSource {
        family: "ConfigMgr Logs",
        label: "ccmsetup Logs Folder",
        description: "ConfigMgr client installation and setup logs.",
        path: r"C:\Windows\ccmsetup\Logs",
        folder: true,
    },
    KnownSource {
        family: "ConfigMgr Logs",
        label: "Software Metering Logs",
        description: "ConfigMgr software metering usage reporting data.",
        path: r"C:\Windows\System32\SWMTRReporting",
        folder: true,
    },
    // ── Panther ─────────────────────────────────────────────────────────
    KnownSource {
        family: "Panther",
        label: "setupact.log (Panther)",
        description: "Primary Windows setup and Autopilot/OOBE action log.",
        path: r"C:\Windows\Panther\setupact.log",
        folder: false,
    },
    KnownSource {
        family: "Panther",
        label: "setuperr.log (Panther)",
        description: "Error-focused Windows setup and Autopilot/OOBE triage log.",
        path: r"C:\Windows\Panther\setuperr.log",
        folder: false,
    },
    // ── CBS and DISM ────────────────────────────────────────────────────
    KnownSource {
        family: "CBS and DISM",
        label: "CBS.log",
        description: "Component-Based Servicing log for update and servicing failures.",
        path: r"C:\Windows\Logs\CBS\CBS.log",
        folder: false,
    },
    KnownSource {
        family: "CBS and DISM",
        label: "DISM.log",
        description: "Deployment Image Servicing and Management diagnostics log.",
        path: r"C:\Windows\Logs\DISM\dism.log",
        folder: false,
    },
    // ── Windows Update ──────────────────────────────────────────────────
    KnownSource {
        family: "Windows Update",
        label: "ReportingEvents.log",
        description: "Windows Update transaction history in tab-delimited text.",
        path: r"C:\Windows\SoftwareDistribution\ReportingEvents.log",
        folder: false,
    },
    // ── W3C Logs ────────────────────────────────────────────────────────
    KnownSource {
        family: "W3C Logs",
        label: "IIS Logs",
        description: "IIS W3C extended log folder (W3SVC*) under inetpub log files.",
        path: r"C:\inetpub\logs\LogFiles",
        folder: true,
    },
    // ── Deployment Logs ─────────────────────────────────────────────────
    KnownSource {
        family: "Deployment Logs",
        label: "Software Logs Folder",
        description:
            "Common deployment log output folder used by PSADT, SCCM, and custom installers.",
        path: r"C:\Windows\Logs\Software",
        folder: true,
    },
    KnownSource {
        family: "Deployment Logs",
        label: "ccmcache Folder",
        description: "ConfigMgr client cache folder where packages and scripts are staged.",
        path: r"C:\Windows\ccmcache",
        folder: true,
    },
    // ── PSADT ───────────────────────────────────────────────────────────
    KnownSource {
        family: "PSADT",
        label: "PSADT Logs Folder",
        description: "Default PSAppDeployToolkit log output directory.",
        path: r"C:\Windows\Logs\Software",
        folder: true,
    },
    // ── MSI Logs ────────────────────────────────────────────────────────
    KnownSource {
        family: "MSI Logs",
        label: "MSI Verbose Log Folder",
        description: "Default location for MSI verbose install logs (%TEMP%).",
        path: r"C:\Windows\Temp",
        folder: true,
    },
    // ── PatchMyPC ───────────────────────────────────────────────────────
    KnownSource {
        family: "PatchMyPC",
        label: "PatchMyPC Logs Folder",
        description: "PatchMyPC client and notification logs (CMTrace format).",
        path: r"C:\ProgramData\PatchMyPC\Logs",
        folder: true,
    },
    KnownSource {
        family: "PatchMyPC",
        label: "PatchMyPC Install Logs",
        description:
            "MSI verbose and WiX/Burn bootstrapper logs from PatchMyPC-managed installations.",
        path: r"C:\ProgramData\PatchMyPCInstallLogs",
        folder: true,
    },
    KnownSource {
        family: "PatchMyPC",
        label: "PatchMyPC Intune Logs",
        description: "PatchMyPC ScriptRunner and software detection/requirement script logs.",
        path: r"C:\ProgramData\PatchMyPCIntuneLogs",
        folder: true,
    },
    // ── Intune Logs (macOS) ─────────────────────────────────────────────
    KnownSource {
        family: "Intune Logs",
        label: "Intune System Logs",
        description:
            "System-level MDM daemon logs for PKG/DMG installs and root script execution.",
        path: r"/Library/Logs/Microsoft/Intune",
        folder: true,
    },
    KnownSource {
        family: "Intune Logs",
        label: "Intune User Agent Logs",
        description: "User-level MDM agent logs for user-context scripts and policies.",
        // Original uses `{HOME}/Library/Logs/Microsoft/Intune`; HOME is resolved
        // at runtime in cmtraceopen. Inferred a representative absolute path here.
        path: r"~/Library/Logs/Microsoft/Intune",
        folder: true,
    },
    KnownSource {
        family: "Intune Logs",
        label: "Intune Script Logs",
        description: "Shell script execution logs from Intune script deployments.",
        path: r"/Library/Logs/Microsoft/IntuneScripts",
        folder: true,
    },
    // ── Company Portal (macOS) ──────────────────────────────────────────
    KnownSource {
        family: "Company Portal",
        label: "Company Portal Logs",
        description:
            "Company Portal app logs for enrollment, device info, and user registration.",
        // Original uses `{HOME}/Library/Logs/CompanyPortal`; HOME is resolved at
        // runtime in cmtraceopen. Inferred a representative absolute path here.
        path: r"~/Library/Logs/CompanyPortal",
        folder: true,
    },
    // ── System Logs (macOS) ─────────────────────────────────────────────
    KnownSource {
        family: "System Logs",
        label: "install.log",
        description:
            "macOS installer log — PKG installs from Intune and Software Update show up here.",
        path: r"/var/log/install.log",
        folder: false,
    },
    KnownSource {
        family: "System Logs",
        label: "system.log",
        description:
            "macOS system log — MDM profile installs, daemon crashes, and system events.",
        path: r"/var/log/system.log",
        folder: false,
    },
    KnownSource {
        family: "System Logs",
        label: "Wi-Fi Log",
        description: "macOS Wi-Fi diagnostic log",
        path: r"/var/log/wifi.log",
        folder: false,
    },
    KnownSource {
        family: "System Logs",
        label: "Application Firewall Log",
        description: "macOS application firewall log",
        path: r"/var/log/appfirewall.log",
        folder: false,
    },
    // ── Defender Logs (macOS) ───────────────────────────────────────────
    KnownSource {
        family: "Defender Logs",
        label: "Defender Logs",
        description: "Microsoft Defender for Endpoint install and error logs.",
        path: r"/Library/Logs/Microsoft/mdatp",
        folder: true,
    },
];
