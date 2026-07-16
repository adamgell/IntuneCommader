using System.Text.Json;
using CmProjectX.Store;
using Intune.Commander.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace CmProjectX.Sync;

// Incremental sync engine. Pulls Intune audit events + config object snapshots via
// Microsoft Graph and appends them to the durable time-machine store.
//
// NOTE ON "DELTA": most Intune config types do NOT support Graph $delta. Instead we
// get incrementality two ways:
//   - Audit events: a high-water mark on activityDateTime (verified filterable +
//     orderable on /deviceManagement/auditEvents) — we only pull events newer than
//     the last one we stored, per tenant.
//   - Config snapshots: a full list each run, but AppendSnapshotIfChangedAsync dedups
//     on a content hash of the normalized body, so an unchanged object is a no-op and
//     only real drift creates a new snapshot.
// Graph SDK middleware already honors 429 Retry-After, so throttling is handled for us.
public sealed class GraphDeltaSync
{
    private readonly ISnapshotStore _store;
    private readonly IExportNormalizer _normalizer;
    private readonly ILogger<GraphDeltaSync> _log;

    // Mirror the IC ExportService serialization shape (camelCase, indented) so the
    // ExportNormalizer — tuned to that output — produces stable, comparable bodies.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GraphDeltaSync(ISnapshotStore store, IExportNormalizer normalizer, ILogger<GraphDeltaSync> log)
    {
        _store = store;
        _normalizer = normalizer;
        _log = log;
    }

    public async Task<SyncResult> RunAsync(GraphServiceClient graph, string tenantId, CancellationToken ct = default)
    {
        var audit = await SyncAuditEventsAsync(graph, tenantId, ct);
        var snapshots = await SyncConfigSnapshotsAsync(graph, tenantId, ct);
        // One fsync for the whole batch — appends were searchable all along via NRT.
        await _store.CommitIndexAsync(ct);
        return new SyncResult(audit, snapshots);
    }

    private static string WatermarkKey(string tenantId) => $"audit_watermark:{tenantId}";

    // Pull audit events newer than the stored watermark, oldest-first, and advance the
    // watermark to the newest activityDateTime we saw. AppendAuditEventAsync is itself
    // idempotent (INSERT OR IGNORE on id), so an overlap on the boundary is harmless.
    private async Task<int> SyncAuditEventsAsync(GraphServiceClient graph, string tenantId, CancellationToken ct)
    {
        var key = WatermarkKey(tenantId);
        var since = await _store.GetSyncStateAsync(key, ct);
        var count = 0;
        DateTimeOffset? maxSeen = null;

        try
        {
            var response = await graph.DeviceManagement.AuditEvents.GetAsync(req =>
            {
                if (!string.IsNullOrEmpty(since))
                    req.QueryParameters.Filter = $"activityDateTime gt {since}";
                req.QueryParameters.Orderby = ["activityDateTime"];
                req.QueryParameters.Top = 1000;
            }, ct);

            while (response is not null)
            {
                foreach (var evt in response.Value ?? [])
                {
                    var record = MapAudit(evt, tenantId);
                    if (record is null) continue;
                    await _store.AppendAuditEventAsync(record, ct);
                    count++;
                    if (evt.ActivityDateTime is { } dt && (maxSeen is null || dt > maxSeen))
                        maxSeen = dt;
                }

                if (string.IsNullOrEmpty(response.OdataNextLink)) break;
                response = await graph.DeviceManagement.AuditEvents
                    .WithUrl(response.OdataNextLink)
                    .GetAsync(cancellationToken: ct);
            }

            if (maxSeen is { } newWatermark)
                await _store.SetSyncStateAsync(key, newWatermark.UtcDateTime.ToString("o"), ct);
        }
        catch (Exception ex)
        {
            // App-only credentials may lack DeviceManagementApps.Read.All; don't let a
            // permission/transient failure abort the whole sync.
            _log.LogWarning(ex, "Audit event sync failed (continuing)");
        }

        return count;
    }

    private static AuditEventRecord? MapAudit(AuditEvent evt, string tenantId)
    {
        if (evt.Id is null) return null;

        var resource = evt.Resources?.FirstOrDefault();
        var actor = evt.Actor?.UserPrincipalName
            ?? evt.Actor?.ApplicationDisplayName
            ?? evt.Actor?.ServicePrincipalName
            ?? evt.Actor?.UserId;

        return new AuditEventRecord(
            Id: evt.Id,
            Timestamp: (evt.ActivityDateTime ?? DateTimeOffset.UtcNow).UtcDateTime,
            Actor: actor,
            Action: evt.Activity ?? evt.DisplayName ?? evt.ActivityType ?? "Unknown",
            ObjectType: resource?.AuditResourceType ?? evt.Category ?? "Unknown",
            ObjectId: resource?.ResourceId ?? evt.Id,
            ObjectName: resource?.DisplayName,
            TenantId: tenantId);
    }

    // List-level snapshots for the headline config types. Each block is independent so a
    // permission gap on one type still captures the others. Compliance policies come back
    // as full bodies (expanded); apps/settings-catalog come back at the list ($select)
    // shape — enough for envelope drift in the MVP.
    private async Task<int> SyncConfigSnapshotsAsync(GraphServiceClient graph, string tenantId, CancellationToken ct)
    {
        var count = 0;

        count += await SnapshotTypeAsync(tenantId, "DeviceCompliancePolicy", async () =>
        {
            var items = await new CompliancePolicyService(graph).ListCompliancePoliciesAsync(ct);
            return items.Select(p => (p.Id, p.DisplayName, (object)p));
        }, ct);

        count += await SnapshotTypeAsync(tenantId, "SettingsCatalogPolicy", async () =>
        {
            var items = await new SettingsCatalogService(graph).ListSettingsCatalogPoliciesAsync(ct);
            return items.Select(p => (p.Id, p.Name, (object)p));
        }, ct);

        count += await SnapshotTypeAsync(tenantId, "MobileApp", async () =>
        {
            var items = await new ApplicationService(graph).ListApplicationsAsync(ct);
            return items.Select(a => (a.Id, a.DisplayName, (object)a));
        }, ct);

        return count;
    }

    private async Task<int> SnapshotTypeAsync(
        string tenantId,
        string objectType,
        Func<Task<IEnumerable<(string? Id, string? Name, object Body)>>> list,
        CancellationToken ct)
    {
        var stored = 0;
        try
        {
            foreach (var (id, name, body) in await list())
            {
                if (id is null) continue;
                if (await SnapshotAsync(tenantId, id, objectType, name, body, ct)) stored++;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Snapshot sync for {ObjectType} failed (continuing)", objectType);
        }
        return stored;
    }

    private async Task<bool> SnapshotAsync(
        string tenantId, string objectId, string objectType, string? objectName, object body, CancellationToken ct)
    {
        // Serialize with the runtime type so polymorphic Graph models (e.g. a
        // Windows10CompliancePolicy behind DeviceCompliancePolicy) emit their derived
        // properties, then normalize (strips id/version/timestamps, sorts keys).
        var raw = JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
        var normalized = _normalizer.NormalizeJson(raw);

        var record = new ConfigSnapshotRecord(
            SnapshotId: Guid.NewGuid().ToString("n"),
            ObjectId: objectId,
            ObjectType: objectType,
            ObjectName: objectName,
            CapturedUtc: DateTime.UtcNow,
            BodyJson: normalized,
            TenantId: tenantId);

        var result = await _store.AppendSnapshotIfChangedAsync(record, ct);
        return result is not null;
    }
}

public sealed record SyncResult(int AuditEvents, int Snapshots);
