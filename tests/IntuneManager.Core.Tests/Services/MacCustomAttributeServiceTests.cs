using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class MacCustomAttributeServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IMacCustomAttributeService).IsAssignableFrom(typeof(MacCustomAttributeService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(MacCustomAttributeService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IMacCustomAttributeService).GetMethod("ListMacCustomAttributesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceCustomAttributeShellScript>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IMacCustomAttributeService).GetMethod("GetMacCustomAttributeAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceCustomAttributeShellScript?>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IMacCustomAttributeService).GetMethod("CreateMacCustomAttributeAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceCustomAttributeShellScript>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceCustomAttributeShellScript), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IMacCustomAttributeService).GetMethod("UpdateMacCustomAttributeAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceCustomAttributeShellScript>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceCustomAttributeShellScript), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IMacCustomAttributeService).GetMethod("DeleteMacCustomAttributeAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IMacCustomAttributeService).GetMethods();
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
        var methods = typeof(IMacCustomAttributeService).GetMethods();
        Assert.Equal(5, methods.Length);
    }

    [Fact]
    public void ListMethod_HasCancellationTokenWithDefault()
    {
        var method = typeof(IMacCustomAttributeService).GetMethod("ListMacCustomAttributesAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(CancellationToken), parameters[0].ParameterType);
        Assert.True(parameters[0].HasDefaultValue);
    }

    [Fact]
    public void GetMethod_AcceptsStringId()
    {
        var method = typeof(IMacCustomAttributeService).GetMethod("GetMacCustomAttributeAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void DeleteMethod_AcceptsStringId()
    {
        var method = typeof(IMacCustomAttributeService).GetMethod("DeleteMacCustomAttributeAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Service_HasPrivateGraphClientField()
    {
        var field = typeof(MacCustomAttributeService).GetField("_graphClient",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(GraphServiceClient), field.FieldType);
    }
}
