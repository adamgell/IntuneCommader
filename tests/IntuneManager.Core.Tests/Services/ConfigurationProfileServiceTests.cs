using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class ConfigurationProfileServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IConfigurationProfileService).IsAssignableFrom(typeof(ConfigurationProfileService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(ConfigurationProfileService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IConfigurationProfileService).GetMethod("ListDeviceConfigurationsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceConfiguration>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IConfigurationProfileService).GetMethod("GetDeviceConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceConfiguration?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IConfigurationProfileService).GetMethod("CreateDeviceConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceConfiguration), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IConfigurationProfileService).GetMethod("UpdateDeviceConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceConfiguration), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IConfigurationProfileService).GetMethod("DeleteDeviceConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesGetAssignmentsMethod()
    {
        var method = typeof(IConfigurationProfileService).GetMethod("GetAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceConfigurationAssignment>>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IConfigurationProfileService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void Interface_HasSixMethods()
    {
        var methods = typeof(IConfigurationProfileService).GetMethods();
        Assert.Equal(6, methods.Length);
    }
}
