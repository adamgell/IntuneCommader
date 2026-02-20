using IntuneManager.Core.Models;
using IntuneManager.Core.Services;

namespace IntuneManager.Core.Tests.Services;

public class ProfileImportHelperTests
{
    // ── IsValid ────────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_AllRequiredFieldsPresent_ReturnsTrue()
    {
        var profile = MakeProfile("P1");
        Assert.True(ProfileImportHelper.IsValid(profile));
    }

    [Fact]
    public void IsValid_MissingName_ReturnsFalse()
    {
        var profile = MakeProfile("");
        Assert.False(ProfileImportHelper.IsValid(profile));
    }

    [Fact]
    public void IsValid_MissingTenantId_ReturnsFalse()
    {
        var profile = MakeProfile("P1");
        profile.TenantId = "";
        Assert.False(ProfileImportHelper.IsValid(profile));
    }

    [Fact]
    public void IsValid_MissingClientId_ReturnsFalse()
    {
        var profile = MakeProfile("P1");
        profile.ClientId = "";
        Assert.False(ProfileImportHelper.IsValid(profile));
    }

    [Fact]
    public void IsValid_WhitespaceOnlyName_ReturnsFalse()
    {
        var profile = MakeProfile("   ");
        Assert.False(ProfileImportHelper.IsValid(profile));
    }

    // ── ParseProfiles — array shape ────────────────────────────────────────

    [Fact]
    public void ParseProfiles_ArrayOfProfiles_ReturnsAll()
    {
        var json = """
            [
              { "name": "A", "tenantId": "00000000-0000-0000-0000-000000000001", "clientId": "00000000-0000-0000-0000-000000000002" },
              { "name": "B", "tenantId": "00000000-0000-0000-0000-000000000003", "clientId": "00000000-0000-0000-0000-000000000004" }
            ]
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].Name);
        Assert.Equal("B", result[1].Name);
    }

    [Fact]
    public void ParseProfiles_ArrayWithInvalidEntry_SkipsInvalid()
    {
        var json = """
            [
              { "name": "Good", "tenantId": "00000000-0000-0000-0000-000000000001", "clientId": "00000000-0000-0000-0000-000000000002" },
              { "name": "", "tenantId": "00000000-0000-0000-0000-000000000003", "clientId": "00000000-0000-0000-0000-000000000004" }
            ]
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Single(result);
        Assert.Equal("Good", result[0].Name);
    }

    [Fact]
    public void ParseProfiles_EmptyArray_ReturnsEmpty()
    {
        var result = ProfileImportHelper.ParseProfiles("[]");
        Assert.Empty(result);
    }

    // ── ParseProfiles — single object shape ───────────────────────────────

    [Fact]
    public void ParseProfiles_SingleObject_ReturnsSingleProfile()
    {
        var json = """
            { "name": "Solo", "tenantId": "00000000-0000-0000-0000-000000000001", "clientId": "00000000-0000-0000-0000-000000000002" }
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Single(result);
        Assert.Equal("Solo", result[0].Name);
    }

    [Fact]
    public void ParseProfiles_SingleObjectMissingRequiredField_ReturnsEmpty()
    {
        var json = """
            { "tenantId": "00000000-0000-0000-0000-000000000001", "clientId": "00000000-0000-0000-0000-000000000002" }
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Empty(result);
    }

    // ── ParseProfiles — ProfileStore envelope shape ────────────────────────

    [Fact]
    public void ParseProfiles_ProfileStoreEnvelope_ReturnsProfiles()
    {
        var json = """
            {
              "profiles": [
                { "name": "Env1", "tenantId": "00000000-0000-0000-0000-000000000001", "clientId": "00000000-0000-0000-0000-000000000002" },
                { "name": "Env2", "tenantId": "00000000-0000-0000-0000-000000000003", "clientId": "00000000-0000-0000-0000-000000000004" }
              ]
            }
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("Env1", result[0].Name);
        Assert.Equal("Env2", result[1].Name);
    }

    [Fact]
    public void ParseProfiles_ProfileStoreEnvelopeEmptyArray_ReturnsEmpty()
    {
        var result = ProfileImportHelper.ParseProfiles("""{ "profiles": [] }""");
        Assert.Empty(result);
    }

    // ── ParseProfiles — field mapping ─────────────────────────────────────

    [Fact]
    public void ParseProfiles_CloudFieldMapped_ParsesCorrectly()
    {
        var json = """
            [{ "name": "GCCTest", "tenantId": "00000000-0000-0000-0000-000000000001", "clientId": "00000000-0000-0000-0000-000000000002", "cloud": "GCCHigh" }]
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Single(result);
        Assert.Equal(CloudEnvironment.GCCHigh, result[0].Cloud);
    }

    [Fact]
    public void ParseProfiles_AuthMethodClientSecret_ParsesCorrectly()
    {
        var json = """
            [{ "name": "SP", "tenantId": "00000000-0000-0000-0000-000000000001", "clientId": "00000000-0000-0000-0000-000000000002", "authMethod": "ClientSecret", "clientSecret": "abc123" }]
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Single(result);
        Assert.Equal(AuthMethod.ClientSecret, result[0].AuthMethod);
        Assert.Equal("abc123", result[0].ClientSecret);
    }

    [Fact]
    public void ParseProfiles_CaseInsensitiveFieldNames_Parses()
    {
        var json = """
            [{ "Name": "CaseTest", "TenantId": "00000000-0000-0000-0000-000000000001", "ClientId": "00000000-0000-0000-0000-000000000002" }]
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Single(result);
        Assert.Equal("CaseTest", result[0].Name);
    }

    // ── ParseProfiles — error / edge cases ────────────────────────────────

    [Fact]
    public void ParseProfiles_InvalidJson_ThrowsJsonException()
    {
        // JsonReaderException is a subclass of JsonException
        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => ProfileImportHelper.ParseProfiles("not valid json {{{"));
    }

    [Fact]
    public void ParseProfiles_NullValueInArray_SkipsNulls()
    {
        var json = """
            [
              { "name": "Real", "tenantId": "00000000-0000-0000-0000-000000000001", "clientId": "00000000-0000-0000-0000-000000000002" },
              null
            ]
            """;

        var result = ProfileImportHelper.ParseProfiles(json);

        Assert.Single(result);
    }

    [Fact]
    public void ParseProfiles_UnrecognizedJsonShape_ReturnsEmpty()
    {
        // A JSON primitive (number) — not array or object
        var result = ProfileImportHelper.ParseProfiles("42");
        Assert.Empty(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static TenantProfile MakeProfile(string name) => new()
    {
        Name = name,
        TenantId = Guid.NewGuid().ToString(),
        ClientId = Guid.NewGuid().ToString()
    };
}
