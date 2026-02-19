using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class ScopeTagServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IScopeTagService).IsAssignableFrom(typeof(ScopeTagService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(ScopeTagService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IScopeTagService).GetMethod("ListScopeTagsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<RoleScopeTag>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IScopeTagService).GetMethod("GetScopeTagAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<RoleScopeTag?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IScopeTagService).GetMethod("CreateScopeTagAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<RoleScopeTag>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(RoleScopeTag), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IScopeTagService).GetMethod("UpdateScopeTagAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<RoleScopeTag>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(RoleScopeTag), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IScopeTagService).GetMethod("DeleteScopeTagAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IScopeTagService).GetMethods();
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
        var methods = typeof(IScopeTagService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
