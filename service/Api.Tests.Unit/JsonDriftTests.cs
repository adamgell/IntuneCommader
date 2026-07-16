using CmProjectX.Api;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 tests for the field-level drift engine (service/Api/JsonDrift.cs) — the
// same structural compare that powers the drift timeline and the M6 write-preview
// gate. Snapshot bodies arrive pre-normalized (volatile fields stripped, keys/
// arrays sorted), so any difference the engine reports is real drift, not noise.
public class JsonDriftTests
{
    [Fact]
    public void IdenticalBodies_ProduceNoChanges()
    {
        var changes = JsonDrift.Diff("""{"a":1,"b":"x"}""", """{"a":1,"b":"x"}""");
        Assert.Empty(changes);
    }

    [Fact]
    public void ModifiedLeaf_IsDetected()
    {
        var c = Assert.Single(JsonDrift.Diff("""{"a":1}""", """{"a":2}"""));
        Assert.Equal("/a", c.Path);
        Assert.Equal("Modified", c.Kind);
        Assert.Equal("1", c.Before?.ToString());
        Assert.Equal("2", c.After?.ToString());
    }

    [Fact]
    public void AddedKey_IsDetected()
    {
        var c = Assert.Single(JsonDrift.Diff("""{"a":1}""", """{"a":1,"b":2}"""));
        Assert.Equal("/b", c.Path);
        Assert.Equal("Added", c.Kind);
        Assert.Null(c.Before);
        Assert.Equal("2", c.After?.ToString());
    }

    [Fact]
    public void RemovedKey_IsDetected()
    {
        var c = Assert.Single(JsonDrift.Diff("""{"a":1,"b":2}""", """{"a":1}"""));
        Assert.Equal("/b", c.Path);
        Assert.Equal("Removed", c.Kind);
        Assert.Equal("2", c.Before?.ToString());
        Assert.Null(c.After);
    }

    [Fact]
    public void NestedObject_PathIsSlashJoined()
    {
        var c = Assert.Single(JsonDrift.Diff("""{"a":{"b":1}}""", """{"a":{"b":2}}"""));
        Assert.Equal("/a/b", c.Path);
        Assert.Equal("Modified", c.Kind);
    }

    [Fact]
    public void ArrayElement_ModifiedByIndex()
    {
        var c = Assert.Single(JsonDrift.Diff("""{"a":[1,2]}""", """{"a":[1,3]}"""));
        Assert.Equal("/a/1", c.Path);
        Assert.Equal("Modified", c.Kind);
    }

    [Fact]
    public void ArrayGrew_NewIndexIsAdded()
    {
        var c = Assert.Single(JsonDrift.Diff("""{"a":[1]}""", """{"a":[1,2]}"""));
        Assert.Equal("/a/1", c.Path);
        Assert.Equal("Added", c.Kind);
    }

    [Fact]
    public void TypeChange_ObjectToScalar_IsModifiedAtRoot()
    {
        // Top-level shape change: object → scalar compares by JSON text → Modified at "/".
        var c = Assert.Single(JsonDrift.Diff("""{"a":1}""", "5"));
        Assert.Equal("/", c.Path);
        Assert.Equal("Modified", c.Kind);
    }
}
