namespace CmProjectX.Api;

// Server-side surface catalog — the single source of truth the MCP layer reads to
// generate its tool surface. Mirrors the client's feature registry
// (app/src/features.rs `list(..)`/`tiles(..)` rows) and the assignable set
// (app/src/assignments.rs `is_assignable`). A contract test (Api.Tests) asserts
// this stays in lockstep with the registered routes, so adding a management
// surface stays a one-line table edit here + the existing endpoint module.
//
// Diagnostics (dsregcmd/error-db/…) are intentionally absent: those run client-side
// in the Rust app against local files, not through the sidecar, so the sidecar's
// MCP server can't proxy them.

public sealed record Surface(
    string Path,         // sidecar route, e.g. "/device-configs"
    string DisplayName,  // human label, e.g. "Device Configurations"
    string Section,      // grouping, mirrors features.rs Section
    bool Writable,       // full CRUD vs read-only surface
    bool Assignable,     // exposes /{id}/assignments (set method in Core)
    // M12.1 blob read-through (docs/CACHE-M12.1.md): the LiteDB cache dataType this
    // surface's LIST/DETAIL handlers key on. null = uncached/pass-through (metric or
    // enriched-projection surfaces). When it equals a PrefetchAllToCacheAsync constant
    // the surface is warm-ahead; otherwise it fills lazily on first online load.
    // Trailing + defaulted so the positional rows that predate caching still compile.
    string? CacheKey = null)
{
    // Tool-facing key — the path without its leading slash (e.g. "device-configs").
    public string Key => Path.TrimStart('/');
}

public static class Surfaces
{
    public static readonly IReadOnlyList<Surface> All =
    [
        // ─ Overview (metric tiles + read surfaces) ──────────────────────────
        // Aggregations, not plain Graph List<T> → uncached (CacheKey null).
        new("/dashboard",            "Dashboard",                  "Overview",       false, false),
        new("/security-posture",     "Security Posture",           "Overview",       false, false),
        // ─ Devices ──────────────────────────────────────────────────────────
        // CacheKey rows ending in a PrefetchAllToCacheAsync constant warm-ahead;
        // the rest (device-categories/quality/driver/managed-devices) fill lazily.
        new("/device-configs",       "Device Configurations",      "Devices",        true,  false, "DeviceConfigurations"),
        new("/compliance-policies",  "Compliance Policies",        "Devices",        true,  true,  "CompliancePolicies"),
        new("/settings-catalog",     "Settings Catalog",           "Devices",        true,  true,  "SettingsCatalog"),
        new("/admin-templates",      "Administrative Templates",   "Devices",        true,  true,  "AdministrativeTemplates"),
        new("/endpoint-security",    "Endpoint Security",          "Devices",        true,  true,  "EndpointSecurityIntents"),
        new("/device-categories",    "Device Categories",          "Devices",        true,  false, "DeviceCategories"),
        new("/remediation-scripts",  "Remediation Scripts",        "Devices",        true,  false, "DeviceHealthScripts"),
        new("/compliance-scripts",   "Compliance Scripts",         "Devices",        true,  false, "ComplianceScripts"),
        new("/feature-updates",      "Feature Updates",            "Devices",        true,  false, "FeatureUpdateProfiles"),
        new("/quality-updates",      "Quality Updates",            "Devices",        true,  false, "QualityUpdateProfiles"),
        new("/driver-updates",       "Driver Updates",             "Devices",        true,  false, "DriverUpdateProfiles"),
        new("/platform-scripts",     "Device Management Scripts",  "Devices",        true,  true,  "DeviceManagementScripts"),
        new("/shell-scripts",        "Shell Scripts (macOS)",      "Devices",        true,  true,  "DeviceShellScripts"),
        new("/mac-custom-attributes","Custom Attributes (macOS)",  "Devices",        true,  false, "MacCustomAttributes"),
        new("/managed-devices",      "Managed Devices",            "Devices",        false, false, "ManagedDevices"),
        // ─ Apps ─────────────────────────────────────────────────────────────
        new("/apps",                 "Applications",               "Apps",           false, true,  "Applications"),
        new("/app-protection",       "App Protection Policies",    "Apps",           true,  false, "AppProtectionPolicies"),
        new("/app-configs",          "App Configurations",         "Apps",           true,  false, "ManagedDeviceAppConfigurations"),
        new("/vpp-tokens",           "VPP Tokens",                 "Apps",           false, false, "VppTokens"),
        // ─ Enrollment ───────────────────────────────────────────────────────
        new("/enrollment-configs",   "Enrollment Configurations",  "Enrollment",     true,  false, "EnrollmentConfigurations"),
        new("/autopilot",            "Autopilot Profiles",         "Enrollment",     true,  false, "AutopilotProfiles"),
        new("/apple-dep",            "Apple DEP",                  "Enrollment",     false, false, "AppleDep"),
        new("/cloudpc-provisioning", "Cloud PC Provisioning",      "Enrollment",     false, false, "CloudPcProvisioning"),
        // ─ Identity & Access ────────────────────────────────────────────────
        // /conditional-access caches the plain LIST/DETAIL; its enriched /list,
        // /{id}/summary, /{id}/detail routes do name resolution and stay live.
        new("/conditional-access",   "Conditional Access",         "Identity",       false, false, "ConditionalAccessPolicies"),
        new("/named-locations",      "Named Locations",            "Identity",       true,  false, "NamedLocations"),
        new("/auth-strengths",       "Authentication Strengths",   "Identity",       true,  false, "AuthenticationStrengths"),
        new("/auth-contexts",        "Authentication Contexts",    "Identity",       true,  false, "AuthenticationContexts"),
        new("/terms-of-use",         "Terms of Use",               "Identity",       true,  false, "TermsOfUseAgreements"),
        // ─ Tenant Admin ─────────────────────────────────────────────────────
        new("/scope-tags",           "Scope Tags",                 "TenantAdmin",    true,  false, "ScopeTags"),
        new("/role-definitions",     "Role Definitions",           "TenantAdmin",    true,  false, "RoleDefinitions"),
        new("/role-assignments",     "Role Assignments",           "TenantAdmin",    false, false, "RoleAssignments"),
        new("/assignment-filters",   "Assignment Filters",         "TenantAdmin",    false, false, "AssignmentFilters"),
        new("/policy-sets",          "Policy Sets",                "TenantAdmin",    false, false, "PolicySets"),
        new("/intune-branding",      "Intune Branding",            "TenantAdmin",    true,  false, "IntuneBrandingProfiles"),
        new("/azure-branding",       "Azure Branding",             "TenantAdmin",    true,  false, "AzureBrandingLocalizations"),
        new("/terms-conditions",     "Terms & Conditions",         "TenantAdmin",    true,  false, "TermsAndConditions"),
        new("/cloudpc-user-settings","Cloud PC User Settings",     "TenantAdmin",    false, false, "CloudPcUserSettings"),
        new("/admx-files",           "ADMX Files",                 "TenantAdmin",    true,  false, "AdmxFiles"),
        new("/reusable-settings",    "Reusable Policy Settings",   "TenantAdmin",    true,  false, "ReusableSettings"),
        new("/notification-templates","Notification Templates",    "TenantAdmin",    true,  false, "NotificationTemplates"),
        // ─ Groups & Monitoring ──────────────────────────────────────────────
        // /groups merges assigned+dynamic; deferred to M12.2 (relationship store).
        new("/groups",               "Groups",                     "GroupsMon",      false, false),
        new("/permission-check",     "Permission Check",           "GroupsMon",      false, false),
        // ─ Drift & Compare ──────────────────────────────────────────────────
        new("/assignment-explorer",  "Assignment Explorer",        "DriftSearch",    false, false),
        new("/baselines",            "Security Baselines",         "DriftSearch",    false, false),
    ];

    // Resolve a tool-supplied surface key (tolerant of a leading slash / casing).
    public static Surface? Find(string key)
    {
        var k = key.Trim().TrimStart('/');
        return All.FirstOrDefault(s => string.Equals(s.Key, k, StringComparison.OrdinalIgnoreCase));
    }
}
