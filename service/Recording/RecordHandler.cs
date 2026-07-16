using System.Text;

namespace CmProjectX.Recording;

// DelegatingHandler that passes each request through to the real inner handler,
// captures the response into the CassetteStore, and returns it. Only method/url
// and response status/content-type/body are persisted — NEVER request auth
// headers or cookies, so a committed cassette can't leak a token (sanitization by
// construction). The body is buffered so capturing it here doesn't break the
// caller's own read of the response.
public sealed class RecordHandler : DelegatingHandler
{
    private readonly CassetteStore _store;

    public RecordHandler(CassetteStore store, HttpMessageHandler innerHandler)
        : base(innerHandler) => _store = store;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        var body = response.Content is null
            ? ""
            : await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content?.Headers.ContentType?.MediaType;

        _store.Save(new Cassette(
            new CassetteRequest(request.Method.Method, request.RequestUri?.ToString() ?? ""),
            new CassetteResponse((int)response.StatusCode, contentType, body)));

        // Re-attach a fresh buffered body so the caller can still read it.
        response.Content = new StringContent(body, Encoding.UTF8, contentType ?? "application/json");
        return response;
    }
}
