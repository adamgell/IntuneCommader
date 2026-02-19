using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class NamedLocationServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(INamedLocationService).IsAssignableFrom(typeof(NamedLocationService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(NamedLocationService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("ListNamedLocationsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<NamedLocation>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("GetNamedLocationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<NamedLocation?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("CreateNamedLocationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<NamedLocation>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(NamedLocation), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("UpdateNamedLocationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<NamedLocation>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(NamedLocation), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("DeleteNamedLocationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(INamedLocationService).GetMethods();
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
        var methods = typeof(INamedLocationService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
