using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class DeviceHealthScriptServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IDeviceHealthScriptService).IsAssignableFrom(typeof(DeviceHealthScriptService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(DeviceHealthScriptService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IDeviceHealthScriptService).GetMethod("ListDeviceHealthScriptsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceHealthScript>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IDeviceHealthScriptService).GetMethod("GetDeviceHealthScriptAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceHealthScript?>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IDeviceHealthScriptService).GetMethod("CreateDeviceHealthScriptAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceHealthScript>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceHealthScript), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IDeviceHealthScriptService).GetMethod("UpdateDeviceHealthScriptAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceHealthScript>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceHealthScript), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IDeviceHealthScriptService).GetMethod("DeleteDeviceHealthScriptAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IDeviceHealthScriptService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void Interface_HasFiveMethods()
    {
        var methods = typeof(IDeviceHealthScriptService).GetMethods();
        Assert.Equal(5, methods.Length);
    }

    [Fact]
    public void ListMethod_HasCancellationTokenWithDefault()
    {
        var method = typeof(IDeviceHealthScriptService).GetMethod("ListDeviceHealthScriptsAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(CancellationToken), parameters[0].ParameterType);
        Assert.True(parameters[0].HasDefaultValue);
    }

    [Fact]
    public void GetMethod_AcceptsStringId()
    {
        var method = typeof(IDeviceHealthScriptService).GetMethod("GetDeviceHealthScriptAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void DeleteMethod_AcceptsStringId()
    {
        var method = typeof(IDeviceHealthScriptService).GetMethod("DeleteDeviceHealthScriptAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Service_HasPrivateGraphClientField()
    {
        var field = typeof(DeviceHealthScriptService).GetField("_graphClient",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(GraphServiceClient), field.FieldType);
    }
}
