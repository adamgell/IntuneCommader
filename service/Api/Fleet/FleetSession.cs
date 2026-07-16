using Azure.Core;
using Intune.Commander.Core.Auth;
using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.Graph.Beta;

namespace CmProjectX.Api;

// M20 — multi-tenant session (Pattern G fan-out plane). Today AuthSession holds one
// live Graph for the single ACTIVE profile and force-SignOut()s on switch, because a
// token issued for tenant A is invalid for B and the per-tenant cache must never be
// cross-written (AuthSession.Activate). FleetSession relaxes that: it holds N
// GraphServiceClients at once — one per profile — so a fan-out read/campaign can reach
// every target concurrently. Each slot is built lazily via IntuneGraphClientFactory
// (the SAME path AuthSession uses), so stored-profile fan-out needs ZERO new consent:
// if the operator can sign into each tenant single-tenant today, fleet signs into all.
//
// Phase 1 is stored-profile fan-out; GDAP (one delegated identity per customer) is the
// deferred Phase 2 — the fan-out plane is identical either way, only how each per-tenant
// Graph is acquired changes (docs/part-ii/M20-fleet.md § GDAP vs multi-profile).
//
// IMPORTANT (hermetic boundary): building a client is silent for ClientSecret profiles
// but token acquisition for delegated (Interactive/DeviceCode) profiles still requires
// the interactive browser/device-code flow — so the live N-tenant campaign in the DoD
// cannot run offline. Fan-out degrades per tenant: a profile that can't acquire a token
// is reported as that tenant's error, never a fleet abort.
public sealed class FleetSession
{
    private readonly ProfileService _profiles;
    private readonly IntuneGraphClientFactory _graphFactory;
    private readonly object _gate = new();

    // tenantId → live session slot. Built lazily, reused across fan-out calls. Keyed by
    // tenantId (not profileId) so the cache/store {tenantId} scoping lines up exactly.
    private readonly Dictionary<string, FleetSlot> _slots = new(StringComparer.OrdinalIgnoreCase);

    public FleetSession(ProfileService profiles, IntuneGraphClientFactory graphFactory)
    {
        _profiles = profiles;
        _graphFactory = graphFactory;
    }

    private sealed record FleetSlot(GraphServiceClient Graph, TokenCredential Credential, string[] Scopes);

    public IReadOnlyList<TenantProfile> Profiles => _profiles.Profiles;

    public TenantProfile? ProfileForTenant(string tenantId) =>
        _profiles.Profiles.FirstOrDefault(p => string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

    // Whether tenantId already has a live slot in the fleet session.
    public bool IsSignedIn(string tenantId)
    {
        lock (_gate) return _slots.ContainsKey(tenantId);
    }

    // The live Graph for a tenant, or null if no slot has been established.
    public GraphServiceClient? GraphFor(string tenantId)
    {
        lock (_gate) return _slots.TryGetValue(tenantId, out var slot) ? slot.Graph : null;
    }

    // Acquire (build + force token) a live Graph client for the tenant and cache the
    // slot. Returns the client, or throws — callers catch per tenant so one failure
    // (no profile, throttle, consent gap) degrades only that tenant's fan-out result.
    public async Task<GraphServiceClient> EnsureAsync(string tenantId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_slots.TryGetValue(tenantId, out var existing))
                return existing.Graph;
        }

        var profile = ProfileForTenant(tenantId)
            ?? throw new InvalidOperationException($"No saved profile for tenant '{tenantId}'.");

        var (client, credential, scopes) = await _graphFactory.CreateClientWithCredentialAsync(profile, null, ct);
        // Force token acquisition now so a consent/secret failure surfaces here (caught
        // per tenant) rather than mid-fan-out on the first Graph call.
        await credential.GetTokenAsync(new TokenRequestContext(scopes), ct);

        lock (_gate)
        {
            if (_slots.TryGetValue(tenantId, out var raced))
                return raced.Graph; // another fan-out built it first
            _slots[tenantId] = new FleetSlot(client, credential, scopes);
            return client;
        }
    }

    // Drop a tenant's slot (e.g. its token expired). The next EnsureAsync rebuilds it.
    public void Drop(string tenantId)
    {
        lock (_gate) _slots.Remove(tenantId);
    }

    public void Clear()
    {
        lock (_gate) _slots.Clear();
    }
}
