namespace CmProjectX.Api.Tests;

// Canonical inventory of the sidecar's data endpoints, derived from the route
// registrations in service/Api/Endpoints/*.cs + Program.cs. Drives the data-driven
// tests so adding a surface is a one-line edit here.
public static class EndpointInventory
{
    // Every endpoint that returns a JSON ARRAY (normalized list rows or an
    // aggregate report). These are the "does this service bring data back?" checks.
    // `HasDetail` = the surface also exposes GET /{path}/{id}.
    public sealed record Surface(string Name, string Path, bool HasDetail);

    public static readonly IReadOnlyList<Surface> Surfaces = new[]
    {
        // ─ Devices ─
        new Surface("Device Configurations", "/device-configs", true),
        new Surface("Compliance Policies", "/compliance-policies", true),
        new Surface("Settings Catalog", "/settings-catalog", true),
        new Surface("Administrative Templates", "/admin-templates", true),
        new Surface("Endpoint Security", "/endpoint-security", true),
        new Surface("Remediation Scripts", "/remediation-scripts", true),
        new Surface("Compliance Scripts", "/compliance-scripts", true),
        new Surface("Platform Scripts", "/platform-scripts", true),
        new Surface("Shell Scripts", "/shell-scripts", true),
        new Surface("Custom Attributes (macOS)", "/mac-custom-attributes", true),
        new Surface("Device Categories", "/device-categories", true),
        new Surface("Feature Updates", "/feature-updates", true),
        new Surface("Quality Updates", "/quality-updates", true),
        new Surface("Driver Updates", "/driver-updates", true),
        new Surface("Managed Devices", "/managed-devices", true),
        // ─ Apps ─
        new Surface("Applications", "/apps", true),
        new Surface("App Protection Policies", "/app-protection", true),
        new Surface("App Configurations", "/app-configs", true),
        new Surface("VPP Tokens", "/vpp-tokens", true),
        // ─ Enrollment ─
        new Surface("Enrollment Configurations", "/enrollment-configs", true),
        new Surface("Autopilot Profiles", "/autopilot", true),
        new Surface("Apple DEP", "/apple-dep", true),
        new Surface("Cloud PC Provisioning", "/cloudpc-provisioning", true),
        // ─ Identity & Access ─
        new Surface("Conditional Access", "/conditional-access", true),
        new Surface("Named Locations", "/named-locations", true),
        new Surface("Authentication Strengths", "/auth-strengths", true),
        new Surface("Authentication Contexts", "/auth-contexts", true),
        new Surface("Terms of Use", "/terms-of-use", true),
        // ─ Tenant Admin ─
        new Surface("Scope Tags", "/scope-tags", true),
        new Surface("Role Definitions", "/role-definitions", true),
        new Surface("Role Assignments", "/role-assignments", true),
        new Surface("Assignment Filters", "/assignment-filters", true),
        new Surface("Policy Sets", "/policy-sets", true),
        new Surface("Intune Branding", "/intune-branding", true),
        new Surface("Azure Branding", "/azure-branding", true),
        new Surface("Terms & Conditions", "/terms-conditions", true),
        new Surface("Cloud PC User Settings", "/cloudpc-user-settings", true),
        new Surface("ADMX Files", "/admx-files", true),
        new Surface("Reusable Policy Settings", "/reusable-settings", true),
        new Surface("Notification Templates", "/notification-templates", true),
        // ─ Groups & Monitoring ─
        new Surface("Groups", "/groups", false),
        // ─ Aggregates / reports (array-shaped, no detail) ─
        new Surface("Assignment Explorer", "/assignment-explorer", false),
        new Surface("Permission Check", "/permission-check", false),
        new Surface("Dashboard", "/dashboard", false),
        new Surface("Security Posture", "/security-posture", false),
        new Surface("Security Baselines", "/baselines", false),
    };

    // xUnit MemberData feeds — one case per surface.
    public static IEnumerable<object[]> ListCases =>
        Surfaces.Select(s => new object[] { s.Name, s.Path });

    public static IEnumerable<object[]> DetailCases =>
        Surfaces.Where(s => s.HasDetail).Select(s => new object[] { s.Name, s.Path });
}
