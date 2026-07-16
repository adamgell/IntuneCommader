namespace CmProjectX.Api.Providers;

// Resolves the active IMdmProvider from the tenant profile. In the full M22 this
// keys off TenantProfile.providerId (net-new field, defaulting to "intune" so every
// existing profile keeps working untouched). For the spike there's no providerId on
// the forked Core TenantProfile yet, so the registry exposes both a by-id lookup
// (used by the stub demo endpoint) and a default — keeping the DI shape the report
// recommends without touching the persisted profile model.
public interface IProviderRegistry
{
    IReadOnlyList<IMdmProvider> All { get; }
    // Resolve by provider id; null when no provider with that id is registered.
    IMdmProvider? Get(string providerId);
}

public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<string, IMdmProvider> _byId;

    // Fail fast on a wiring mistake: zero providers (e.g. a commented-out registration)
    // would otherwise surface as a confusing 404 on the first request, and two providers
    // claiming the same id as ToDictionary's cryptic ArgumentException — both at runtime
    // instead of at startup.
    public ProviderRegistry(IEnumerable<IMdmProvider> providers)
    {
        var list = (providers ?? throw new ArgumentNullException(nameof(providers))).ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("ProviderRegistry requires at least one IMdmProvider registration.");
        _byId = new Dictionary<string, IMdmProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in list)
            if (!_byId.TryAdd(p.Id, p))
                throw new InvalidOperationException($"Duplicate IMdmProvider id '{p.Id}' — provider ids must be unique.");
    }

    public IReadOnlyList<IMdmProvider> All => _byId.Values.ToList();

    public IMdmProvider? Get(string providerId) =>
        _byId.TryGetValue(providerId, out var p) ? p : null;
}
