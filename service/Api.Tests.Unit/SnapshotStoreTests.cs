using CmProjectX.Store;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Tier-1 invariant tests for the append-only time-machine store
// (service/Store/SnapshotStore.cs). These pin the load-bearing guarantees the
// whole product rests on: append-only history, SHA256 content-hash dedup, delta
// watermarks, and full-text search — all against a throwaway temp directory so
// the real %LocalAppData%\cmProjectX\ store is never touched.
public sealed class SnapshotStoreTests : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cmpx-rt1-" + Guid.NewGuid().ToString("N"));
    private SnapshotStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new SnapshotStore(_root);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        // Best effort: SQLite/Lucene may still hold file handles via pooling.
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
        return Task.CompletedTask;
    }

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ConfigSnapshotRecord Snap(string objectId, string body, DateTime capturedUtc, string? tenantId = null) =>
        new(SnapshotId: Guid.NewGuid().ToString("N"),
            ObjectId: objectId,
            ObjectType: "deviceConfiguration",
            ObjectName: "Test Policy",
            CapturedUtc: capturedUtc,
            BodyJson: body,
            TenantId: tenantId);

    // M20 — a pending change must carry its target tenant through the store so a
    // cross-tenant approval can be refused; legacy/unscoped changes stay null.
    [Fact]
    public async Task PendingChange_RoundTripsTenantId()
    {
        await _store.AppendPendingChangeAsync(new PendingChangeRecord(
            "pc-1", "fleet campaign x", "update", "/compliance-policies", "obj-1", "Policy",
            "{}", "[]", "pending", T0, TenantId: "tenant-a"));
        var withTenant = await _store.GetPendingChangeAsync("pc-1");
        Assert.NotNull(withTenant);
        Assert.Equal("tenant-a", withTenant!.TenantId);

        await _store.AppendPendingChangeAsync(new PendingChangeRecord(
            "pc-2", "MCP client", "update", "/settings-catalog", "obj-2", "Cfg",
            "{}", "[]", "pending", T0));
        var noTenant = await _store.GetPendingChangeAsync("pc-2");
        Assert.NotNull(noTenant);
        Assert.Null(noTenant!.TenantId);

        // And it survives the list path too (the inbox view scopes on this).
        var listed = await _store.GetPendingChangesAsync("pending");
        Assert.Equal("tenant-a", Assert.Single(listed, c => c.Id == "pc-1").TenantId);
    }

    private static AuditEventRecord Audit(
        string id, DateTime ts, string action = "update",
        string objectType = "deviceConfiguration", string? objectName = "Test Policy",
        string? actor = "tester", string objectId = "obj1", string? tenantId = null) =>
        new(id, ts, actor, action, objectType, objectId, objectName, tenantId);

    // ── Append-only + content-hash dedup ────────────────────────────────────

    [Fact]
    public async Task AppendSnapshot_FirstWrite_IsStoredWithHash()
    {
        var stored = await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":1}""", T0));

        Assert.NotNull(stored);
        Assert.False(string.IsNullOrEmpty(stored!.ContentHash)); // hash computed on store
        Assert.Single(await _store.GetSnapshotsForObjectAsync("obj1"));
    }

    [Fact]
    public async Task AppendSnapshot_IdenticalBody_IsDeduped()
    {
        await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":1}""", T0));
        var second = await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":1}""", T0.AddMinutes(5)));

        Assert.Null(second); // unchanged body → skipped, not appended
        Assert.Single(await _store.GetSnapshotsForObjectAsync("obj1"));
    }

    [Fact]
    public async Task AppendSnapshot_ChangedBody_AppendsNewestFirst()
    {
        await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":1}""", T0));
        await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":2}""", T0.AddMinutes(5)));

        var history = await _store.GetSnapshotsForObjectAsync("obj1");
        Assert.Equal(2, history.Count);
        Assert.Equal("""{"a":2}""", history[0].BodyJson); // newest first
        Assert.Equal("""{"a":1}""", history[1].BodyJson);
    }

    [Fact]
    public async Task AppendSnapshot_RevertToEarlierBody_StillAppends()
    {
        // Dedup only compares against the LATEST snapshot, so a→b→a is real history.
        await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":1}""", T0));
        await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":2}""", T0.AddMinutes(5)));
        var revert = await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":1}""", T0.AddMinutes(10)));

        Assert.NotNull(revert);
        Assert.Equal(3, (await _store.GetSnapshotsForObjectAsync("obj1")).Count);
    }

    [Fact]
    public async Task AppendSnapshot_SameObjectIdAcrossTenants_IsIsolated()
    {
        var tenantA = await _store.AppendSnapshotIfChangedAsync(
            Snap("shared", """{"a":1}""", T0, "tenant-a"));
        var tenantB = await _store.AppendSnapshotIfChangedAsync(
            Snap("shared", """{"a":1}""", T0.AddMinutes(1), "tenant-b"));
        var duplicateA = await _store.AppendSnapshotIfChangedAsync(
            Snap("shared", """{"a":1}""", T0.AddMinutes(2), "tenant-a"));

        Assert.NotNull(tenantA);
        Assert.NotNull(tenantB);
        Assert.Null(duplicateA);
        Assert.Equal("tenant-a", tenantA!.TenantId);
        Assert.Equal("tenant-b", tenantB!.TenantId);
        Assert.Single(await _store.GetSnapshotsForObjectAsync("shared", tenantId: "tenant-a"));
        Assert.Single(await _store.GetSnapshotsForObjectAsync("shared", tenantId: "tenant-b"));
        Assert.Equal(2, (await _store.GetSnapshotsForObjectAsync("shared")).Count);
    }

    // ── Audit log: append-only + idempotent by id ───────────────────────────

    [Fact]
    public async Task AppendAuditEvent_DuplicateId_IsIgnored()
    {
        await _store.AppendAuditEventAsync(Audit("evt1", T0, action: "update"));
        await _store.AppendAuditEventAsync(Audit("evt1", T0, action: "delete")); // same id, INSERT OR IGNORE

        var rows = await _store.QueryAuditAsync(null, null, null);
        Assert.Single(rows);
        Assert.Equal("update", rows[0].Action); // first write wins; history is immutable
    }

    [Fact]
    public async Task QueryAudit_FiltersByDateRange()
    {
        await _store.AppendAuditEventAsync(Audit("a", T0));
        await _store.AppendAuditEventAsync(Audit("b", T0.AddMonths(5)));

        var inRange = await _store.QueryAuditAsync(T0.AddMonths(4), null, null);
        Assert.Single(inRange);
        Assert.Equal("b", inRange[0].Id);
    }

    [Fact]
    public async Task QueryAudit_FiltersByKeyword()
    {
        await _store.AppendAuditEventAsync(Audit("a", T0, objectName: "Kiosk Policy"));
        await _store.AppendAuditEventAsync(Audit("b", T0.AddDays(1), objectName: "Baseline"));

        var hits = await _store.QueryAuditAsync(null, null, "kiosk");
        Assert.Single(hits);
        Assert.Equal("a", hits[0].Id);
    }

    [Fact]
    public async Task QueryAudit_RespectsTenantScope()
    {
        await _store.AppendAuditEventAsync(Audit("a", T0, objectName: "Policy A", tenantId: "tenant-a"));
        await _store.AppendAuditEventAsync(Audit("b", T0.AddDays(1), objectName: "Policy B", tenantId: "tenant-b"));

        var rows = await _store.QueryAuditAsync(null, null, null, tenantId: "tenant-a");

        var row = Assert.Single(rows);
        Assert.Equal("a", row.Id);
        Assert.Equal("tenant-a", row.TenantId);
    }

    // ── Delta watermarks (the one mutable table) ────────────────────────────

    [Fact]
    public async Task SyncState_RoundTripsAndOverwrites()
    {
        Assert.Null(await _store.GetSyncStateAsync("watermark"));

        await _store.SetSyncStateAsync("watermark", "2026-01-01T00:00:00Z");
        Assert.Equal("2026-01-01T00:00:00Z", await _store.GetSyncStateAsync("watermark"));

        await _store.SetSyncStateAsync("watermark", "2026-06-01T00:00:00Z"); // ON CONFLICT → update
        Assert.Equal("2026-06-01T00:00:00Z", await _store.GetSyncStateAsync("watermark"));
    }

    // ── Full-text search across the appended events ─────────────────────────

    [Fact]
    public async Task Search_FindsAppendedAuditEvent()
    {
        await _store.AppendAuditEventAsync(Audit("a", T0, actor: "alice", objectName: "Encryption Policy"));
        await _store.CommitIndexAsync();

        var hits = await _store.SearchAsync("Encryption");
        Assert.Contains(hits, h => h.Id == "a" && h.Kind == "AuditEvent");
    }

    [Fact]
    public async Task Search_AuditEvents_RespectsTenantScope()
    {
        await _store.AppendAuditEventAsync(Audit("a", T0, objectName: "Needle", tenantId: "tenant-a"));
        await _store.AppendAuditEventAsync(Audit("b", T0, objectName: "Needle", tenantId: "tenant-b"));
        await _store.CommitIndexAsync();

        var hits = await _store.SearchAsync("Needle", tenantId: "tenant-a");

        Assert.Contains(hits, h => h.Id == "a");
        Assert.DoesNotContain(hits, h => h.Id == "b");
    }

    [Fact]
    public async Task Search_ConfigSnapshots_RespectsTenantScope()
    {
        var a = await _store.AppendSnapshotIfChangedAsync(
            Snap("a", """{"displayName":"Needle"}""", T0, "tenant-a"));
        var b = await _store.AppendSnapshotIfChangedAsync(
            Snap("b", """{"displayName":"Needle"}""", T0, "tenant-b"));
        await _store.CommitIndexAsync();

        var hits = await _store.SearchAsync("Needle", tenantId: "tenant-a");

        Assert.Contains(hits, h => h.Id == a!.SnapshotId);
        Assert.DoesNotContain(hits, h => h.Id == b!.SnapshotId);
    }

    // ── Drift picker projection (count + two newest bodies in one query) ─────

    [Fact]
    public async Task SnapshottedObjects_ReportsCountAndTwoNewestBodies()
    {
        await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":1}""", T0));
        await _store.AppendSnapshotIfChangedAsync(Snap("obj1", """{"a":2}""", T0.AddMinutes(5)));

        var row = Assert.Single(await _store.GetSnapshottedObjectsAsync());
        Assert.Equal("obj1", row.ObjectId);
        Assert.Equal(2, row.SnapshotCount);
        Assert.Equal("""{"a":2}""", row.LatestBodyJson);   // body1 = newest
        Assert.Equal("""{"a":1}""", row.PreviousBodyJson); // body2 = next-newest (LEAD)
    }

    [Fact]
    public async Task SnapshottedObjects_RespectsTenantScope()
    {
        await _store.AppendSnapshotIfChangedAsync(Snap("shared", """{"a":1}""", T0, "tenant-a"));
        await _store.AppendSnapshotIfChangedAsync(Snap("shared", """{"a":2}""", T0.AddMinutes(5), "tenant-a"));
        await _store.AppendSnapshotIfChangedAsync(Snap("shared", """{"b":1}""", T0.AddMinutes(10), "tenant-b"));

        var tenantA = Assert.Single(await _store.GetSnapshottedObjectsAsync(tenantId: "tenant-a"));
        var tenantB = Assert.Single(await _store.GetSnapshottedObjectsAsync(tenantId: "tenant-b"));

        Assert.Equal(2, tenantA.SnapshotCount);
        Assert.Equal("""{"a":2}""", tenantA.LatestBodyJson);
        Assert.Equal("""{"a":1}""", tenantA.PreviousBodyJson);
        Assert.Equal(1, tenantB.SnapshotCount);
        Assert.Equal("""{"b":1}""", tenantB.LatestBodyJson);
        Assert.Null(tenantB.PreviousBodyJson);
    }
}
