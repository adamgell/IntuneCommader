using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace CmProjectX.Api.Tests;

// M12.1 blob read-through cache — HTTP-level integration checks against the live
// sidecar (see docs/CACHE-M12.1.md). Black-box like the other suites: the cache must
// be transparent (identical data across consecutive reads), and warming must surface
// via /health.lastWarmedUtc. Auth-gated checks skip when the sidecar is signed out,
// matching the ListEndpointTests/DetailEndpointTests convention.
[Collection("sidecar")]
public sealed class CacheTests
{
    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public CacheTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // The contract grew a lastWarmedUtc field (M12.1). /health is auth-agnostic, so
    // this runs whenever the sidecar is reachable. The value is null until the first
    // successful warm; assert /health is the SyncStatus object and that lastWarmedUtc,
    // when non-null, is a well-formed date-time.
    [Fact]
    public async Task Health_IsSyncStatus_LastWarmedUtcWellFormedWhenPresent()
    {
        using var resp = await _fx.Http.GetAsync("/health");
        Assert.Equal(200, (int)resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("authState", out _), "/health missing authState");

        if (root.TryGetProperty("lastWarmedUtc", out var w) && w.ValueKind != JsonValueKind.Null)
        {
            Assert.True(DateTimeOffset.TryParse(w.GetString(), out _),
                $"lastWarmedUtc was present but not a date-time: {w.GetString()}");
        }
    }

    // After sign-in the warm fires fire-and-forget (AuthSession.CacheWarm →
    // PrefetchAllToCacheAsync) and stamps LastWarmedUtc on full success. Poll /health
    // until it appears. Skips when signed out.
    [Fact]
    public async Task SignIn_Warms_PopulatesLastWarmedUtc()
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate warming. {_fx.SignInError}");

        string? warmed = null;
        for (var i = 0; i < 180; i++) // up to ~90s; the warm pulls ~32 Graph lists best-effort
        {
            using var resp = await _fx.Http.GetAsync("/health");
            if ((int)resp.StatusCode == 200)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("lastWarmedUtc", out var w) &&
                    w.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(w.GetString()))
                {
                    warmed = w.GetString();
                    break;
                }
            }
            await Task.Delay(500);
        }

        _out.WriteLine($"lastWarmedUtc = {warmed ?? "(null)"}");
        Assert.False(string.IsNullOrEmpty(warmed),
            "Sign-in did not warm the cache (lastWarmedUtc stayed null after ~45s).");
    }

    // The read-through must be transparent: two consecutive GETs of a cacheable
    // surface return the same array length (a cache hit must equal a cache miss).
    // Skips when signed out.
    [Theory]
    [InlineData("/device-configs")]
    [InlineData("/compliance-policies")]
    [InlineData("/endpoint-security")]
    [InlineData("/scope-tags")]
    public async Task CacheableList_StableAcrossTwoReads(string path)
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate caching. {_fx.SignInError}");

        var first = await ReadArrayLen(path);
        var second = await ReadArrayLen(path);
        _out.WriteLine($"{path}: first={first}, second={second}");

        Assert.True(first >= 0, $"{path} first read was not a JSON array");
        Assert.Equal(first, second);
    }

    private async Task<int> ReadArrayLen(string path)
    {
        using var resp = await _fx.Http.GetAsync(path);
        Assert.Equal(200, (int)resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : -1;
    }
}
