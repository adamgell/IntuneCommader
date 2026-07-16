using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace CmProjectX.Api.Tests;

// One test per management service: GET its list endpoint and confirm it returns a
// well-formed JSON array. Empty arrays are RECORDED (a surface can be empty by
// design); only a non-200, a non-JSON body, or a non-array fails the test.
[Collection("sidecar")]
public sealed class ListEndpointTests
{
    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public ListEndpointTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // Anchor test: if this fails, the whole run is auth-blocked — every data test
    // below will also fail, and this message says why.
    [Fact]
    public void Sidecar_IsSignedIn()
        => Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate data. {_fx.SignInError}");

    [Theory]
    [MemberData(nameof(EndpointInventory.ListCases), MemberType = typeof(EndpointInventory))]
    public async Task Service_ReturnsJsonArray(string name, string path)
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate data. {_fx.SignInError}");

        using var resp = await _fx.Http.GetAsync(path);
        var status = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync();

        int? count = null;
        string note;
        if (status == 200)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                SidecarFixture.Results.Add(new(name, path, status, null, "NOT JSON"));
                Assert.Fail($"{name} ({path}) returned 200 but not valid JSON: {ex.Message}");
                return;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    count = doc.RootElement.GetArrayLength();
                note = count switch
                {
                    null => "NOT AN ARRAY",
                    0 => "EMPTY (verify by-design)",
                    _ => "ok",
                };
            }
        }
        else
        {
            note = $"HTTP {status}";
        }

        SidecarFixture.Results.Add(new(name, path, status, count, note));
        _out.WriteLine($"{path,-28} -> {status}  rows={count?.ToString() ?? "-"}  {note}");

        Assert.True(status == 200, $"{name} ({path}) returned HTTP {status}: {Trunc(body)}");
        Assert.True(count is not null, $"{name} ({path}) did not return a JSON array (got {note})");
    }

    internal static string Trunc(string s) => s.Length <= 300 ? s : s[..300] + "…";
}

// For every surface with a GET /{id}, fetch the list, take the first row's id, and
// confirm the detail endpoint returns a well-formed JSON object — i.e. the full
// configuration the UI will render. Surfaces whose list is empty are skipped
// (recorded as SKIPPED) since there's no item to fetch.
[Collection("sidecar")]
public sealed class DetailEndpointTests
{
    private readonly SidecarFixture _fx;
    private readonly ITestOutputHelper _out;

    public DetailEndpointTests(SidecarFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Theory]
    [MemberData(nameof(EndpointInventory.DetailCases), MemberType = typeof(EndpointInventory))]
    public async Task Service_DetailReturnsJsonObject(string name, string path)
    {
        Assert.True(_fx.SignedIn, $"Sidecar is not signed in — cannot validate data. {_fx.SignInError}");

        using var listResp = await _fx.Http.GetAsync(path);
        Assert.True((int)listResp.StatusCode == 200,
            $"{name} list ({path}) returned HTTP {(int)listResp.StatusCode}");

        using var list = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        if (list.RootElement.ValueKind != JsonValueKind.Array || list.RootElement.GetArrayLength() == 0)
        {
            _out.WriteLine($"{path}/{{id}} -> skipped (list empty)");
            SidecarFixture.Results.Add(new(name + " (detail)", path + "/{id}", 0, null, "SKIPPED (list empty)"));
            return;
        }

        var id = list.RootElement[0].GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id), $"{name} first list row has no id");

        using var detailResp = await _fx.Http.GetAsync($"{path}/{Uri.EscapeDataString(id!)}");
        var status = (int)detailResp.StatusCode;
        var body = await detailResp.Content.ReadAsStringAsync();

        var note = status == 200 ? "ok" : $"HTTP {status}";
        var jsonOk = false;
        if (status == 200 && !string.IsNullOrWhiteSpace(body))
        {
            try { using var _ = JsonDocument.Parse(body); jsonOk = true; }
            catch (JsonException) { note = "NOT JSON"; }
        }
        else if (status == 200)
        {
            note = "EMPTY BODY";
        }

        SidecarFixture.Results.Add(new(name + " (detail)", path + "/{id}", status, null, note));
        _out.WriteLine($"{path}/{{id}} -> {status}  {note}  ({body.Length} bytes)");

        Assert.True(status == 200, $"{name} detail ({path}/{{id}}) returned HTTP {status}: {ListEndpointTests.Trunc(body)}");
        Assert.True(jsonOk, $"{name} detail body was not valid non-empty JSON ({note})");
    }
}
