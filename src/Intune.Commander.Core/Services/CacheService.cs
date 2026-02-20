using System.Security.Cryptography;
using Intune.Commander.Core.Models;
using LiteDB;
using Microsoft.AspNetCore.DataProtection;

namespace Intune.Commander.Core.Services;

/// <summary>
/// LiteDB-backed cache with AES encryption and configurable TTL.
/// The LiteDB password is generated on first use and stored encrypted
/// via DataProtection in a sidecar file.
/// </summary>
public class CacheService : ICacheService
{
    private const string CollectionName = "cache";
    private const string PasswordPurpose = "Intune.Commander.Cache.Password.v1";

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly LiteDatabase _db;
    private readonly ILiteCollection<CacheEntry> _collection;

    public CacheService(IDataProtectionProvider dataProtectionProvider, string? basePath = null)
    {
        var appDataPath = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Intune.Commander");
        Directory.CreateDirectory(appDataPath);

        var dbPath = Path.Combine(appDataPath, "cache.db");
        var passwordFilePath = Path.Combine(appDataPath, "cache-key.bin");

        var protector = dataProtectionProvider.CreateProtector(PasswordPurpose);
        var dbPassword = GetOrCreatePassword(protector, passwordFilePath, dbPath);

        var connectionString = new ConnectionString
        {
            Filename = dbPath,
            Password = dbPassword
        };

        // LiteDB stores DateTime as Local by default, which breaks UTC comparisons.
        // Configure the mapper to keep UTC kind intact.
        var mapper = new BsonMapper();
        mapper.RegisterType<DateTime>(
            serialize: dt => new BsonValue(dt.ToUniversalTime()),
            deserialize: bson => bson.AsDateTime.ToUniversalTime());

        _db = new LiteDatabase(connectionString, mapper);

        _collection = _db.GetCollection<CacheEntry>(CollectionName);
        _collection.EnsureIndex(x => x.TenantId);
        _collection.EnsureIndex(x => x.ExpiresAtUtc);
    }

    public List<T>? Get<T>(string tenantId, string dataType)
    {
        var id = MakeKey(tenantId, dataType);
        var entry = _collection.FindById(id);

        if (entry == null)
            return null;

        if (DateTime.UtcNow > entry.ExpiresAtUtc)
        {
            // Expired — remove and return null
            _collection.Delete(id);
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<T>>(entry.JsonData, JsonOptions);
        }
        catch
        {
            // Deserialization failed (schema change, corruption) — remove stale entry
            _collection.Delete(id);
            return null;
        }
    }

    public void Set<T>(string tenantId, string dataType, List<T> items, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? DefaultTtl;
        var now = DateTime.UtcNow;

        var entry = new CacheEntry
        {
            Id = MakeKey(tenantId, dataType),
            TenantId = tenantId,
            DataType = dataType,
            JsonData = System.Text.Json.JsonSerializer.Serialize(items, JsonOptions),
            CachedAtUtc = now,
            ExpiresAtUtc = now + effectiveTtl,
            ItemCount = items.Count
        };

        _collection.Upsert(entry);
    }

    public void Invalidate(string tenantId, string? dataType = null)
    {
        if (dataType != null)
        {
            _collection.Delete(MakeKey(tenantId, dataType));
        }
        else
        {
            _collection.DeleteMany(x => x.TenantId == tenantId);
        }
    }

    public int CleanupExpired()
    {
        var now = DateTime.UtcNow;
        var expiredIds = _collection.Find(e => e.ExpiresAtUtc < now)
            .Select(e => new BsonValue(e.Id))
            .ToList();

        foreach (var id in expiredIds)
            _collection.Delete(id);

        return expiredIds.Count;
    }

    public (DateTime CachedAt, int ItemCount)? GetMetadata(string tenantId, string dataType)
    {
        var id = MakeKey(tenantId, dataType);
        var entry = _collection.FindById(id);

        if (entry == null || DateTime.UtcNow > entry.ExpiresAtUtc)
            return null;

        return (entry.CachedAtUtc, entry.ItemCount);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string MakeKey(string tenantId, string dataType)
        => $"{tenantId}|{dataType}";

    /// <summary>
    /// Gets or creates the LiteDB password. The password is a random 32-byte
    /// value, base64-encoded, and stored encrypted via DataProtection.
    /// If the key file exists but cannot be decrypted (keys rotated, corruption),
    /// the unreadable DB and key file are deleted before a fresh pair is created.
    /// </summary>
    private static string GetOrCreatePassword(IDataProtector protector, string passwordFilePath, string dbPath)
    {
        if (File.Exists(passwordFilePath))
        {
            try
            {
                var encryptedPassword = File.ReadAllText(passwordFilePath);
                return protector.Unprotect(encryptedPassword);
            }
            catch
            {
                // Keys rotated or file corrupted — the DB is now permanently unreadable.
                // Wipe both files so we don't leave orphaned encrypted trash on disk.
                DeleteCacheFiles(dbPath, passwordFilePath);
            }
        }

        // Generate new random password
        var passwordBytes = new byte[32];
        RandomNumberGenerator.Fill(passwordBytes);
        var password = Convert.ToBase64String(passwordBytes);

        // Encrypt and persist
        var encrypted = protector.Protect(password);
        File.WriteAllText(passwordFilePath, encrypted);

        return password;
    }

    /// <summary>
    /// Deletes the LiteDB database file, its journal sidecar, and the key file.
    /// All deletions are best-effort — a locked or missing file is silently skipped.
    /// </summary>
    private static void DeleteCacheFiles(string dbPath, string passwordFilePath)
    {
        foreach (var path in (string[])[dbPath, dbPath + "-log", passwordFilePath])
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
