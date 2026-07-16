using System.Text.Json;

namespace CmProjectX.Api.Providers;

// ─── M22 cross-MDM — ONE read surface from a SECOND provider ──────────────────
//
// A stand-in for a future JamfProvider, proving the seam end-to-end without a real
// Jamf tenant or auth adapter. It declares exactly ONE supported surface
// (managed-devices) and projects a RECORDED Jamf Pro `/v1/computers-inventory`
// payload (the embedded fixture Providers/fixtures/jamf-computers-inventory.json)
// into the SAME ListItemDto the Intune endpoint emits — the projection step is the
// same KIND ApplicationMapper does for Graph, just starting from a Jamf body. The
// provenance now rides on the first-class ListItemDto.Source field ("jamf"), not the
// subtitle. GET .../{id} returns the provider-native JSON as recorded.
//
// Everything else (writes, assignments, other surfaces) throws
// ProviderUnsupportedException, which the dispatch endpoint turns into a 404 the
// uniform client already handles. This is the milestone DoD case: "a single READ
// surface, served by a non-Intune provider, behind the same contract."
public sealed class StubMdmProvider : IMdmProvider
{
    public string Id => "jamf";

    // Only what a real Jamf provider could honestly back read-only on day one
    // (M22 doc: groups + apps + device inventory are the lowest-risk overlap).
    public IReadOnlyList<Surface> SupportedSurfaces { get; } =
        [RequireSurface("managed-devices")];

    // Loaded once from the embedded recorded Jamf computers-inventory fixture.
    private static readonly IReadOnlyList<JamfComputer> Computers = LoadFixture();

    public Task<IReadOnlyList<ListItemDto>> ListAsync(string surfaceKey, CancellationToken ct)
    {
        EnsureSupported(surfaceKey, "list");
        IReadOnlyList<ListItemDto> rows = Computers
            .Select(c => new ListItemDto(
                Id: c.Id,
                Title: c.Name,
                Subtitle: $"{c.Model} • {c.Os}",
                Badge: null,
                Platform: PlatformOf(c.Os),
                Modified: c.LastInventory,
                Source: Id)) // first-class provenance (ListItemDto.Source), no longer the subtitle
            .ToList();
        return Task.FromResult(rows);
    }

    // The provider-native body, exactly as recorded (DETAIL parity is out of scope; the
    // client renders it as-is — the M22 seam's declared behaviour).
    public Task<string?> GetAsync(string surfaceKey, string id, CancellationToken ct)
    {
        EnsureSupported(surfaceKey, "get");
        return Task.FromResult(Computers.FirstOrDefault(x => x.Id == id)?.NativeJson);
    }

    // ── Everything past the one read surface is honestly unsupported ──────────
    public Task<string> CreateAsync(string surfaceKey, string bodyJson, CancellationToken ct) =>
        throw new ProviderUnsupportedException(Id, surfaceKey, "create");
    public Task UpdateAsync(string surfaceKey, string id, string bodyJson, CancellationToken ct) =>
        throw new ProviderUnsupportedException(Id, surfaceKey, "update");
    public Task DeleteAsync(string surfaceKey, string id, CancellationToken ct) =>
        throw new ProviderUnsupportedException(Id, surfaceKey, "delete");
    public Task<IReadOnlyList<AssignmentDto>> GetAssignmentsAsync(string surfaceKey, string id, CancellationToken ct) =>
        throw new ProviderUnsupportedException(Id, surfaceKey, "get-assignments");
    public Task SetAssignmentsAsync(string surfaceKey, string id, IReadOnlyList<AssignmentDto> a, CancellationToken ct) =>
        throw new ProviderUnsupportedException(Id, surfaceKey, "set-assignments");

    private void EnsureSupported(string surfaceKey, string verb)
    {
        if (!SupportedSurfaces.Any(s => string.Equals(s.Key, surfaceKey.TrimStart('/'), StringComparison.OrdinalIgnoreCase)))
            throw new ProviderUnsupportedException(Id, surfaceKey, verb);
    }

    // Fail fast with a clear message if the catalog ever drops the surface, instead of the
    // cryptic NullReferenceException at DI-construction time the null-forgiving `!` gave.
    private static Surface RequireSurface(string key) =>
        Surfaces.Find(key) ?? throw new InvalidOperationException(
            $"M22 stub provider requires the '{key}' surface in Surfaces.cs.");

    private static string PlatformOf(string os) =>
        os.StartsWith("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS" :
        os.StartsWith("mac", StringComparison.OrdinalIgnoreCase) ? "macOS" : "Unknown";

    // ── fixture loading — project the recorded Jamf inventory into our model ──
    private static IReadOnlyList<JamfComputer> LoadFixture()
    {
        var asm = typeof(StubMdmProvider).Assembly;
        var name = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith("jamf-computers-inventory.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "M22 fixture 'jamf-computers-inventory.json' is not embedded (Api.csproj EmbeddedResource missing?)");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);

        var list = new List<JamfComputer>();
        if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            foreach (var r in results.EnumerateArray())
            {
                var general = r.GetProperty("general");
                var os = r.GetProperty("operatingSystem");
                var osName = os.GetProperty("name").GetString() ?? "";
                var osVer = os.GetProperty("version").GetString() ?? "";
                list.Add(new JamfComputer(
                    Id: r.GetProperty("id").GetString() ?? "",
                    Name: general.GetProperty("name").GetString() ?? "(unnamed)",
                    Os: $"{osName} {osVer}".Trim(),
                    Model: r.GetProperty("hardware").GetProperty("model").GetString() ?? "",
                    LastInventory: general.TryGetProperty("lastContactTime", out var lc) ? lc.GetString() : null,
                    NativeJson: r.GetRawText()));
            }
        return list;
    }

    private sealed record JamfComputer(
        string Id, string Name, string Os, string Model, string? LastInventory, string NativeJson);
}
