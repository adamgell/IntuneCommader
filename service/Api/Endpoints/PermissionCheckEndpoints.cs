// PermissionCheck endpoint — read-only diagnostic surface that compares the
// permissions present in the active access token against the full set required by
// Intune Commander. Lives in its own module (not Program.cs) per the project's
// Minimal-API convention; Graph Beta type names are safe to reference here.
//
// Shared shape: GET /permission-check projects PermissionCheckResult into the
// normalized ListItemDto list (summary row + one row per required permission +
// optional extra-permission rows). 409 (Conflict) = signed out (auth.Graph is null).
//
// DI note: neither PermissionCheckService nor IPermissionCheckService is registered
// in AddIntuneCommanderCore, and PermissionCheckService takes (TokenCredential,
// string[]) — NOT a GraphServiceClient. AuthSession exposes the live TokenCredential
// it acquired at sign-in (AuthSession.Credential), so the handler reuses THAT (same
// token cache) instead of building a fresh credential — a fresh one would re-trigger
// an interactive browser prompt for delegated (Interactive/DeviceCode) profiles and
// fail to bind the loopback redirect the live session still owns.
using Intune.Commander.Core.Services;

namespace CmProjectX.Api;

public static class PermissionCheckEndpoints
{
    public static void MapPermissionCheck(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        // ─── permission-check (PermissionCheckResult) — READONLY ──────────────
        app.MapGet("/permission-check", async (AuthSession auth, CancellationToken ct) =>
        {
            // Reuse the session's live credential (set at sign-in). Building a new one
            // would re-prompt the browser for delegated profiles; there's no reason to
            // acquire a second token here.
            var credential = auth.Credential;
            var scopes = auth.Scopes;
            if (credential is null || scopes is null) return Results.Conflict(); // signed out

            var result = await new PermissionCheckService(credential, scopes).CheckPermissionsAsync(ct);

            var items = new List<ListItemDto>
            {
                new ListItemDto(
                    "__summary",
                    "Summary",
                    $"claim source: {result.ClaimSource}",
                    result.AllPermissionsGranted ? "OK" : "gaps"),
            };

            var grantedSet = new HashSet<string>(result.GrantedPermissions, StringComparer.OrdinalIgnoreCase);

            foreach (var p in result.RequiredPermissions)
            {
                items.Add(new ListItemDto(
                    p,
                    p,
                    "required",
                    grantedSet.Contains(p) ? "granted" : "MISSING"));
            }

            foreach (var p in result.ExtraPermissions)
            {
                items.Add(new ListItemDto(
                    p,
                    p,
                    "extra (granted, not required)",
                    "extra"));
            }

            return Results.Ok(items);
        });
    }
}
