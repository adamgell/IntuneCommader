using System.Text.Json;

namespace CmProjectX.Api;

// M20 — persisted fleet state: tenant groups, golden templates, and campaign
// results. This is mutable OPERATIONAL config (like profiles.json / sync_state),
// NOT the append-only audit time-machine — the durable record of each per-tenant
// write still lands in that tenant's append-only store on apply (via the gated M13
// replay). Stored as small JSON files under %LocalAppData%\cmProjectX\ alongside the
// SnapshotStore DB. Tenant groups carry no Graph data; campaign results are the
// aggregated per-tenant outcomes the /fleet/campaign/{id} poll returns.
//
// Thread-safety: a single lock serializes load/mutate/save — fleet config is
// low-volume operator config, so a coarse lock is simpler and correct here.
public sealed class FleetStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _root;
    private readonly object _gate = new();

    private List<TenantGroupDto>? _groups;
    private List<GoldenTemplateDto>? _templates;
    private Dictionary<string, CampaignResultDto>? _campaigns;

    public FleetStore() : this(null) { }

    // storeRoot lets hermetic tests redirect the JSON files to an isolated dir
    // (mirrors SnapshotStore(storeRoot)); production passes null → the per-user path.
    public FleetStore(string? storeRoot)
    {
        _root = storeRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cmProjectX");
        Directory.CreateDirectory(_root);
    }

    private string GroupsPath => Path.Combine(_root, "fleet-groups.json");
    private string TemplatesPath => Path.Combine(_root, "fleet-templates.json");
    private string CampaignsPath => Path.Combine(_root, "fleet-campaigns.json");

    // ─── Tenant groups ───────────────────────────────────────────────────────

    public IReadOnlyList<TenantGroupDto> ListGroups()
    {
        lock (_gate) return LoadGroups().ToList();
    }

    public TenantGroupDto? GetGroup(string id)
    {
        lock (_gate) return LoadGroups().FirstOrDefault(g => g.Id == id);
    }

    // Create or replace a group by id. Server-assigns id/createdUtc when absent.
    public TenantGroupDto UpsertGroup(TenantGroupDto group)
    {
        lock (_gate)
        {
            var groups = LoadGroups();
            var id = string.IsNullOrWhiteSpace(group.Id) ? Guid.NewGuid().ToString("n") : group.Id;
            var createdUtc = string.IsNullOrWhiteSpace(group.CreatedUtc)
                ? DateTime.UtcNow.ToString("o") : group.CreatedUtc;
            var stored = group with { Id = id, CreatedUtc = createdUtc };
            groups.RemoveAll(g => g.Id == id);
            groups.Add(stored);
            SaveGroups(groups);
            return stored;
        }
    }

    public bool DeleteGroup(string id)
    {
        lock (_gate)
        {
            var groups = LoadGroups();
            var removed = groups.RemoveAll(g => g.Id == id) > 0;
            if (removed) SaveGroups(groups);
            return removed;
        }
    }

    // ─── Golden templates ────────────────────────────────────────────────────

    public IReadOnlyList<GoldenTemplateDto> ListTemplates()
    {
        lock (_gate) return LoadTemplates().ToList();
    }

    public GoldenTemplateDto? GetTemplate(string id)
    {
        lock (_gate) return LoadTemplates().FirstOrDefault(t => t.Id == id);
    }

    public GoldenTemplateDto UpsertTemplate(GoldenTemplateDto template)
    {
        lock (_gate)
        {
            var templates = LoadTemplates();
            var createdUtc = string.IsNullOrWhiteSpace(template.CreatedUtc)
                ? DateTime.UtcNow.ToString("o") : template.CreatedUtc;
            var stored = template with { CreatedUtc = createdUtc };
            templates.RemoveAll(t => t.Id == template.Id);
            templates.Add(stored);
            SaveTemplates(templates);
            return stored;
        }
    }

    // ─── Campaign results (poll via /fleet/campaign/{id}) ────────────────────

    public void SaveCampaign(CampaignResultDto result)
    {
        lock (_gate)
        {
            var campaigns = LoadCampaigns();
            campaigns[result.CampaignId] = result;
            SaveCampaigns(campaigns);
        }
    }

    public CampaignResultDto? GetCampaign(string id)
    {
        lock (_gate) return LoadCampaigns().TryGetValue(id, out var c) ? c : null;
    }

    // ─── JSON file helpers (all called under _gate) ──────────────────────────

    private List<TenantGroupDto> LoadGroups() => _groups ??= ReadList<TenantGroupDto>(GroupsPath);
    private void SaveGroups(List<TenantGroupDto> groups) { _groups = groups; WriteJson(GroupsPath, groups); }

    private List<GoldenTemplateDto> LoadTemplates() => _templates ??= ReadList<GoldenTemplateDto>(TemplatesPath);
    private void SaveTemplates(List<GoldenTemplateDto> templates) { _templates = templates; WriteJson(TemplatesPath, templates); }

    private Dictionary<string, CampaignResultDto> LoadCampaigns() =>
        _campaigns ??= ReadDict<CampaignResultDto>(CampaignsPath);
    private void SaveCampaigns(Dictionary<string, CampaignResultDto> campaigns)
    {
        _campaigns = campaigns;
        WriteJson(CampaignsPath, campaigns);
    }

    private static List<T> ReadList<T>(string path)
    {
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Json) ?? []; }
        catch { return []; }
    }

    private static Dictionary<string, T> ReadDict<T>(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, T>>(File.ReadAllText(path), Json) ?? new(); }
        catch { return new(); }
    }

    private static void WriteJson(string path, object value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
}
