using System.Net;
using System.Net.Http.Headers;
using CmProjectX.Recording;
using Xunit;

namespace CmProjectX.Api.Tests.Integration.Recording;

// Tier-2 infrastructure tests: prove the record→replay round-trip works fully
// OFFLINE, before any of it is wired into the Graph SDK or a live tenant. A stub
// handler stands in for "the network" during record.
public sealed class RecordReplayTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "cmpx-cassettes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private const string Url =
        "https://graph.microsoft.com/beta/deviceManagement/deviceConfigurations";
    private const string Body = """{"value":[{"id":"abc","displayName":"Kiosk"}]}""";

    [Fact]
    public async Task Record_ThenReplay_RoundTripsResponse()
    {
        // Record through a stub "network" that returns a canned Graph-ish response.
        var store = new CassetteStore(_dir);
        using (var recordClient = new HttpClient(new RecordHandler(store, new StubHandler(Body))))
        {
            var recorded = await recordClient.GetAsync(Url);
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
            Assert.Equal(Body, await recorded.Content.ReadAsStringAsync());
        }
        Assert.Equal(1, store.Count);

        // Replay from a freshly-loaded store with NO inner handler — purely offline.
        using var replayClient = new HttpClient(new ReplayHandler(new CassetteStore(_dir)));
        var replayed = await replayClient.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(Body, await replayed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Replay_UnmatchedRequest_FailsLoudly()
    {
        using var client = new HttpClient(new ReplayHandler(new CassetteStore(_dir)));

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync(Url));
        Assert.Contains("No cassette", ex.Message + (ex.InnerException?.Message ?? ""));
    }

    [Fact]
    public async Task Record_DoesNotPersistAuthorizationHeader()
    {
        var store = new CassetteStore(_dir);
        using (var client = new HttpClient(new RecordHandler(store, new StubHandler(Body))))
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "super-secret-token");
            await client.SendAsync(req);
        }

        // The cassette on disk must not contain the token — sanitization by construction.
        var contents = Directory.EnumerateFiles(_dir, "*.json").Select(File.ReadAllText);
        Assert.DoesNotContain(contents, c => c.Contains("super-secret-token"));
    }

    // A fake "network": always returns 200 + the given JSON body.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
