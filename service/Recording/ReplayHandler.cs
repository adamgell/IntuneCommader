using System.Net;
using System.Text;

namespace CmProjectX.Recording;

// Terminal HttpMessageHandler that serves recorded responses from a CassetteStore
// instead of hitting the network — the heart of offline, deterministic Tier-2
// runs. An UNMATCHED request is a hard failure: that surfaces missing coverage
// loudly instead of silently falling through to a live Graph call.
public sealed class ReplayHandler : HttpMessageHandler
{
    private readonly CassetteStore _store;

    public ReplayHandler(CassetteStore store) => _store = store;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var url = request.RequestUri?.ToString() ?? "";

        var cassette = _store.TryGet(method, url)
            ?? throw new InvalidOperationException(
                $"No cassette for {method} {url}. Record it first (record mode), or the " +
                "request isn't covered yet.");

        var response = new HttpResponseMessage((HttpStatusCode)cassette.Response.Status)
        {
            RequestMessage = request,
            Content = new StringContent(
                cassette.Response.Body, Encoding.UTF8,
                cassette.Response.ContentType ?? "application/json"),
        };
        return Task.FromResult(response);
    }
}
