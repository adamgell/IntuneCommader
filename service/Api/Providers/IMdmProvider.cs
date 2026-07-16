namespace CmProjectX.Api.Providers;

// ─── M22 cross-MDM feasibility spike — the provider seam ──────────────────────
//
// Today every endpoint module news a forked Core service against the active Graph
// client (`new ConfigurationProfileService(g).ListDeviceConfigurationsAsync(ct)` —
// DevicesEndpoints.cs:26) and projects the Graph element into a ListItemDto. The
// "Intune provider" is therefore *implicit* — it's the Core engine, wired straight
// into the handlers.
//
// IMdmProvider lifts that one projection step behind an interface keyed by the
// catalog Surface.Key, so a second backend (Jamf Pro, Workspace ONE) can answer
// the SAME contract without the client, the time-machine, GitOps, or the MCP layer
// knowing. The interface deals only in the shared, Graph-free DTOs
// (ListItemDto / AssignmentDto from Contracts.cs + Assignments.cs) — never a
// Microsoft.Graph.Beta type — which is exactly why it can live in the Api layer
// (where projection already happens) rather than in Core.
//
// SPIKE SCOPE: only ListAsync is exercised end-to-end (by StubMdmProvider, a
// read-only in-memory fake). The write/assign members are part of the seam's shape
// so the feasibility report can reason about the full surface, but an
// unimplemented verb on a given provider throws ProviderUnsupportedException →
// the endpoint maps it to 404 ("unsupported on this provider"), which the client
// already tolerates the same way it tolerates a 409-when-signed-out
// (api_client.rs:146). See docs/part-ii/M22-spike.md for the verdict.
public interface IMdmProvider
{
    // Stable provider id, matching TenantProfile.providerId ("intune" | "jamf" | …).
    string Id { get; }

    // The provider's honest declaration of which catalog surfaces it backs — a
    // subset of Surfaces.All. A surface absent here resolves to 404 for this
    // provider (the nav can gray it out; see the report's "partial overlap" note).
    IReadOnlyList<Surface> SupportedSurfaces { get; }

    // ── Reads — already the shape every endpoint produces today ──────────────
    Task<IReadOnlyList<ListItemDto>> ListAsync(string surfaceKey, CancellationToken ct);
    // Raw provider-native JSON body. DETAIL parity is explicitly out of scope for
    // M22 (the body shape differs per backend); the client renders it as-is.
    Task<string?> GetAsync(string surfaceKey, string id, CancellationToken ct);

    // ── Writes — gated by Surface.Writable; unsupported verbs throw ───────────
    Task<string> CreateAsync(string surfaceKey, string bodyJson, CancellationToken ct); // → new id
    Task UpdateAsync(string surfaceKey, string id, string bodyJson, CancellationToken ct);
    Task DeleteAsync(string surfaceKey, string id, CancellationToken ct);

    // ── Assignments — gated by Surface.Assignable; projects native targeting ──
    Task<IReadOnlyList<AssignmentDto>> GetAssignmentsAsync(string surfaceKey, string id, CancellationToken ct);
    Task SetAssignmentsAsync(string surfaceKey, string id, IReadOnlyList<AssignmentDto> assignments, CancellationToken ct);
}

// Thrown when a provider doesn't back a verb/surface. The dispatch layer maps it to
// 404 so the uniform client treats it like any other "surface not here" response.
public sealed class ProviderUnsupportedException(string providerId, string surfaceKey, string verb)
    : Exception($"Provider '{providerId}' does not support {verb} on surface '{surfaceKey}'.")
{
    public string ProviderId { get; } = providerId;
    public string SurfaceKey { get; } = surfaceKey;
    public string Verb { get; } = verb;
}
