using System.Security.Cryptography;
using System.Text;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Data.Sqlite;

namespace CmProjectX.Store;

// The DURABLE system-of-record for the audit/drift time-machine.
// IMPORTANT: this is NOT the IC LiteDB cache (that 24h-TTL cache is a Graph
// accelerator). This store is APPEND-ONLY and never expires — that permanence
// is the whole value prop (outliving Microsoft's audit retention).
//
// SQLite holds the append-only event log + config-snapshot history; Lucene.NET
// provides full-text search across both. The only mutable table is sync_state
// (delta watermarks), which is bookkeeping, not history.
public interface ISnapshotStore
{
    Task InitializeAsync(CancellationToken ct = default);

    // Append-only writes (never mutate/delete history).
    Task AppendAuditEventAsync(AuditEventRecord evt, CancellationToken ct = default);

    // Stores a new snapshot only when the (already-normalized) body differs from
    // the latest snapshot for the same object. Returns the stored record, or null
    // if the content was unchanged. This is what makes the history a drift log
    // rather than a firehose of identical copies.
    Task<ConfigSnapshotRecord?> AppendSnapshotIfChangedAsync(ConfigSnapshotRecord snapshot, CancellationToken ct = default);

    // Flush the full-text index to disk. Appends keep their new docs searchable via the
    // near-real-time reader without committing, so callers batch many appends and call
    // this once (e.g. at the end of a sync) instead of paying an fsync per document.
    Task CommitIndexAsync(CancellationToken ct = default);

    // Reads for the timeline / drift / search surfaces.
    Task<IReadOnlyList<AuditEventRecord>> QueryAuditAsync(
        DateTime? from, DateTime? to, string? q, int limit = 500, CancellationToken ct = default, string? tenantId = null);

    Task<ConfigSnapshotRecord?> GetSnapshotAsync(string snapshotId, CancellationToken ct = default);
    Task<IReadOnlyList<ConfigSnapshotRecord>> GetSnapshotsForObjectAsync(
        string objectId, int limit = 50, CancellationToken ct = default, string? tenantId = null);

    // One row per object that has config-snapshot history (newest captured first),
    // for the drift picker. SnapshotCount >= 2 means there's something to diff.
    Task<IReadOnlyList<SnapshottedObject>> GetSnapshottedObjectsAsync(
        int limit = 500, CancellationToken ct = default, string? tenantId = null);

    // Delta watermarks (e.g. last audit activityDateTime per tenant).
    Task<string?> GetSyncStateAsync(string key, CancellationToken ct = default);
    Task SetSyncStateAsync(string key, string value, CancellationToken ct = default);

    // Full-text across audit events and config snapshots.
    Task<IReadOnlyList<SearchHit>> SearchAsync(string q, int limit = 50, CancellationToken ct = default, string? tenantId = null);

    // ── Pending AI changes (M13.2) — the human-in-the-loop write queue ──────
    // Operational (mutable) state, NOT history: a write an MCP client PROPOSED,
    // parked until the operator approves it in the app. The durable record of an
    // approved write still lands in the append-only audit log on apply.
    Task AppendPendingChangeAsync(PendingChangeRecord change, CancellationToken ct = default);
    Task<IReadOnlyList<PendingChangeRecord>> GetPendingChangesAsync(
        string? state = "pending", int limit = 200, CancellationToken ct = default);
    Task<PendingChangeRecord?> GetPendingChangeAsync(string id, CancellationToken ct = default);
    Task SetPendingChangeStateAsync(
        string id, string state, string? note, DateTime decidedUtc, CancellationToken ct = default);

    // ── M18 Autonomy runs — the append-only loop-history log ────────────────
    // Each scheduled tick appends one row (watched/detected/proposed/decision/verify),
    // serialized as the AutonomyRunDto JSON. Like audit_events this is HISTORY: a run
    // is appended once finished, then upserted in place ONLY to fill in the
    // decision/verify chain a later tick observes (the same physical run, completed) —
    // it is never deleted. Queryable via GET /autonomy/runs(/{id}).
    Task UpsertAutonomyRunAsync(AutonomyRunRecord run, CancellationToken ct = default);
    Task<IReadOnlyList<AutonomyRunRecord>> GetAutonomyRunsAsync(int limit = 50, CancellationToken ct = default);
    Task<AutonomyRunRecord?> GetAutonomyRunAsync(string runId, CancellationToken ct = default);
}

public sealed record AuditEventRecord(
    string Id, DateTime Timestamp, string? Actor, string Action,
    string ObjectType, string ObjectId, string? ObjectName,
    string? TenantId = null);

public sealed record ConfigSnapshotRecord(
    string SnapshotId, string ObjectId, string ObjectType, string? ObjectName,
    DateTime CapturedUtc, string BodyJson, string? ContentHash = null,
    string? TenantId = null);

public sealed record SearchHit(string Kind, string Id, float Score, string Summary);

public sealed record SnapshottedObject(
    string ObjectId, string ObjectType, string? ObjectName, int SnapshotCount, DateTime LastCapturedUtc,
    // The two newest snapshot bodies (newest first), carried so the drift picker
    // can compute changeCount in-memory without a per-object follow-up query.
    // PreviousBodyJson is null when only one snapshot exists.
    string? LatestBodyJson = null, string? PreviousBodyJson = null);

// A write an MCP client proposed, parked for operator approval (M13.2). `Kind` is
// create|update|delete|assign (the apply path derives the verb+URL from it + Path +
// ObjectId). `DiffJson` is the DriftChange[] the AI saw, shown verbatim in the
// approval inbox. `State` is pending|applied|rejected|failed.
public sealed record PendingChangeRecord(
    string Id, string Proposer, string Kind, string Path,
    string? ObjectId, string? ObjectName, string? BodyJson, string DiffJson,
    string State, DateTime CreatedUtc, DateTime? DecidedUtc = null, string? Note = null,
    string? SimulationJson = null,
    // M20 — the tenant this change targets. Stamped at enqueue (the active profile, or the
    // fleet campaign's target tenant); approval is refused unless the active tenant matches,
    // so a cross-tenant replay can't write to the wrong tenant. Null = legacy/unscoped.
    string? TenantId = null);

// One M18 autonomy loop run. `RunJson` is the serialized AutonomyRunDto (the whole
// watched/detected/proposed/decision/verify chain); the columns are just the index
// keys for the runs list (newest-first by StartedUtc). Append-only history: a run is
// written once finished, then upserted in place only to record the operator's
// decision + the verify result a later tick observes — never deleted.
public sealed record AutonomyRunRecord(
    string RunId, string? TenantId, DateTime StartedUtc, string RunJson);

public sealed class SnapshotStore : ISnapshotStore, IDisposable
{
    private readonly string _dbPath;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    private SqliteConnection? _db;
    private FSDirectory? _luceneDir;
    private StandardAnalyzer? _analyzer;
    private IndexWriter? _writer;
    private bool _initialized;

    public SnapshotStore() : this(null) { }

    // storeRoot lets hermetic tests (and future Tier-2 harnesses, see
    // docs/REGRESSION-TESTING.md) redirect the SQLite DB + Lucene index to an
    // isolated directory instead of the shared %LocalAppData%\cmProjectX\ store.
    // Production passes null → the real per-user path (unchanged behavior).
    public SnapshotStore(string? storeRoot)
    {
        var root = storeRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cmProjectX");
        System.IO.Directory.CreateDirectory(root);
        _dbPath = Path.Combine(root, "timemachine.db");
        _indexPath = Path.Combine(root, "index");
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            _db = new SqliteConnection($"Data Source={_dbPath}");
            _db.Open();
            Execute("PRAGMA journal_mode=WAL;");
            Execute("""
                CREATE TABLE IF NOT EXISTS audit_events (
                    id TEXT PRIMARY KEY,
                    timestamp TEXT NOT NULL,
                    actor TEXT,
                    action TEXT NOT NULL,
                    object_type TEXT NOT NULL,
                    object_id TEXT NOT NULL,
                    object_name TEXT,
                    tenant_id TEXT
                );
                """);
            Execute("CREATE INDEX IF NOT EXISTS ix_audit_ts ON audit_events(timestamp);");
            try { Execute("ALTER TABLE audit_events ADD COLUMN tenant_id TEXT;"); }
            catch (SqliteException) { /* column already exists */ }
            Execute("CREATE INDEX IF NOT EXISTS ix_audit_tenant_ts ON audit_events(tenant_id, timestamp);");
            Execute("""
                CREATE TABLE IF NOT EXISTS config_snapshots (
                    snapshot_id TEXT PRIMARY KEY,
                    object_id TEXT NOT NULL,
                    object_type TEXT NOT NULL,
                    object_name TEXT,
                    captured_utc TEXT NOT NULL,
                    body_json TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    tenant_id TEXT
                );
                """);
            Execute("CREATE INDEX IF NOT EXISTS ix_snap_obj ON config_snapshots(object_id, captured_utc);");
            // M20.1 — tenant-bind config snapshots. Older DBs predate the column;
            // keep them readable as legacy/unscoped rows while all new writes are
            // stamped by the active tenant.
            try { Execute("ALTER TABLE config_snapshots ADD COLUMN tenant_id TEXT;"); }
            catch (SqliteException) { /* column already exists */ }
            Execute("CREATE INDEX IF NOT EXISTS ix_snap_tenant_obj ON config_snapshots(tenant_id, object_id, captured_utc);");
            Execute("""
                CREATE TABLE IF NOT EXISTS sync_state (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """);
            // M13.2 — pending AI-proposed writes (mutable operational state, like
            // sync_state; the approved write's permanent record lands in audit_events).
            Execute("""
                CREATE TABLE IF NOT EXISTS pending_changes (
                    id TEXT PRIMARY KEY,
                    proposer TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    path TEXT NOT NULL,
                    object_id TEXT,
                    object_name TEXT,
                    body_json TEXT,
                    diff_json TEXT NOT NULL,
                    state TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    decided_utc TEXT,
                    note TEXT,
                    simulation_json TEXT,
                    tenant_id TEXT
                );
                """);
            Execute("CREATE INDEX IF NOT EXISTS ix_pending_state ON pending_changes(state, created_utc);");
            // M16 — carry a blast-radius report on each pending change. Best-effort
            // migration for DBs created before M16 (ALTER throws "duplicate column" if
            // already present → ignore; the CREATE above already has it for fresh DBs).
            try { Execute("ALTER TABLE pending_changes ADD COLUMN simulation_json TEXT;"); }
            catch (Exception) { /* column already exists */ }
            // M20 — tenant-bind pending changes so a cross-tenant approval can be refused.
            // Older DBs predate the column; ADD COLUMN is idempotent-guarded (the CREATE
            // above already includes it on a fresh DB, so the ALTER then no-ops).
            try { Execute("ALTER TABLE pending_changes ADD COLUMN tenant_id TEXT;"); }
            catch (SqliteException) { /* column already exists */ }
            // M18 — autonomy loop run history (append-only; row_json is the full
            // AutonomyRunDto chain, columns are list index keys). Distinct from
            // audit_events: this is the loop's own structured trace, not the Graph
            // audit feed. The append-only audit_events still gets the permanent
            // record of any APPLIED write (via /pending-changes/{id}/approve).
            Execute("""
                CREATE TABLE IF NOT EXISTS autonomy_runs (
                    run_id TEXT PRIMARY KEY,
                    tenant_id TEXT,
                    started_utc TEXT NOT NULL,
                    run_json TEXT NOT NULL
                );
                """);
            Execute("CREATE INDEX IF NOT EXISTS ix_autonomy_started ON autonomy_runs(started_utc);");

            _analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            _luceneDir = FSDirectory.Open(_indexPath);
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND
            };
            try
            {
                _writer = new IndexWriter(_luceneDir, config);
            }
            catch (LockObtainFailedException ex)
            {
                // Lucene allows exactly one writer per index directory; the lock
                // being held means another process has the store open. (Program.cs
                // has a single-instance mutex guard, but the index can also be held
                // by a non-sidecar process, e.g. a tool poking at the index dir.)
                throw new InvalidOperationException(
                    $"Couldn't open the search index at {_indexPath} — another cmProjectX " +
                    "service instance appears to be running. Stop it and try again.", ex);
            }
            _writer.Commit(); // ensure the index exists for the first searcher

            _initialized = true;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task AppendAuditEventAsync(AuditEventRecord evt, CancellationToken ct = default)
    {
        var tenantId = ScopeTenant(evt.TenantId);
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO audit_events
                    (id, timestamp, actor, action, object_type, object_id, object_name, tenant_id)
                VALUES ($id, $ts, $actor, $action, $otype, $oid, $oname, $tenant);
                """;
            cmd.Parameters.AddWithValue("$id", evt.Id);
            cmd.Parameters.AddWithValue("$ts", evt.Timestamp.ToUniversalTime().ToString("o"));
            cmd.Parameters.AddWithValue("$actor", (object?)evt.Actor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$action", evt.Action);
            cmd.Parameters.AddWithValue("$otype", evt.ObjectType);
            cmd.Parameters.AddWithValue("$oid", evt.ObjectId);
            cmd.Parameters.AddWithValue("$oname", (object?)evt.ObjectName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tenant", (object?)tenantId ?? DBNull.Value);
            var rows = cmd.ExecuteNonQuery();

            if (rows > 0)
            {
                var text = $"{evt.Actor} {evt.Action} {evt.ObjectType} {evt.ObjectName} {evt.ObjectId}";
                var summary = $"{evt.Action} {evt.ObjectType}: {evt.ObjectName ?? evt.ObjectId}";
                IndexDoc("AuditEvent", evt.Id, text, summary, tenantId);
            }
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<ConfigSnapshotRecord?> AppendSnapshotIfChangedAsync(
        ConfigSnapshotRecord snapshot, CancellationToken ct = default)
    {
        var hash = Sha256(snapshot.BodyJson);
        var tenantId = ScopeTenant(snapshot.TenantId);

        await _dbLock.WaitAsync(ct);
        try
        {
            // Change detection: skip if the newest snapshot for this object matches.
            using (var latest = Db.CreateCommand())
            {
                latest.CommandText = tenantId is null
                    ? """
                        SELECT content_hash FROM config_snapshots
                        WHERE object_id = $oid AND tenant_id IS NULL
                        ORDER BY captured_utc DESC LIMIT 1;
                        """
                    : """
                        SELECT content_hash FROM config_snapshots
                        WHERE object_id = $oid AND tenant_id = $tenant
                        ORDER BY captured_utc DESC LIMIT 1;
                        """;
                latest.Parameters.AddWithValue("$oid", snapshot.ObjectId);
                if (tenantId is not null) latest.Parameters.AddWithValue("$tenant", tenantId);
                if (latest.ExecuteScalar() is string prevHash && prevHash == hash)
                    return null; // unchanged — nothing to append
            }

            var stored = snapshot with { ContentHash = hash, TenantId = tenantId };
            using (var cmd = Db.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO config_snapshots
                        (snapshot_id, object_id, object_type, object_name, captured_utc, body_json, content_hash, tenant_id)
                    VALUES ($sid, $oid, $otype, $oname, $cap, $body, $hash, $tenant);
                    """;
                cmd.Parameters.AddWithValue("$sid", stored.SnapshotId);
                cmd.Parameters.AddWithValue("$oid", stored.ObjectId);
                cmd.Parameters.AddWithValue("$otype", stored.ObjectType);
                cmd.Parameters.AddWithValue("$oname", (object?)stored.ObjectName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$cap", stored.CapturedUtc.ToUniversalTime().ToString("o"));
                cmd.Parameters.AddWithValue("$body", stored.BodyJson);
                cmd.Parameters.AddWithValue("$hash", hash);
                cmd.Parameters.AddWithValue("$tenant", (object?)stored.TenantId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            var text = $"{stored.ObjectType} {stored.ObjectName} {stored.BodyJson}";
            var summary = $"{stored.ObjectType}: {stored.ObjectName ?? stored.ObjectId} @ {stored.CapturedUtc:u}";
            IndexDoc("ConfigSnapshot", stored.SnapshotId, text, summary, stored.TenantId);

            return stored;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEventRecord>> QueryAuditAsync(
        DateTime? from, DateTime? to, string? q, int limit = 500, CancellationToken ct = default, string? tenantId = null)
    {
        tenantId = ScopeTenant(tenantId);
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            var sql = new StringBuilder("SELECT id, timestamp, actor, action, object_type, object_id, object_name, tenant_id FROM audit_events WHERE 1=1");
            if (from is not null)
            {
                sql.Append(" AND timestamp >= $from");
                cmd.Parameters.AddWithValue("$from", from.Value.ToUniversalTime().ToString("o"));
            }
            if (to is not null)
            {
                sql.Append(" AND timestamp <= $to");
                cmd.Parameters.AddWithValue("$to", to.Value.ToUniversalTime().ToString("o"));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                sql.Append(" AND (action LIKE $q OR object_name LIKE $q OR actor LIKE $q OR object_type LIKE $q)");
                cmd.Parameters.AddWithValue("$q", $"%{q}%");
            }
            if (tenantId is not null)
            {
                sql.Append(" AND tenant_id = $tenant");
                cmd.Parameters.AddWithValue("$tenant", tenantId);
            }
            sql.Append(" ORDER BY timestamp DESC LIMIT $limit;");
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.CommandText = sql.ToString();

            var results = new List<AuditEventRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new AuditEventRecord(
                    reader.GetString(0),
                    DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
            return results;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<ConfigSnapshotRecord?> GetSnapshotAsync(string snapshotId, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = """
                SELECT snapshot_id, object_id, object_type, object_name, captured_utc, body_json, content_hash, tenant_id
                FROM config_snapshots WHERE snapshot_id = $sid;
                """;
            cmd.Parameters.AddWithValue("$sid", snapshotId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadSnapshot(reader) : null;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<IReadOnlyList<ConfigSnapshotRecord>> GetSnapshotsForObjectAsync(
        string objectId, int limit = 50, CancellationToken ct = default, string? tenantId = null)
    {
        tenantId = ScopeTenant(tenantId);
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            var sql = new StringBuilder("""
                SELECT snapshot_id, object_id, object_type, object_name, captured_utc, body_json, content_hash, tenant_id
                FROM config_snapshots WHERE object_id = $oid
                """);
            if (tenantId is not null)
            {
                sql.Append(" AND tenant_id = $tenant");
                cmd.Parameters.AddWithValue("$tenant", tenantId);
            }
            sql.Append(" ORDER BY captured_utc DESC LIMIT $limit;");
            cmd.CommandText = sql.ToString();
            cmd.Parameters.AddWithValue("$oid", objectId);
            cmd.Parameters.AddWithValue("$limit", limit);
            var results = new List<ConfigSnapshotRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(ReadSnapshot(reader));
            return results;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<IReadOnlyList<SnapshottedObject>> GetSnapshottedObjectsAsync(
        int limit = 500, CancellationToken ct = default, string? tenantId = null)
    {
        tenantId = ScopeTenant(tenantId);
        await _dbLock.WaitAsync(ct);
        try
        {
            // Latest row per object (for the current object_name), plus how many
            // snapshots exist, when the most recent was captured, and the two
            // newest bodies (body1 = newest via this row, body2 = next-newest via
            // LEAD) so the caller can diff in-memory without a per-object query.
            using var cmd = Db.CreateCommand();
            var sql = new StringBuilder("""
                SELECT object_id, object_type, object_name, cnt, last_cap, body1, body2 FROM (
                    SELECT object_id, object_type, object_name, captured_utc,
                           COUNT(*)        OVER (PARTITION BY object_id) AS cnt,
                           MAX(captured_utc) OVER (PARTITION BY object_id) AS last_cap,
                           ROW_NUMBER()    OVER (PARTITION BY object_id ORDER BY captured_utc DESC) AS rn,
                           body_json       AS body1,
                           LEAD(body_json) OVER (PARTITION BY object_id ORDER BY captured_utc DESC) AS body2
                    FROM config_snapshots
                """);
            if (tenantId is not null)
            {
                sql.Append(" WHERE tenant_id = $tenant");
                cmd.Parameters.AddWithValue("$tenant", tenantId);
            }
            sql.Append("""
                )
                WHERE rn = 1
                ORDER BY last_cap DESC
                LIMIT $limit;
                """);
            cmd.CommandText = sql.ToString();
            cmd.Parameters.AddWithValue("$limit", limit);

            var results = new List<SnapshottedObject>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SnapshottedObject(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3),
                    DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
            return results;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<string?> GetSyncStateAsync(string key, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = "SELECT value FROM sync_state WHERE key = $k;";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task SetSyncStateAsync(string key, string value, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sync_state (key, value) VALUES ($k, $v)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task CommitIndexAsync(CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            Writer.Commit();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string q, int limit = 50, CancellationToken ct = default, string? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(q)) return Array.Empty<SearchHit>();
        tenantId = ScopeTenant(tenantId);

        await _dbLock.WaitAsync(ct);
        try
        {
            // Near-real-time read straight off the writer so freshly-synced docs are visible.
            using var reader = DirectoryReader.Open(Writer, applyAllDeletes: true);
            var searcher = new IndexSearcher(reader);
            var parser = new QueryParser(LuceneVersion.LUCENE_48, "text", _analyzer);
            Query query;
            try
            {
                query = parser.Parse(QueryParserBase.Escape(q));
            }
            catch (ParseException)
            {
                return Array.Empty<SearchHit>();
            }

            var hits = searcher.Search(query, tenantId is null ? limit : Math.Max(limit * 5, limit)).ScoreDocs;
            var results = new List<SearchHit>(hits.Length);
            foreach (var hit in hits)
            {
                var doc = searcher.Doc(hit.Doc);
                var kind = doc.Get("kind") ?? "";
                if (tenantId is not null &&
                    (kind == "ConfigSnapshot" || kind == "AuditEvent") &&
                    !string.Equals(doc.Get("tenantId"), tenantId, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new SearchHit(
                    kind,
                    doc.Get("id") ?? "",
                    hit.Score,
                    doc.Get("summary") ?? ""));
                if (results.Count >= limit) break;
            }
            return results;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    // ── Pending AI changes (M13.2) ───────────────────────────────────────────

    public async Task AppendPendingChangeAsync(PendingChangeRecord c, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO pending_changes
                    (id, proposer, kind, path, object_id, object_name, body_json, diff_json, state, created_utc, decided_utc, note, simulation_json, tenant_id)
                VALUES ($id,$prop,$kind,$path,$oid,$oname,$body,$diff,$state,$created,$decided,$note,$sim,$tenant);
                """;
            cmd.Parameters.AddWithValue("$id", c.Id);
            cmd.Parameters.AddWithValue("$prop", c.Proposer);
            cmd.Parameters.AddWithValue("$kind", c.Kind);
            cmd.Parameters.AddWithValue("$path", c.Path);
            cmd.Parameters.AddWithValue("$oid", (object?)c.ObjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$oname", (object?)c.ObjectName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$body", (object?)c.BodyJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$diff", c.DiffJson);
            cmd.Parameters.AddWithValue("$state", c.State);
            cmd.Parameters.AddWithValue("$created", c.CreatedUtc.ToUniversalTime().ToString("o"));
            cmd.Parameters.AddWithValue("$decided", (object?)c.DecidedUtc?.ToUniversalTime().ToString("o") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$note", (object?)c.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sim", (object?)c.SimulationJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tenant", (object?)c.TenantId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        finally { _dbLock.Release(); }
    }

    public async Task<IReadOnlyList<PendingChangeRecord>> GetPendingChangesAsync(
        string? state = "pending", int limit = 200, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            var sql = new StringBuilder(
                "SELECT id, proposer, kind, path, object_id, object_name, body_json, diff_json, state, created_utc, decided_utc, note, simulation_json, tenant_id FROM pending_changes");
            if (!string.IsNullOrWhiteSpace(state))
            {
                sql.Append(" WHERE state = $state");
                cmd.Parameters.AddWithValue("$state", state);
            }
            sql.Append(" ORDER BY created_utc DESC LIMIT $limit;");
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.CommandText = sql.ToString();

            var results = new List<PendingChangeRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) results.Add(ReadPending(reader));
            return results;
        }
        finally { _dbLock.Release(); }
    }

    public async Task<PendingChangeRecord?> GetPendingChangeAsync(string id, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText =
                "SELECT id, proposer, kind, path, object_id, object_name, body_json, diff_json, state, created_utc, decided_utc, note, simulation_json, tenant_id FROM pending_changes WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadPending(reader) : null;
        }
        finally { _dbLock.Release(); }
    }

    public async Task SetPendingChangeStateAsync(
        string id, string state, string? note, DateTime decidedUtc, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = "UPDATE pending_changes SET state = $state, note = $note, decided_utc = $decided WHERE id = $id;";
            cmd.Parameters.AddWithValue("$state", state);
            cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$decided", decidedUtc.ToUniversalTime().ToString("o"));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        finally { _dbLock.Release(); }
    }

    // ── M18 Autonomy runs ─────────────────────────────────────────────────────

    public async Task UpsertAutonomyRunAsync(AutonomyRunRecord run, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            // Upsert: a run is appended once finished, then re-written in place only to
            // fold in the operator's decision + the verify result a later tick learns
            // (same physical run). started_utc never changes, so list ordering is stable.
            cmd.CommandText = """
                INSERT INTO autonomy_runs (run_id, tenant_id, started_utc, run_json)
                VALUES ($id, $tid, $started, $json)
                ON CONFLICT(run_id) DO UPDATE SET run_json = excluded.run_json;
                """;
            cmd.Parameters.AddWithValue("$id", run.RunId);
            cmd.Parameters.AddWithValue("$tid", (object?)run.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$started", run.StartedUtc.ToUniversalTime().ToString("o"));
            cmd.Parameters.AddWithValue("$json", run.RunJson);
            cmd.ExecuteNonQuery();

            // Retention: the loop appends one row per tick (re-written in place on
            // decision/verify), so without a bound the table grows forever (~96 rows/day
            // at the 15-min default). Keep the newest N — the canonical audit trail lives
            // in audit_events/snapshots, not here.
            using var prune = Db.CreateCommand();
            prune.CommandText = """
                DELETE FROM autonomy_runs WHERE run_id NOT IN (
                    SELECT run_id FROM autonomy_runs ORDER BY started_utc DESC LIMIT $keep);
                """;
            prune.Parameters.AddWithValue("$keep", AutonomyRunRetention);
            prune.ExecuteNonQuery();
        }
        finally { _dbLock.Release(); }
    }

    // Cap on retained autonomy loop-run rows (≈3 weeks at the 15-min default cadence).
    private const int AutonomyRunRetention = 2000;

    public async Task<IReadOnlyList<AutonomyRunRecord>> GetAutonomyRunsAsync(int limit = 50, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = """
                SELECT run_id, tenant_id, started_utc, run_json FROM autonomy_runs
                ORDER BY started_utc DESC LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", limit);
            var results = new List<AutonomyRunRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) results.Add(ReadRun(reader));
            return results;
        }
        finally { _dbLock.Release(); }
    }

    public async Task<AutonomyRunRecord?> GetAutonomyRunAsync(string runId, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            using var cmd = Db.CreateCommand();
            cmd.CommandText = "SELECT run_id, tenant_id, started_utc, run_json FROM autonomy_runs WHERE run_id = $id;";
            cmd.Parameters.AddWithValue("$id", runId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadRun(reader) : null;
        }
        finally { _dbLock.Release(); }
    }

    private static AutonomyRunRecord ReadRun(SqliteDataReader r) => new(
        r.GetString(0),
        r.IsDBNull(1) ? null : r.GetString(1),
        DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
        r.GetString(3));

    // --- helpers (all called under _dbLock) ---------------------------------

    private SqliteConnection Db => _db ?? throw new InvalidOperationException("Store not initialized — call InitializeAsync first.");
    private IndexWriter Writer => _writer ?? throw new InvalidOperationException("Store not initialized — call InitializeAsync first.");

    private void Execute(string sql)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void IndexDoc(string kind, string id, string text, string summary, string? tenantId = null)
    {
        var doc = new Document
        {
            new StringField("kind", kind, Field.Store.YES),
            new StringField("id", id, Field.Store.YES),
            new TextField("text", text, Field.Store.NO),
            new StoredField("summary", summary),
        };
        if (!string.IsNullOrWhiteSpace(tenantId))
            doc.Add(new StringField("tenantId", tenantId, Field.Store.YES));
        // Keyed update keeps indexing idempotent if the same id is appended twice.
        // No Commit() here — the NRT searcher already sees uncommitted docs, so we defer
        // the (expensive) fsync to CommitIndexAsync at the end of a batch.
        Writer.UpdateDocument(new Term("id", id), doc);
    }

    private static ConfigSnapshotRecord ReadSnapshot(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        DateTime.Parse(r.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
        r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7));

    private static string? ScopeTenant(string? tenantId) =>
        string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;

    private static PendingChangeRecord ReadPending(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.GetString(7),
        r.GetString(8),
        DateTime.Parse(r.GetString(9), null, System.Globalization.DateTimeStyles.RoundtripKind),
        r.IsDBNull(10) ? null : DateTime.Parse(r.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
        r.IsDBNull(11) ? null : r.GetString(11),   // note
        r.IsDBNull(12) ? null : r.GetString(12),   // simulation_json (M16)
        r.IsDBNull(13) ? null : r.GetString(13));  // tenant_id (M20)

    private static string Sha256(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    public void Dispose()
    {
        _writer?.Dispose();
        _luceneDir?.Dispose();
        _analyzer?.Dispose();
        _db?.Dispose();
        _dbLock.Dispose();
    }
}
