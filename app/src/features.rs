//! Feature registry — the single source of truth for the unified left-nav and
//! workspace dispatch. Every screen in the app (Intune management features ported
//! from IntuneCommander + cmtrace diagnostics from cmtraceopen) is one row here.
//! `app()` in `main.rs` folds this into a grouped `NavigationView` and routes the
//! workspace from `Feature::kind`, so adding a feature is a one-line table edit
//! plus (for a LIVE list) one match arm in `list_workspace`.

use windows_reactor::Symbol;

/// Top-level collapsible nav sections, in display order.
#[derive(Clone, Copy, PartialEq, Eq)]
pub enum Section {
    Overview,
    Compliance,
    Devices,
    Apps,
    Enrollment,
    Identity,
    TenantAdmin,
    GroupsMon,
    DriftSearch,
    Diagnostics,
}

impl Section {
    pub fn all() -> &'static [Section] {
        use Section::*;
        &[
            Overview, Compliance, Devices, Apps, Enrollment, Identity, TenantAdmin, GroupsMon,
            DriftSearch, Diagnostics,
        ]
    }

    pub fn title(self) -> &'static str {
        use Section::*;
        match self {
            Overview => "Overview",
            Compliance => "Security & Compliance",
            Devices => "Devices",
            Apps => "Apps",
            Enrollment => "Enrollment",
            Identity => "Identity & Access",
            TenantAdmin => "Tenant Admin",
            GroupsMon => "Groups & Monitoring",
            DriftSearch => "Drift & Compare",
            Diagnostics => "Diagnostics",
        }
    }

    pub fn icon(self) -> Symbol {
        use Section::*;
        match self {
            Overview => Symbol::World,
            Compliance => Symbol::Find,
            Devices => Symbol::Setting,
            Apps => Symbol::Download,
            Enrollment => Symbol::Add,
            Identity => Symbol::People,
            TenantAdmin => Symbol::Favorite,
            GroupsMon => Symbol::More,
            DriftSearch => Symbol::Find,
            Diagnostics => Symbol::Play,
        }
    }

    /// The 50/50 brand motif (BRAND.md §0): `≣` line = diagnostics (cmtrace
    /// heritage); `▣` node = management (IntuneCommander heritage). Carried by
    /// FORM, not color — prefixes the section label in the nav.
    pub fn motif(self) -> &'static str {
        match self {
            Section::Diagnostics => "≣",
            _ => "▣",
        }
    }
}

/// The hand-built built-in screens (the original four + the M13.2 AI approval inbox).
#[derive(Clone, Copy)]
pub enum Builtin {
    Timeline,
    Drift,
    Search,
    Logs,
    PendingChanges,
}

/// How a feature's workspace is rendered.
#[derive(Clone, Copy)]
pub enum Screen {
    /// One of the original hand-built workspaces.
    Builtin(Builtin),
    /// A live Graph-backed master/detail list with view/edit/create/delete. The
    /// `&str` is the sidecar path (e.g. "/scope-tags"); `bool` = writable (false =
    /// read-only surface, view only).
    List(&'static str, bool),
    /// A local parser-backed diagnostics workspace (cmtraceopen-parser), dispatched
    /// by kind (e.g. "dsregcmd", "error-db"). No sidecar / no auth.
    Diag(&'static str),
    /// A read-only metric-tile grid backed by a sidecar path returning a
    /// `Vec<ListItem>` (title = metric, subtitle = description, badge = count).
    /// Dashboard / Security Posture.
    Tiles(&'static str),
    /// A bulk/lifecycle workspace (M8) dispatched by kind ("export", "import").
    /// Backed by the sidecar's Export/Import engines.
    Bulk(&'static str),
    /// The scored Security Posture dashboard (/security-posture/summary).
    Posture,
    /// The dedicated Conditional Access grid + full-detail workspace.
    Ca,
    /// The dedicated Managed Devices workspace (inventory detail + M14 action UI).
    /// The `&str` is the sidecar list path ("/managed-devices").
    Devices(&'static str),
    /// The dedicated Groups workspace (member table + reverse properties panel).
    Groups,
    /// The Maester security-controls dashboard (/maester/*). The `&str` is a category
    /// filter ("" = all categories; "Entra"/"Intune"/… = that service only).
    Maester(&'static str),
    /// The Assignment Explorer workspace — the IntuneAssignmentChecker report in
    /// every mode (all / all-users / all-devices / unassigned / empty-groups /
    /// failed / group) + HTML/CSV export over /assignment-explorer[/report].
    AssignmentExplorer,
    /// The dedicated assignments-first Applications workspace — an app picker that
    /// opens the intent-aware `assignment_editor` directly (no object-detail chrome).
    AppAssignments,
    /// The M12.1 cache-dev (Cache Sync) workspace — inspect/warm/evict the LiteDB
    /// read-through blob cache for the active tenant.
    Cache,
    /// The M15 Policy-as-Code (GitOps) workspace — pull/plan/apply a repo tree
    /// against the live tenant over the sidecar's /gitops/* routes.
    GitOps,
    /// The M16 Foresight blast-radius simulator — a proposed-write form over
    /// POST /simulate returning a BlastRadiusReport card (who does this hit?).
    Simulate,
    /// The M17 Tenant Digital Twin — offline graph analytics (findings) + node
    /// neighborhoods over /twin/*. Read-only.
    Twin,
    /// The M18 Autonomy (AI SRE) workspace — per-tenant policy editor + the
    /// closed-loop run log over /autonomy/*. Proposals gate through the M13 inbox.
    Autonomy,
    /// The M19 continuous-posture workspace — benchmark score + trend + POA&M +
    /// evidence-pack export over /posture/*.
    PostureTrend,
    /// The M20 Fleet workspace — MSP-scale multi-tenant fan-out LIST + a dry-run
    /// campaign panel over /fleet/*.
    Fleet,
    /// The M21 Ecosystem workspace — packs / playbooks / marketplace catalogs with
    /// adopt (plan) / run / install actions over /packs, /playbooks, /marketplace.
    Ecosystem,
    /// The Policy Comparison workspace — a SettingsCatalog baseline vs a tenant policy
    /// over POST /baselines/compare, grouped by verdict.
    Compare,
    /// The Bulk App Assignment workspace — a multi-select app picker + intent-aware
    /// assignment builder over POST /apps/assign (dry-run by default).
    BulkAssign,
    /// The Detection & Remediation workspace — proactive-remediation run summary +
    /// per-device state + a gated on-demand run over /remediation-scripts/{id}/*.
    DetectionRemediation,
    /// Scaffolded placeholder ("coming soon") — nav-present, no backend yet.
    Stub,
}

pub struct Feature {
    pub tag: &'static str,
    pub title: &'static str,
    pub section: Section,
    pub kind: Screen,
}

const fn builtin(tag: &'static str, title: &'static str, section: Section, b: Builtin) -> Feature {
    Feature { tag, title, section, kind: Screen::Builtin(b) }
}
// `path` = sidecar route (e.g. "/scope-tags"); `rw` = writable (full CRUD) vs read-only.
const fn list(
    tag: &'static str,
    title: &'static str,
    section: Section,
    path: &'static str,
    rw: bool,
) -> Feature {
    Feature { tag, title, section, kind: Screen::List(path, rw) }
}
const fn stub(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Stub }
}
const fn diag(tag: &'static str, title: &'static str, section: Section, kind: &'static str) -> Feature {
    Feature { tag, title, section, kind: Screen::Diag(kind) }
}
// `path` = sidecar route returning Vec<ListItem> metric tiles (e.g. "/dashboard").
const fn tiles(tag: &'static str, title: &'static str, section: Section, path: &'static str) -> Feature {
    Feature { tag, title, section, kind: Screen::Tiles(path) }
}
// `kind` = bulk workspace dispatch key ("export" | "import").
const fn bulk(tag: &'static str, title: &'static str, section: Section, kind: &'static str) -> Feature {
    Feature { tag, title, section, kind: Screen::Bulk(kind) }
}
const fn posture(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Posture }
}
const fn ca(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Ca }
}
// `path` = sidecar list route ("/managed-devices") for the dedicated detail+action screen.
const fn devices(
    tag: &'static str,
    title: &'static str,
    section: Section,
    path: &'static str,
) -> Feature {
    Feature { tag, title, section, kind: Screen::Devices(path) }
}
const fn groups_screen(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Groups }
}
// `filter` = Maester category to show ("" = all; "Entra"/"Intune"/… = that service).
const fn maester(tag: &'static str, title: &'static str, section: Section, filter: &'static str) -> Feature {
    Feature { tag, title, section, kind: Screen::Maester(filter) }
}
const fn assignment_explorer(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::AssignmentExplorer }
}
const fn app_assignments(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::AppAssignments }
}
const fn cache(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Cache }
}
const fn gitops(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::GitOps }
}
const fn simulate(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Simulate }
}
const fn twin(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Twin }
}
const fn autonomy(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Autonomy }
}
const fn posture_trend(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::PostureTrend }
}
const fn fleet(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Fleet }
}
const fn ecosystem(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Ecosystem }
}
const fn compare(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::Compare }
}
const fn bulk_assign(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::BulkAssign }
}
const fn detection(tag: &'static str, title: &'static str, section: Section) -> Feature {
    Feature { tag, title, section, kind: Screen::DetectionRemediation }
}

pub fn find(tag: &str) -> Option<&'static Feature> {
    FEATURES.iter().find(|f| f.tag == tag)
}

use Section::*;

/// The full unified catalog. LIVE features carry `list(..)`; everything else is a
/// `stub(..)` scaffold until its slice lands (see plan milestones M2–M6).
pub static FEATURES: &[Feature] = &[
    // ─ Overview ────────────────────────────────────────────────────────────
    builtin("timeline", "Audit Timeline", Overview, Builtin::Timeline),
    builtin("search", "Global Search", Overview, Builtin::Search),
    builtin("pending-ai", "Pending AI Changes", Overview, Builtin::PendingChanges),
    tiles("dashboard", "Dashboard", Overview, "/dashboard"),
    posture("security-posture", "Security Posture", Overview),
    // ─ Security & Compliance (Maester checks: CIS / CISA SCuBA / EIDSCA) ──────
    maester("maester-controls", "All Controls", Compliance, ""),
    maester("maester-entra", "Entra Controls", Compliance, "Entra"),
    maester("maester-intune", "Intune Controls", Compliance, "Intune"),
    maester("maester-exchange", "Exchange Controls", Compliance, "Exchange"),
    maester("maester-defender", "Defender Controls", Compliance, "Defender"),
    maester("maester-teams", "Teams Controls", Compliance, "Teams"),
    // M19 continuous posture: benchmark score + trend + POA&M + evidence over /posture/*.
    posture_trend("posture-trend", "Posture Trend", Overview),
    // ─ Devices (full CRUD except managed-devices) ──────────────────────────
    list("device-configs", "Device Configurations", Devices, "/device-configs", true),
    list("compliance-policies", "Compliance Policies", Devices, "/compliance-policies", true),
    list("settings-catalog", "Settings Catalog", Devices, "/settings-catalog", true),
    list("admin-templates", "Administrative Templates", Devices, "/admin-templates", true),
    list("endpoint-security", "Endpoint Security", Devices, "/endpoint-security", true),
    list("device-categories", "Device Categories", Devices, "/device-categories", true),
    list("remediation-scripts", "Remediation Scripts", Devices, "/remediation-scripts", true),
    list("compliance-scripts", "Compliance Scripts", Devices, "/compliance-scripts", true),
    list("feature-updates", "Feature Updates", Devices, "/feature-updates", true),
    list("quality-updates", "Quality Updates", Devices, "/quality-updates", true),
    list("driver-updates", "Driver Updates", Devices, "/driver-updates", true),
    list("platform-scripts", "Device Management Scripts", Devices, "/platform-scripts", true),
    list("shell-scripts", "Shell Scripts (macOS)", Devices, "/shell-scripts", true),
    list("mac-custom-attributes", "Custom Attributes (macOS)", Devices, "/mac-custom-attributes", true),
    devices("managed-devices", "Managed Devices", Devices, "/managed-devices"),
    // ─ Apps ────────────────────────────────────────────────────────────────
    list("apps", "Applications", Apps, "/apps", false),
    app_assignments("app-assignments", "Application Assignments", Apps),
    list("app-protection", "App Protection Policies", Apps, "/app-protection", true),
    list("app-configs", "App Configurations", Apps, "/app-configs", true),
    list("vpp-tokens", "VPP Tokens", Apps, "/vpp-tokens", false),
    bulk_assign("bulk-assign", "Bulk App Assignment", Apps),
    // ─ Enrollment ──────────────────────────────────────────────────────────
    list("enrollment-configs", "Enrollment Configurations", Enrollment, "/enrollment-configs", true),
    list("autopilot", "Autopilot Profiles", Enrollment, "/autopilot", true),
    list("apple-dep", "Apple DEP", Enrollment, "/apple-dep", false),
    list("cloudpc-provisioning", "Cloud PC Provisioning", Enrollment, "/cloudpc-provisioning", false),
    // ─ Identity & Access ───────────────────────────────────────────────────
    ca("conditional-access", "Conditional Access", Identity),
    list("named-locations", "Named Locations", Identity, "/named-locations", true),
    list("auth-strengths", "Authentication Strengths", Identity, "/auth-strengths", true),
    list("auth-contexts", "Authentication Contexts", Identity, "/auth-contexts", true),
    list("terms-of-use", "Terms of Use", Identity, "/terms-of-use", true),
    bulk("ca-pptx", "CA → PowerPoint", Identity, "ca-pptx"),
    // ─ Tenant Admin ────────────────────────────────────────────────────────
    list("scope-tags", "Scope Tags", TenantAdmin, "/scope-tags", true),
    list("role-definitions", "Role Definitions", TenantAdmin, "/role-definitions", true),
    list("role-assignments", "Role Assignments", TenantAdmin, "/role-assignments", false),
    list("assignment-filters", "Assignment Filters", TenantAdmin, "/assignment-filters", false),
    list("policy-sets", "Policy Sets", TenantAdmin, "/policy-sets", false),
    list("intune-branding", "Intune Branding", TenantAdmin, "/intune-branding", true),
    list("azure-branding", "Azure Branding", TenantAdmin, "/azure-branding", true),
    list("terms-conditions", "Terms & Conditions", TenantAdmin, "/terms-conditions", true),
    list("cloudpc-user-settings", "Cloud PC User Settings", TenantAdmin, "/cloudpc-user-settings", false),
    list("admx-files", "ADMX Files", TenantAdmin, "/admx-files", true),
    list("reusable-settings", "Reusable Policy Settings", TenantAdmin, "/reusable-settings", true),
    list("notification-templates", "Notification Templates", TenantAdmin, "/notification-templates", true),
    // M21 Ecosystem: packs / playbooks / marketplace over /packs, /playbooks, /marketplace.
    ecosystem("ecosystem", "Ecosystem", TenantAdmin),
    // ─ Groups & Monitoring ─────────────────────────────────────────────────
    groups_screen("groups", "Groups", GroupsMon),
    list("permission-check", "Permission Check", GroupsMon, "/permission-check", false),
    // ─ Drift & Compare ─────────────────────────────────────────────────────
    builtin("drift", "Drift Detection", DriftSearch, Builtin::Drift),
    compare("policy-comparison", "Policy Comparison", DriftSearch),
    assignment_explorer("assignment-explorer", "Assignment Explorer", DriftSearch),
    tiles("baseline-compare", "Security Baselines", DriftSearch, "/baselines"),
    bulk("export", "Backup / Export", DriftSearch, "export"),
    bulk("import", "Restore / Import", DriftSearch, "import"),
    // M15 Policy-as-Code: pull → plan → apply over the sidecar's /gitops/* routes
    // (ApiClient::gitops_*). Two-pane git-mirror UI — repo picker + change-set that
    // reuses the M6 drift_row diff panel, gated by an inline apply confirm.
    gitops("gitops", "Policy as Code (GitOps)", DriftSearch),
    // M16 Foresight: pre-flight a proposed write's blast radius over POST /simulate.
    simulate("simulator", "Blast-Radius Simulator", DriftSearch),
    // M17 Tenant Digital Twin: offline graph analytics over /twin/*.
    twin("twin", "Tenant Digital Twin", DriftSearch),
    // M18 Autonomy (AI SRE): per-tenant policy + closed-loop run log over /autonomy/*.
    autonomy("autonomy", "Autonomy (AI SRE)", DriftSearch),
    // M20 Fleet: MSP multi-tenant fan-out LIST + dry-run campaign over /fleet/*.
    fleet("fleet", "Fleet (Multi-Tenant)", DriftSearch),
    cache("cache-dev", "Cache Sync", DriftSearch),
    detection("detection-remediation", "Detection & Remediation", DriftSearch),
    // ─ Diagnostics (cmtraceopen) ───────────────────────────────────────────
    builtin("logs", "Log Explorer", Diagnostics, Builtin::Logs),
    diag("intune-diag", "Intune Diagnostics", Diagnostics, "intune-diag"),
    diag("dsregcmd", "dsregcmd", Diagnostics, "dsregcmd"),
    diag("deployment", "Software Deployment", Diagnostics, "deployment"),
    diag("dns-dhcp", "DNS / DHCP", Diagnostics, "dns-dhcp"),
    diag("error-db", "Error-Code Database", Diagnostics, "error-db"),
    diag("registry", "Registry Viewer", Diagnostics, "registry"),
    diag("event-log", "Event Log Viewer", Diagnostics, "event-log"),
    diag("sysmon", "Sysmon", Diagnostics, "sysmon"),
    diag("secureboot", "Secure Boot Certs", Diagnostics, "secureboot"),
    diag("timeline-correlation", "Timeline Correlation", Diagnostics, "timeline"),
    diag("collector", "Diagnostics Collector", Diagnostics, "collector"),
];
