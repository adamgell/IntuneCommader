using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace CmProjectX.Api.Tests;

// Mutating black-box round-trips (apply → measure) against the LIVE sandbox tenant.
//
// Unlike the rest of Api.Tests (read-only GETs + write-GATE checks that never reach Graph),
// these author REAL writes: create an object, read it back to prove the write took effect,
// update + re-read, then delete + prove it's gone. Every object carries a "cmpx-roundtrip-"
// prefix and is cleaned up in a finally, so a mid-test failure can leak at most one orphan
// (findable by the prefix). The surfaces are benign + fully reversible (device categories,
// scope tags) — no device, user, or policy-assignment impact.
//
// OPT-IN: skipped unless CMPROJECTX_ALLOW_MUTATING_TESTS=1, so pointing the suite at a
// non-sandbox tenant can't fire writes by accident. Ivy24 is the designated sandbox tenant.
[Collection("sidecar")]
public sealed class MutatingRoundTripTests
{
    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public MutatingRoundTripTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private static bool MutatingEnabled =>
        (Environment.GetEnvironmentVariable("CMPROJECTX_ALLOW_MUTATING_TESTS") ?? "").Trim()
            is "1" or "true" or "TRUE" or "yes";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string UniqueName(string kind) => $"cmpx-roundtrip-{kind}-{Guid.NewGuid():N}";

    // Gate: signed in AND opted in. Logs a soft-skip (passing no-op) when the opt-in is unset,
    // matching the suite's convention for environment-dependent paths.
    private bool Ready(string test)
    {
        if (!MutatingEnabled)
        {
            _out.WriteLine(
                $"{test}: skipped — set CMPROJECTX_ALLOW_MUTATING_TESTS=1 to run mutating " +
                "round-trips against the sandbox tenant.");
            return false;
        }
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in. {_fx.SignInError}");
        return true;
    }

    // Poll a GET until it returns `want` — absorbs the brief Graph eventual-consistency lag
    // after a create/delete. The final fetch is returned live so the caller can assert on it.
    private async Task<HttpResponseMessage> PollAsync(string path, HttpStatusCode want, int attempts = 12)
    {
        for (var i = 0; i < attempts; i++)
        {
            var resp = await _fx.Http.GetAsync(path);
            if (resp.StatusCode == want) return resp;
            resp.Dispose();
            await Task.Delay(500);
        }
        return await _fx.Http.GetAsync(path);
    }

    // POST a collection, assert 200 { id }, return the new id.
    private async Task<string> CreateAsync(string collectionPath, string body)
    {
        using var resp = await _fx.Http.PostAsync(collectionPath, Json(body));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id), "create returned an empty id");
        return id!;
    }

    private async Task DeleteQuietAsync(string path)
    {
        try { using var _ = await _fx.Http.DeleteAsync(path); }
        catch { /* best-effort cleanup — the object may already be gone */ }
    }

    [Fact]
    public async Task DeviceCategory_CreateReadUpdateDelete_RoundTrips()
    {
        if (!Ready(nameof(DeviceCategory_CreateReadUpdateDelete_RoundTrips))) return;

        var name = UniqueName("devcat");
        var id = await CreateAsync("/device-categories",
            $"{{\"displayName\":\"{name}\",\"description\":\"created by cmProjectX round-trip test\"}}");

        var deleted = false;
        try
        {
            // APPLY measured (create): the detail reads back the exact displayName we wrote.
            using (var got = await PollAsync($"/device-categories/{id}", HttpStatusCode.OK))
            {
                Assert.Equal(HttpStatusCode.OK, got.StatusCode);
                using var doc = JsonDocument.Parse(await got.Content.ReadAsStringAsync());
                Assert.Equal(name, doc.RootElement.GetProperty("displayName").GetString());
            }

            // …and it appears in the LIST projection (cache invalidated on the write).
            using (var list = await _fx.Http.GetAsync("/device-categories"))
            {
                Assert.Equal(HttpStatusCode.OK, list.StatusCode);
                using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
                Assert.Contains(doc.RootElement.EnumerateArray(),
                    e => e.GetProperty("id").GetString() == id);
            }

            // APPLY measured (update): PATCH a new description, then read it back.
            const string updated = "updated by cmProjectX round-trip test";
            using (var patch = await _fx.Http.PatchAsync($"/device-categories/{id}",
                       Json($"{{\"displayName\":\"{name}\",\"description\":\"{updated}\"}}")))
            {
                Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
            }
            using (var got = await _fx.Http.GetAsync($"/device-categories/{id}"))
            {
                Assert.Equal(HttpStatusCode.OK, got.StatusCode);
                using var doc = JsonDocument.Parse(await got.Content.ReadAsStringAsync());
                Assert.Equal(updated, doc.RootElement.GetProperty("description").GetString());
            }

            // APPLY measured (delete): remove it, then prove it's gone.
            using (var del = await _fx.Http.DeleteAsync($"/device-categories/{id}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
            }
            deleted = true;
            using (var gone = await PollAsync($"/device-categories/{id}", HttpStatusCode.NotFound))
            {
                Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
            }
        }
        finally
        {
            if (!deleted) await DeleteQuietAsync($"/device-categories/{id}");
        }
    }

    [Fact]
    public async Task ScopeTag_CreateReadDelete_RoundTrips()
    {
        if (!Ready(nameof(ScopeTag_CreateReadDelete_RoundTrips))) return;

        var name = UniqueName("scope");
        var id = await CreateAsync("/scope-tags",
            $"{{\"displayName\":\"{name}\",\"description\":\"created by cmProjectX round-trip test\"}}");

        var deleted = false;
        try
        {
            // APPLY measured (create): detail reads back the displayName.
            using (var got = await PollAsync($"/scope-tags/{id}", HttpStatusCode.OK))
            {
                Assert.Equal(HttpStatusCode.OK, got.StatusCode);
                using var doc = JsonDocument.Parse(await got.Content.ReadAsStringAsync());
                Assert.Equal(name, doc.RootElement.GetProperty("displayName").GetString());
            }

            // APPLY measured (delete): remove it, then prove it's gone.
            using (var del = await _fx.Http.DeleteAsync($"/scope-tags/{id}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
            }
            deleted = true;
            using (var gone = await PollAsync($"/scope-tags/{id}", HttpStatusCode.NotFound))
            {
                Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
            }
        }
        finally
        {
            if (!deleted) await DeleteQuietAsync($"/scope-tags/{id}");
        }
    }
}
