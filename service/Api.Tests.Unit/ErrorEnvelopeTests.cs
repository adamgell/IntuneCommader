using System.Text.Json;
using CmProjectX.Api;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Hermetic tests for the uniform error envelope (ErrorEnvelope + ErrorDto). Pin the
// wire shape the global UseExceptionHandler / UseStatusCodePages / ApiResults all
// emit, so the thin Rust client's `body.error` read stays compatible and the
// contract's `Error` schema keeps its guarantees. No sidecar, no network.
public class ErrorEnvelopeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void FromException_5xx_HidesInternalsAndCarriesStatusAndTraceId()
    {
        var dto = ErrorEnvelope.FromException(
            new InvalidOperationException("secret stack detail"), 500, "trace-123");

        Assert.Equal(500, dto.Status);
        Assert.Equal("trace-123", dto.TraceId);
        Assert.DoesNotContain("secret", dto.Error);       // never leak internals on a 5xx
        Assert.False(string.IsNullOrWhiteSpace(dto.Error));
    }

    [Fact]
    public void FromException_Sub500_PassesTheMessageThrough()
    {
        var dto = ErrorEnvelope.FromException(new Exception("bad input"), 400, null);

        Assert.Equal(400, dto.Status);
        Assert.Equal("bad input", dto.Error);
        Assert.Null(dto.TraceId);
    }

    [Theory]
    [InlineData(409, "Conflict")]
    [InlineData(404, "Not Found")]
    [InlineData(400, "Bad Request")]
    public void FromStatus_UsesReasonPhrase(int status, string expected)
    {
        var dto = ErrorEnvelope.FromStatus(status, "t");

        Assert.Equal(status, dto.Status);
        Assert.Equal(expected, dto.Error);
        Assert.Equal("t", dto.TraceId);
    }

    [Fact]
    public void Envelope_SerializesCamelCaseWithTheContractKeys()
    {
        // The exact wire keys the Rust side (api_client.rs `body_error`) reads, plus
        // status/traceId. Guards against a PascalCase regression on the error path.
        var json = JsonSerializer.Serialize(
            new ErrorDto("not signed in", null, 409, "abc"), Web);

        Assert.Contains("\"error\":\"not signed in\"", json);
        Assert.Contains("\"status\":409", json);
        Assert.Contains("\"traceId\":\"abc\"", json);
        Assert.DoesNotContain("\"Error\"", json);
        Assert.DoesNotContain("\"Status\"", json);
    }
}
