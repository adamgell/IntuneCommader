// M14 device-action catalog + safety gate (Pattern F). Extracted from
// DeviceScriptsEndpoints so the pure decisions are unit-testable (Api.Tests.Unit) and
// the endpoint handlers stay thin. Nothing here touches Graph or HTTP — it's the
// classification (reversible vs destructive), the org allowlist / caps / opt-in
// resolution, the required-scope mapping, and the typed-confirm token checks.
//
// Gating model (M14 DoD — the SAFER doc spec, reconciled with the shipped env vars):
//   • Global kill-switch  CMPROJECTX_DEVICE_ACTIONS_DISABLED   → every action 403s.
//   • Per-verb allowlist  CMPROJECTX_DEVICE_ACTION_<VERBID>=1  enables ONE destructive
//     verb (e.g. CMPROJECTX_DEVICE_ACTION_WIPE). Destructive verbs are OFF by default
//     (wipe / retire / cleanWindowsDevice / autopilotReset).
//   • Legacy master switch CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE=1 enables ALL
//     destructive verbs at once (backward-compat with the M14 backend that shipped).
//   • Reversible bulk cap  CMPROJECTX_DEVICE_ACTIONS_BULK_CAP            (default 200).
//   • Destructive bulk is a SEPARATE opt-in CMPROJECTX_DEVICE_ACTIONS_BULK_DESTRUCTIVE=1,
//     hard-capped by CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE_BULK_CAP     (default 25).
// A verb being enabled for single use does NOT enable it for bulk — bulk destructive
// needs its own opt-in on top of the allowlist. Env overrides keep working throughout.
namespace CmProjectX.Api;

// One managedDevice action verb the sidecar can dispatch. `Id` is the client-facing
// action key AND (usually) the Graph OData action segment; `EffectiveVerb` lets an id
// diverge from the Graph verb (autopilotReset → the `wipe` verb with autopilot params).
// `Destructive` ⇒ typed-confirm + allowlist + (for bulk) opt-in gated. `DefaultBody` is
// sent when the verb needs a parameter object and the caller supplied none.
internal sealed record DeviceActionMeta(
    string Id, string DisplayName, bool Destructive, string? GraphVerb = null, string? DefaultBody = null)
{
    public string EffectiveVerb => GraphVerb ?? Id;
}

// The action catalog. Verb ids/segments and their bodies were validated against
// Microsoft.Graph.Beta 5.130.0-preview (Microsoft.Graph.Beta.xml):
//   setDeviceName (SetDeviceNamePostRequestBody.DeviceName), rotateFileVaultKey,
//   createDeviceLogCollectionRequest (…PostRequestBody.TemplateType), cleanWindowsDevice,
//   wipe (WipePostRequestBody.KeepEnrollmentData/KeepUserData). No standalone
//   `autopilotReset` verb exists → it maps onto `wipe` with keepEnrollmentData=true.
internal static class DeviceActionCatalog
{
    public static readonly IReadOnlyDictionary<string, DeviceActionMeta> Actions =
        new[]
        {
            // ── reversible ────────────────────────────────────────────────────
            new DeviceActionMeta("syncDevice", "Sync", false),
            new DeviceActionMeta("rebootNow", "Restart", false),
            new DeviceActionMeta("remoteLock", "Remote Lock", false),
            new DeviceActionMeta("shutDown", "Shut Down", false),
            new DeviceActionMeta("locateDevice", "Locate", false),
            new DeviceActionMeta("windowsDefenderScan", "Defender Quick Scan", false, DefaultBody: "{\"quickScan\":true}"),
            new DeviceActionMeta("windowsDefenderUpdateSignatures", "Update Defender Signatures", false),
            new DeviceActionMeta("rotateBitLockerKeys", "Rotate BitLocker Keys", false),
            new DeviceActionMeta("rotateFileVaultKey", "Rotate FileVault Key", false),
            new DeviceActionMeta("setDeviceName", "Rename", false), // needs { deviceName } from the caller
            new DeviceActionMeta("createDeviceLogCollectionRequest", "Collect Diagnostics", false, DefaultBody: "{\"templateType\":\"predefined\"}"),
            // ── destructive (OFF by default; allowlist-gated) ─────────────────
            new DeviceActionMeta("retire", "Retire (remove company data)", true),
            new DeviceActionMeta("wipe", "Wipe (factory reset)", true),
            new DeviceActionMeta("cleanWindowsDevice", "Fresh Start", true, DefaultBody: "{\"keepUserData\":true}"),
            new DeviceActionMeta("autopilotReset", "Autopilot Reset", true, GraphVerb: "wipe", DefaultBody: "{\"keepEnrollmentData\":true,\"keepUserData\":false}"),
        }.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    // The destructive verb ids — the allowlist domain the gate resolves env flags for.
    public static readonly IReadOnlyList<string> DestructiveVerbIds =
        Actions.Values.Where(a => a.Destructive).Select(a => a.Id).ToList();
}

internal static class DeviceActionGate
{
    internal const int DefaultReversibleBulkCap = 200;
    internal const int DefaultDestructiveBulkCap = 25;

    // Graph delegated/app permission scopes gating device actions (doc rail 3).
    internal const string ReadWriteScope = "DeviceManagementManagedDevices.ReadWrite.All";
    internal const string PrivilegedScope = "DeviceManagementManagedDevices.PrivilegedOperations.All";

    // Resolved gating configuration for one request. Pure data — no env reads here, so
    // decisions over a Config are deterministic and unit-testable.
    internal sealed record Config(
        bool GlobalKill,
        bool AllDestructive,
        IReadOnlySet<string> EnabledDestructiveVerbs,
        bool BulkDestructive,
        int ReversibleBulkCap,
        int DestructiveBulkCap);

    // "1" or any case/whitespace variant of a real bool ("true"/"True"/"TRUE ").
    internal static bool IsTruthy(string? v)
    {
        var s = v?.Trim();
        return s == "1" || (bool.TryParse(s, out var b) && b);
    }

    // Resolve the gate from the environment. `destructiveVerbIds` scopes the per-verb
    // allowlist scan (one CMPROJECTX_DEVICE_ACTION_<VERBID> lookup per destructive verb).
    internal static Config FromEnvironment(IEnumerable<string> destructiveVerbIds)
    {
        static string? E(string k) => Environment.GetEnvironmentVariable(k);
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in destructiveVerbIds)
            if (IsTruthy(E($"CMPROJECTX_DEVICE_ACTION_{id.ToUpperInvariant()}")))
                enabled.Add(id);
        static int Cap(string k, int def) =>
            int.TryParse(E(k), out var n) && n > 0 ? n : def;
        return new Config(
            GlobalKill: IsTruthy(E("CMPROJECTX_DEVICE_ACTIONS_DISABLED")),
            AllDestructive: IsTruthy(E("CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE")),
            EnabledDestructiveVerbs: enabled,
            BulkDestructive: IsTruthy(E("CMPROJECTX_DEVICE_ACTIONS_BULK_DESTRUCTIVE")),
            ReversibleBulkCap: Cap("CMPROJECTX_DEVICE_ACTIONS_BULK_CAP", DefaultReversibleBulkCap),
            DestructiveBulkCap: Cap("CMPROJECTX_DEVICE_ACTIONS_DESTRUCTIVE_BULK_CAP", DefaultDestructiveBulkCap));
    }

    // Org allowlist (doc rail 4): a destructive verb runs only when the legacy master
    // switch is on OR the per-verb flag enabled it. Reversible verbs are always allowed.
    internal static bool IsDestructiveVerbEnabled(Config c, string verbId) =>
        c.AllDestructive || c.EnabledDestructiveVerbs.Contains(verbId);

    // The bulk id cap for this action class (doc rail 5): 200 reversible / 25 destructive.
    internal static int BulkCapFor(Config c, bool destructive) =>
        destructive ? c.DestructiveBulkCap : c.ReversibleBulkCap;

    // ── Scope-check (rail 3) ─────────────────────────────────────────────────
    // Privileged operations for wipe/retire/etc.; plain read-write for reversible verbs.
    internal static string RequiredScope(bool destructive) =>
        destructive ? PrivilegedScope : ReadWriteScope;

    // Does the granted permission set authorize this action class? The privileged scope
    // subsumes read-write, so it also satisfies reversible actions.
    internal static bool HasDeviceActionScope(IEnumerable<string> granted, bool destructive)
    {
        var set = new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase);
        if (set.Contains(RequiredScope(destructive))) return true;
        if (!destructive && set.Contains(PrivilegedScope)) return true;
        return false;
    }

    // ── Typed-confirm (rail 2) ───────────────────────────────────────────────
    // Single destructive: confirm == the live device name, case-INSENSITIVE (matches the
    // shipped M14 behaviour + the client's eq_ignore_ascii_case gate).
    internal static bool SingleConfirmMatches(string? confirm, string? deviceName) =>
        !string.IsNullOrWhiteSpace(deviceName) &&
        string.Equals(confirm, deviceName, StringComparison.OrdinalIgnoreCase);

    // Bulk destructive: confirm == the literal sweep phrase "{verb} {count}", case-
    // SENSITIVE (per-device name typing doesn't scale; the phrase is deliberate friction).
    internal static string BulkConfirmToken(string verbId, int count) => $"{verbId} {count}";
    internal static bool BulkConfirmMatches(string? confirm, string verbId, int count) =>
        string.Equals(confirm, BulkConfirmToken(verbId, count), StringComparison.Ordinal);
}
