using CmProjectX.Api;
using CmProjectX.Api.Providers;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 hermetic tests for the M22 cross-MDM provider seam (service/Api/Providers).
// Fully offline: the stub provider serves in-memory fake data, so this proves the
// IMdmProvider abstraction projects a non-Intune backend into the SAME ListItemDto
// contract without any Graph/network/tenant. (docs/part-ii/M22-spike.md)
public sealed class MdmProviderTests
{
    private static readonly IMdmProvider Jamf = new StubMdmProvider();

    [Fact]
    public async Task StubProvider_ProjectsInventory_IntoListItemContract()
    {
        var rows = await Jamf.ListAsync("managed-devices", CancellationToken.None);

        Assert.NotEmpty(rows);
        // Every row is a well-formed ListItemDto — same shape the Intune endpoint emits.
        Assert.All(rows, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Id));
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.False(string.IsNullOrWhiteSpace(r.Subtitle));
        });
        // The provider tags itself via the first-class ListItemDto.Source field (M22).
        Assert.All(rows, r => Assert.Equal("jamf", r.Source));
    }

    [Fact]
    public async Task StubProvider_LoadsFromEmbeddedFixture_NotHardcodedRows()
    {
        // The inventory comes from the embedded recorded Jamf computers-inventory fixture
        // (5 computers across macOS + iPadOS), projected — not an inline list.
        var rows = await Jamf.ListAsync("managed-devices", CancellationToken.None);
        Assert.Equal(5, rows.Count);
        Assert.Contains(rows, r => r.Platform == "macOS");
        Assert.Contains(rows, r => r.Platform == "iOS");
    }

    [Fact]
    public async Task StubProvider_Get_ReturnsProviderNativeJson()
    {
        var body = await Jamf.GetAsync("managed-devices", "312", CancellationToken.None);
        Assert.NotNull(body);
        // The recorded provider-native shape (Jamf general/hardware), not our ListItem.
        Assert.Contains("\"general\"", body!);
        Assert.Contains("FRONTDESK-MAC-01", body);
        Assert.Null(await Jamf.GetAsync("managed-devices", "no-such-id", CancellationToken.None));
    }

    [Fact]
    public async Task StubProvider_LeadingSlashSurfaceKey_IsTolerated()
    {
        var rows = await Jamf.ListAsync("/managed-devices", CancellationToken.None);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task UnsupportedSurface_Throws_ProviderUnsupported()
    {
        var ex = await Assert.ThrowsAsync<ProviderUnsupportedException>(
            () => Jamf.ListAsync("conditional-access", CancellationToken.None));
        Assert.Equal("jamf", ex.ProviderId);
        Assert.Equal("conditional-access", ex.SurfaceKey);
    }

    [Fact]
    public async Task Writes_AreUnsupported_OnReadOnlyStub()
    {
        await Assert.ThrowsAsync<ProviderUnsupportedException>(
            () => Jamf.CreateAsync("managed-devices", "{}", CancellationToken.None));
        await Assert.ThrowsAsync<ProviderUnsupportedException>(
            () => Jamf.DeleteAsync("managed-devices", "312", CancellationToken.None));
    }

    [Fact]
    public void SupportedSurfaces_IsAHonestSubset_OfTheCatalog()
    {
        // The stub declares exactly the surfaces it backs, and each is a real catalog
        // surface — so the (future) nav can gray out everything it doesn't list.
        Assert.All(Jamf.SupportedSurfaces, s => Assert.NotNull(Surfaces.Find(s.Key)));
        Assert.Contains(Jamf.SupportedSurfaces, s => s.Key == "managed-devices");
    }

    [Fact]
    public void Registry_ResolvesActiveProvider_ById()
    {
        IProviderRegistry registry = new ProviderRegistry([Jamf]);
        Assert.Same(Jamf, registry.Get("jamf"));
        Assert.Same(Jamf, registry.Get("JAMF")); // case-insensitive
        Assert.Null(registry.Get("workspaceone")); // unknown id → null (endpoint maps to 404)
    }

    [Fact]
    public void Registry_RejectsZeroProviders_AtStartup()
    {
        // A wiring mistake (no registrations) must fail fast, not silently 404 every request.
        Assert.Throws<InvalidOperationException>(() => new ProviderRegistry([]));
    }

    [Fact]
    public void Registry_RejectsDuplicateProviderIds()
    {
        // Two providers claiming the same id is a programming error — surface it clearly.
        Assert.Throws<InvalidOperationException>(
            () => new ProviderRegistry([new StubMdmProvider(), new StubMdmProvider()]));
    }
}
