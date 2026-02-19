using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class ManagedAppConfigurationServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IManagedAppConfigurationService).IsAssignableFrom(typeof(ManagedAppConfigurationService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(ManagedAppConfigurationService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    // ManagedDevice App Configuration methods

    [Fact]
    public void Interface_DefinesListManagedDeviceAppConfigurationsMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("ListManagedDeviceAppConfigurationsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<ManagedDeviceMobileAppConfiguration>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetManagedDeviceAppConfigurationMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("GetManagedDeviceAppConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ManagedDeviceMobileAppConfiguration?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateManagedDeviceAppConfigurationMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("CreateManagedDeviceAppConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ManagedDeviceMobileAppConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(ManagedDeviceMobileAppConfiguration), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateManagedDeviceAppConfigurationMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("UpdateManagedDeviceAppConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ManagedDeviceMobileAppConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(ManagedDeviceMobileAppConfiguration), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteManagedDeviceAppConfigurationMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("DeleteManagedDeviceAppConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    // Targeted Managed App Configuration methods

    [Fact]
    public void Interface_DefinesListTargetedManagedAppConfigurationsMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("ListTargetedManagedAppConfigurationsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<TargetedManagedAppConfiguration>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetTargetedManagedAppConfigurationMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("GetTargetedManagedAppConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<TargetedManagedAppConfiguration?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateTargetedManagedAppConfigurationMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("CreateTargetedManagedAppConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<TargetedManagedAppConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(TargetedManagedAppConfiguration), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateTargetedManagedAppConfigurationMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("UpdateTargetedManagedAppConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<TargetedManagedAppConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(TargetedManagedAppConfiguration), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteTargetedManagedAppConfigurationMethod()
    {
        var method = typeof(IManagedAppConfigurationService).GetMethod("DeleteTargetedManagedAppConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IManagedAppConfigurationService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void Interface_HasTenMethods()
    {
        var methods = typeof(IManagedAppConfigurationService).GetMethods();
        Assert.Equal(10, methods.Length);
    }
}
