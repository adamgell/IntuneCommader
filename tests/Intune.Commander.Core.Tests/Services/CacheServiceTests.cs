using Intune.Commander.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Intune.Commander.Core.Tests.Services;

public class CacheServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CacheService _sut;
    private readonly ServiceProvider _sp;

    public CacheServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"IntuneCommander_CacheTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("Intune.Commander.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_tempDir, "keys")));
        _sp = services.BuildServiceProvider();

        var dp = _sp.GetRequiredService<IDataProtectionProvider>();
        _sut = new CacheService(dp, _tempDir);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _sp.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // --- Simple DTO for testing ---
    private record TestItem(string Name, int Value);

    [Fact]
    public void Set_and_Get_roundtrips_data()
    {
        var items = new List<TestItem>
        {
            new("Alpha", 1),
            new("Beta", 2)
        };

        _sut.Set("tenant1", "TestItems", items);

        var result = _sut.Get<TestItem>("tenant1", "TestItems");

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
        Assert.Equal(2, result[1].Value);
    }

    [Fact]
    public void Get_returns_null_for_missing_key()
    {
        var result = _sut.Get<TestItem>("tenant1", "NonExistent");

        Assert.Null(result);
    }

    [Fact]
    public void Get_returns_null_for_expired_entry()
    {
        var items = new List<TestItem> { new("Expired", 0) };

        // Set with a TTL of zero (already expired)
        _sut.Set("tenant1", "Expired", items, TimeSpan.Zero);

        // Small delay to ensure expiry
        Thread.Sleep(10);

        var result = _sut.Get<TestItem>("tenant1", "Expired");

        Assert.Null(result);
    }

    [Fact]
    public void Invalidate_specific_type_removes_only_that_entry()
    {
        _sut.Set("tenant1", "TypeA", new List<TestItem> { new("A", 1) });
        _sut.Set("tenant1", "TypeB", new List<TestItem> { new("B", 2) });

        _sut.Invalidate("tenant1", "TypeA");

        Assert.Null(_sut.Get<TestItem>("tenant1", "TypeA"));
        Assert.NotNull(_sut.Get<TestItem>("tenant1", "TypeB"));
    }

    [Fact]
    public void Invalidate_all_for_tenant_removes_all_entries()
    {
        _sut.Set("tenant1", "TypeA", new List<TestItem> { new("A", 1) });
        _sut.Set("tenant1", "TypeB", new List<TestItem> { new("B", 2) });
        _sut.Set("tenant2", "TypeA", new List<TestItem> { new("X", 9) });

        _sut.Invalidate("tenant1");

        Assert.Null(_sut.Get<TestItem>("tenant1", "TypeA"));
        Assert.Null(_sut.Get<TestItem>("tenant1", "TypeB"));
        // tenant2 unaffected
        Assert.NotNull(_sut.Get<TestItem>("tenant2", "TypeA"));
    }

    [Fact]
    public void CleanupExpired_removes_expired_entries_only()
    {
        _sut.Set("tenant1", "Fresh", new List<TestItem> { new("Good", 1) }, TimeSpan.FromHours(1));
        _sut.Set("tenant1", "Stale", new List<TestItem> { new("Old", 2) }, TimeSpan.Zero);

        Thread.Sleep(10);

        var removed = _sut.CleanupExpired();

        Assert.Equal(1, removed);
        Assert.NotNull(_sut.Get<TestItem>("tenant1", "Fresh"));
        Assert.Null(_sut.Get<TestItem>("tenant1", "Stale"));
    }

    [Fact]
    public void GetMetadata_returns_info_for_valid_entry()
    {
        var items = new List<TestItem> { new("A", 1), new("B", 2), new("C", 3) };
        _sut.Set("tenant1", "Items", items);

        var meta = _sut.GetMetadata("tenant1", "Items");

        Assert.NotNull(meta);
        Assert.Equal(3, meta.Value.ItemCount);
        Assert.True(meta.Value.CachedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void GetMetadata_returns_null_for_missing_entry()
    {
        var meta = _sut.GetMetadata("tenant1", "Missing");

        Assert.Null(meta);
    }

    [Fact]
    public void Set_overwrites_existing_entry()
    {
        _sut.Set("tenant1", "Items", new List<TestItem> { new("Old", 1) });
        _sut.Set("tenant1", "Items", new List<TestItem> { new("New", 2), new("Newer", 3) });

        var result = _sut.Get<TestItem>("tenant1", "Items");

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("New", result[0].Name);
    }

    [Fact]
    public void Password_is_persisted_and_reusable()
    {
        // Set data, dispose, then create a new service pointing to same dir
        _sut.Set("tenant1", "Persist", new List<TestItem> { new("Durable", 42) });
        _sut.Dispose();

        var dp = _sp.GetRequiredService<IDataProtectionProvider>();
        using var sut2 = new CacheService(dp, _tempDir);

        var result = sut2.Get<TestItem>("tenant1", "Persist");

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Durable", result[0].Name);
    }

    [Fact]
    public void Get_deserialization_failure_returns_null_and_deletes_entry()
    {
        _sut.Set("tenant1", "Mismatched", new List<string> { "not-an-object" });

        var result = _sut.Get<TestItem>("tenant1", "Mismatched");

        Assert.Null(result);
        Assert.Null(_sut.Get<string>("tenant1", "Mismatched"));
    }

    [Fact]
    public void GetMetadata_returns_null_for_expired_entry()
    {
        _sut.Set("tenant1", "SoonExpired", new List<TestItem> { new("X", 1) }, TimeSpan.Zero);
        Thread.Sleep(10);

        var meta = _sut.GetMetadata("tenant1", "SoonExpired");

        Assert.Null(meta);
    }

    [Fact]
    public void Corrupted_password_file_is_regenerated()
    {
        _sut.Dispose();

        var passwordPath = Path.Combine(_tempDir, "cache-key.bin");
        var dbPath = Path.Combine(_tempDir, "cache.db");
        File.WriteAllText(passwordPath, "corrupted-key-material");
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        var dp = _sp.GetRequiredService<IDataProtectionProvider>();
        using var sut2 = new CacheService(dp, _tempDir);
        sut2.Set("tenant1", "AfterCorruption", new List<TestItem> { new("Ok", 1) });

        var result = sut2.Get<TestItem>("tenant1", "AfterCorruption");
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void Corrupted_key_with_existing_db_deletes_orphaned_db_and_recovers()
    {
        // Arrange: write real data so the DB file actually exists on disk
        _sut.Set("tenant1", "PreCorruption", new List<TestItem> { new("Old", 99) });
        _sut.Dispose();

        var passwordPath = Path.Combine(_tempDir, "cache-key.bin");
        var dbPath = Path.Combine(_tempDir, "cache.db");

        Assert.True(File.Exists(dbPath), "DB file must exist before corruption test");

        // Corrupt the key — the existing DB is now permanently unreadable
        File.WriteAllText(passwordPath, "corrupted-key-material");

        // Act: create a new instance — should detect corruption, wipe orphaned files, start fresh
        var dp = _sp.GetRequiredService<IDataProtectionProvider>();
        using var sut2 = new CacheService(dp, _tempDir);

        // Previously cached data is gone (DB was wiped)
        Assert.Null(sut2.Get<TestItem>("tenant1", "PreCorruption"));

        // New data can be stored and retrieved normally
        sut2.Set("tenant1", "PostRecovery", new List<TestItem> { new("New", 1) });
        var result = sut2.Get<TestItem>("tenant1", "PostRecovery");
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void Corrupted_key_deletes_key_file_and_db_from_disk()
    {
        // Arrange: ensure DB exists
        _sut.Set("tenant1", "Anything", new List<TestItem> { new("X", 1) });
        _sut.Dispose();

        var passwordPath = Path.Combine(_tempDir, "cache-key.bin");
        var dbPath = Path.Combine(_tempDir, "cache.db");

        File.WriteAllText(passwordPath, "corrupted-key-material");

        // Act
        var dp = _sp.GetRequiredService<IDataProtectionProvider>();
        using var sut2 = new CacheService(dp, _tempDir);
        sut2.Dispose(); // close before inspecting files

        // The OLD db is gone; a new key file was written
        Assert.True(File.Exists(passwordPath), "New key file should have been created");
        Assert.NotEqual("corrupted-key-material", File.ReadAllText(passwordPath));
    }

    [Fact]
    public void CleanupExpired_returns_zero_when_nothing_is_expired()
    {
        _sut.Set("tenant1", "Fresh", new List<TestItem> { new("Good", 1) }, TimeSpan.FromHours(1));

        var removed = _sut.CleanupExpired();

        Assert.Equal(0, removed);
    }
}
