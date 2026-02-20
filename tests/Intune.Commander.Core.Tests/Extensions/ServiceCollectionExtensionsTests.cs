using Intune.Commander.Core.Auth;
using Intune.Commander.Core.Extensions;
using Intune.Commander.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Intune.Commander.Core.Tests.Extensions;

public class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public ServiceCollectionExtensionsTests()
    {
        var services = new ServiceCollection();
        services.AddIntuneCommanderCore();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AddIntuneCommanderCore_Returns_Same_ServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddIntuneCommanderCore();
        Assert.Same(services, result);
    }

    [Fact]
    public void Resolves_IDataProtectionProvider()
    {
        var dp = _provider.GetService<IDataProtectionProvider>();
        Assert.NotNull(dp);
    }

    [Fact]
    public void Resolves_IProfileEncryptionService_As_Singleton()
    {
        var svc1 = _provider.GetService<IProfileEncryptionService>();
        var svc2 = _provider.GetService<IProfileEncryptionService>();
        Assert.NotNull(svc1);
        Assert.IsType<ProfileEncryptionService>(svc1);
        Assert.Same(svc1, svc2);
    }

    [Fact]
    public void Resolves_IAuthenticationProvider_As_Singleton()
    {
        var svc1 = _provider.GetService<IAuthenticationProvider>();
        var svc2 = _provider.GetService<IAuthenticationProvider>();
        Assert.NotNull(svc1);
        Assert.IsType<InteractiveBrowserAuthProvider>(svc1);
        Assert.Same(svc1, svc2);
    }

    [Fact]
    public void Resolves_IntuneGraphClientFactory_As_Singleton()
    {
        var svc1 = _provider.GetService<IntuneGraphClientFactory>();
        var svc2 = _provider.GetService<IntuneGraphClientFactory>();
        Assert.NotNull(svc1);
        Assert.Same(svc1, svc2);
    }

    [Fact]
    public void Resolves_ProfileService_As_Singleton()
    {
        var svc1 = _provider.GetService<ProfileService>();
        var svc2 = _provider.GetService<ProfileService>();
        Assert.NotNull(svc1);
        Assert.Same(svc1, svc2);
    }

    [Fact]
    public void Resolves_IExportService_As_Transient()
    {
        var svc1 = _provider.GetService<IExportService>();
        var svc2 = _provider.GetService<IExportService>();
        Assert.NotNull(svc1);
        Assert.IsType<ExportService>(svc1);
        Assert.NotSame(svc1, svc2);
    }

    [Fact]
    public void Resolves_ICacheService_As_Singleton()
    {
        var svc1 = _provider.GetService<ICacheService>();
        var svc2 = _provider.GetService<ICacheService>();
        Assert.NotNull(svc1);
        Assert.IsType<CacheService>(svc1);
        Assert.Same(svc1, svc2);
    }

    [Fact]
    public void All_Expected_Services_Are_Registered()
    {
        // Verify every service registered by AddIntuneCommanderCore is resolvable
        Assert.NotNull(_provider.GetService<IDataProtectionProvider>());
        Assert.NotNull(_provider.GetService<IProfileEncryptionService>());
        Assert.NotNull(_provider.GetService<IAuthenticationProvider>());
        Assert.NotNull(_provider.GetService<IntuneGraphClientFactory>());
        Assert.NotNull(_provider.GetService<ProfileService>());
        Assert.NotNull(_provider.GetService<IExportService>());
        Assert.NotNull(_provider.GetService<ICacheService>());
    }
}
