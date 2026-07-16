using System.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;

namespace CmProjectX.Api;

// ─── Uniform error envelope: the ONE non-2xx shape (ErrorDto) ─────────────────
// One place that maps any error into the shared ErrorDto { error, detail?, status,
// traceId? }. It backs the global UseExceptionHandler + UseStatusCodePages wired in
// Program.cs, and ApiResults (below) retrofits the endpoint-level error sites, so an
// unhandled exception, a bare framework status code, and an endpoint validation/
// conflict error all serialize identically. `error` stays the short human message the
// thin Rust client reads verbatim (app/src/api_client.rs body_error), so the new
// envelope is wire-compatible with the existing client — it just gains status/traceId.
//
// The mapping methods are pure + internal so Api.Tests.Unit can assert the shape
// (InternalsVisibleTo="Api.Tests.Unit"); the middleware and ApiResults only add the
// HttpContext plumbing (status code + traceId + JSON write) around them.
internal static class ErrorEnvelope
{
    // Map an unhandled exception to the envelope. NEVER leak internals for a 5xx —
    // the raw exception is logged server-side and the client gets a generic message;
    // sub-500 (rare on this path) passes the message through.
    internal static ErrorDto FromException(Exception ex, int status, string? traceId) =>
        new(
            Error: status >= 500 ? "An unexpected error occurred." : ex.Message,
            Detail: null,
            Status: status,
            TraceId: traceId);

    // Map a bare error status code (no body was written by the endpoint — e.g. a
    // signed-out Results.Conflict()) to the envelope, using the HTTP reason phrase.
    internal static ErrorDto FromStatus(int status, string? traceId) =>
        new(
            Error: ReasonPhrases.GetReasonPhrase(status) is { Length: > 0 } phrase ? phrase : $"HTTP {status}",
            Detail: null,
            Status: status,
            TraceId: traceId);

    // The correlation id for a response: the current W3C Activity id when telemetry
    // produced a span, else the ASP.NET request id — always something to correlate on.
    internal static string TraceId(HttpContext ctx) => Activity.Current?.Id ?? ctx.TraceIdentifier;
}

// ─── Endpoint-facing envelope results ─────────────────────────────────────────
// Drop-in replacements for the ad-hoc `Results.BadRequest(new { error = ... })`,
// `Results.Json(new { error = ... }, statusCode: 403)`, and `Results.Problem(...)`
// sites. Each emits the ErrorDto envelope with the right status code, stamping the
// traceId at write time (HttpContext-scoped) rather than at construction.
internal static class ApiResults
{
    internal static IResult Error(int status, string error, string? detail = null) =>
        new ErrorResult(status, error, detail);

    internal static IResult BadRequest(string error, string? detail = null) =>
        Error(StatusCodes.Status400BadRequest, error, detail);

    internal static IResult NotFound(string error, string? detail = null) =>
        Error(StatusCodes.Status404NotFound, error, detail);

    internal static IResult Conflict(string error, string? detail = null) =>
        Error(StatusCodes.Status409Conflict, error, detail);

    internal static IResult Forbidden(string error, string? detail = null) =>
        Error(StatusCodes.Status403Forbidden, error, detail);

    internal static IResult ServerError(string error, string? detail = null) =>
        Error(StatusCodes.Status500InternalServerError, error, detail);

    private sealed class ErrorResult(int status, string error, string? detail) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var dto = new ErrorDto(error, detail, status, ErrorEnvelope.TraceId(httpContext));
            httpContext.Response.StatusCode = status;
            await httpContext.Response.WriteAsJsonAsync(dto);
        }
    }
}
