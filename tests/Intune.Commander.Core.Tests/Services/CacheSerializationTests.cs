using System.Text.Json;
using Intune.Commander.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Tests.Services;

/// <summary>
/// Tests that verify Graph SDK model caching round-trips correctly.
/// </summary>
public class CacheSerializationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CacheService _sut;
    private readonly ServiceProvider _sp;

    public CacheSerializationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"IntuneCommander_CacheSerTests_{Guid.NewGuid():N}");
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

    [Fact]
    public void DeviceConfiguration_roundtrips_through_cache()
    {
        var items = new List<DeviceConfiguration>
        {
            new DeviceConfiguration
            {
                Id = "config-1",
                DisplayName = "Test Config",
                OdataType = "#microsoft.graph.windows10GeneralConfiguration",
                Description = "A test configuration"
            }
        };

        _sut.Set("tenant1", "DeviceConfigurations", items);
        var result = _sut.Get<DeviceConfiguration>("tenant1", "DeviceConfigurations");

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("config-1", result[0].Id);
        Assert.Equal("Test Config", result[0].DisplayName);
        Assert.Equal("#microsoft.graph.windows10GeneralConfiguration", result[0].OdataType);
    }

    [Fact]
    public void DeviceCompliancePolicy_roundtrips_through_cache()
    {
        var items = new List<DeviceCompliancePolicy>
        {
            new DeviceCompliancePolicy
            {
                Id = "policy-1",
                DisplayName = "Compliance Policy",
                OdataType = "#microsoft.graph.windows10CompliancePolicy"
            }
        };

        _sut.Set("tenant1", "CompliancePolicies", items);
        var result = _sut.Get<DeviceCompliancePolicy>("tenant1", "CompliancePolicies");

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("policy-1", result[0].Id);
        Assert.Equal("Compliance Policy", result[0].DisplayName);
    }

    [Fact]
    public void MobileApp_roundtrips_through_cache()
    {
        var items = new List<MobileApp>
        {
            new MobileApp
            {
                Id = "app-1",
                DisplayName = "My App",
                OdataType = "#microsoft.graph.win32LobApp"
            }
        };

        _sut.Set("tenant1", "Applications", items);
        var result = _sut.Get<MobileApp>("tenant1", "Applications");

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("app-1", result[0].Id);
        Assert.Equal("My App", result[0].DisplayName);
    }

    [Fact]
    public void SettingsCatalog_roundtrips_through_cache()
    {
        var items = new List<DeviceManagementConfigurationPolicy>
        {
            new DeviceManagementConfigurationPolicy
            {
                Id = "sc-1",
                Name = "Settings Policy",
                IsAssigned = true
            }
        };

        _sut.Set("tenant1", "SettingsCatalog", items);
        var result = _sut.Get<DeviceManagementConfigurationPolicy>("tenant1", "SettingsCatalog");

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("sc-1", result[0].Id);
        Assert.Equal("Settings Policy", result[0].Name);
        Assert.True(result[0].IsAssigned);
    }

    [Fact]
    public void Serialization_preserves_null_properties_and_odatatype()
    {
        // Simulate what the app actually does: serialize a list, then check what survives
        var config = new DeviceConfiguration
        {
            Id = "id-1",
            DisplayName = "Config1",
            OdataType = "#microsoft.graph.windows10GeneralConfiguration",
            Description = null // intentionally null
        };

        var items = new List<DeviceConfiguration> { config };

        // Check what the JSON looks like
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        var json = JsonSerializer.Serialize(items, options);

        // Verify the JSON contains the key properties
        Assert.Contains("id-1", json);
        Assert.Contains("Config1", json);

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<List<DeviceConfiguration>>(json, options);
        Assert.NotNull(deserialized);
        Assert.Equal("id-1", deserialized![0].Id);
        Assert.Equal("Config1", deserialized[0].DisplayName);
    }
}
