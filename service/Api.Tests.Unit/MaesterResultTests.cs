using CmProjectX.Api;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Hermetic tests for the Maester result projection. These exercise the pure
// parse/categorize helpers (MaesterRunner.Parse / .Categorize) against a sample of
// Invoke-Maester's JSON shape — no PowerShell, network, or tenant required, so they
// run on every PR. The live run path (RunAsync → pwsh) is integration-only.
public class MaesterResultTests
{
    // A representative slice of Invoke-Maester -OutputJsonFile output: top-level
    // counts + a Tests array whose per-test fields mirror ConvertTo-MtMaesterResult
    // (Id, Title, Name, Result, Severity, Tag, Block, HelpUrl).
    private const string SampleJson = """
    {
      "Result": "Failed",
      "PassedCount": 2,
      "FailedCount": 1,
      "SkippedCount": 1,
      "TotalCount": 4,
      "Tests": [
        {
          "Id": "MT.1054",
          "Title": "Built-in Device Compliance Policy marks devices non-compliant",
          "Name": "MT.1054: Built-in Device Compliance Policy",
          "Result": "Passed",
          "Severity": "High",
          "Tag": ["CIS", "Intune"],
          "Block": "Maester/Intune",
          "HelpUrl": "https://maester.dev/docs/tests/MT.1054"
        },
        {
          "Id": "MT.1001",
          "Title": "Security Defaults or Conditional Access enabled",
          "Name": "MT.1001",
          "Result": "Failed",
          "Severity": "Critical",
          "Tag": ["CISA", "MS.AAD.1.1"],
          "Block": "Maester/Entra"
        },
        {
          "Id": "MT.1066",
          "Title": "EIDSCA: PIM alerts",
          "Name": "MT.1066",
          "Result": "Passed",
          "Tag": ["EIDSCA"],
          "Block": "Maester/Entra/EIDSCA"
        },
        {
          "Id": "MT.1500",
          "Title": "Exchange transport rule check",
          "Name": "MT.1500",
          "Result": "Skipped",
          "Tag": "ORCA",
          "Block": "Maester/Exchange Online"
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ProjectsCountsAndControls()
    {
        var r = MaesterRunner.Parse(SampleJson, executedAt: "2026-06-24T00:00:00Z");

        Assert.Equal(4, r.Total);
        Assert.Equal(2, r.Passed);
        Assert.Equal(1, r.Failed);
        Assert.Equal(1, r.Skipped); // the one Skipped control
        Assert.Equal("Failed", r.OverallResult);
        Assert.Equal(4, r.Controls.Count);
        Assert.Equal("2026-06-24T00:00:00Z", r.ExecutedAt);
    }

    [Fact]
    public void Parse_CategorizesByServiceFromTagsAndBlock()
    {
        var r = MaesterRunner.Parse(SampleJson, executedAt: null);
        var byId = r.Controls.ToDictionary(c => c.Id, c => c.Category);

        Assert.Equal("Intune", byId["MT.1054"]);
        Assert.Equal("Entra", byId["MT.1001"]);   // MS.AAD tag
        Assert.Equal("Entra", byId["MT.1066"]);   // EIDSCA tag
        Assert.Equal("Exchange", byId["MT.1500"]); // ORCA tag / Exchange block
    }

    [Fact]
    public void Parse_RollsUpCategoriesWithScores()
    {
        var r = MaesterRunner.Parse(SampleJson, executedAt: null);

        var entra = r.Categories.Single(c => c.Name == "Entra");
        Assert.Equal(2, entra.Total);
        Assert.Equal(1, entra.Passed);
        Assert.Equal(1, entra.Failed);
        Assert.Equal(50, entra.Score); // 1 passed / (1 passed + 1 failed)

        var intune = r.Categories.Single(c => c.Name == "Intune");
        Assert.Equal(100, intune.Score); // 1/1

        // A category with only skipped controls scores 100 (nothing applicable failed).
        var exchange = r.Categories.Single(c => c.Name == "Exchange");
        Assert.Equal(100, exchange.Score);
        Assert.Equal(1, exchange.Skipped);
    }

    [Fact]
    public void Parse_SortsControlsByIdForStableSnapshots()
    {
        var r = MaesterRunner.Parse(SampleJson, executedAt: null);
        var ids = r.Controls.Select(c => c.Id).ToList();
        var sorted = ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, ids);
    }

    [Theory]
    [InlineData("Intune")]
    [InlineData("Entra")]
    [InlineData("Exchange")]
    [InlineData("Defender")]
    [InlineData("Teams")]
    [InlineData("SharePoint")]
    public void Categorize_MatchesKnownServiceTags(string service)
    {
        Assert.Equal(service, MaesterRunner.Categorize(new[] { service }, block: null));
    }

    [Fact]
    public void Categorize_FallsBackToOther()
    {
        Assert.Equal("Other", MaesterRunner.Categorize(Array.Empty<string>(), block: "Maester/Misc"));
    }

    [Fact]
    public void Parse_HandlesMissingTestsArray()
    {
        var r = MaesterRunner.Parse("""{ "Result": "Passed" }""", executedAt: null);
        Assert.Equal(0, r.Total);
        Assert.Empty(r.Controls);
        Assert.Empty(r.Categories);
    }
}
