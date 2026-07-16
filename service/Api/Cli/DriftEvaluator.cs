using CmProjectX.Store;

namespace CmProjectX.Api;

// The per-object drift projection shared by GET /objects and the headless drift-gate CLI,
// so the endpoint and the gate can never diverge. One store query returns each object's two
// newest bodies; drift is JsonDrift.Diff(previous, latest).Count computed in-memory.
public static class DriftEvaluator
{
    public static async Task<IReadOnlyList<DriftObjectDto>> EvaluateAsync(ISnapshotStore store, string? tenantId = null)
    {
        var objects = await store.GetSnapshottedObjectsAsync(tenantId: tenantId);
        return objects.Select(o =>
        {
            var changeCount = (o.SnapshotCount >= 2 && o.PreviousBodyJson is not null && o.LatestBodyJson is not null)
                ? JsonDrift.Diff(o.PreviousBodyJson, o.LatestBodyJson).Count // (older, newer) — matches /drift
                : 0;
            return new DriftObjectDto(
                o.ObjectId, o.ObjectType, o.ObjectName, o.SnapshotCount, o.LastCapturedUtc.ToString("o"), changeCount);
        })
        // Most-changed first (ChangeCount desc puts >0 ahead of 0), then newest.
        .OrderByDescending(d => d.ChangeCount)
        .ThenByDescending(d => d.LastCapturedUtc, StringComparer.Ordinal)
        .ToList();
    }
}
