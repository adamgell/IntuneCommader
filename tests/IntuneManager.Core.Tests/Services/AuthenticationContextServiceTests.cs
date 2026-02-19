using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class AuthenticationContextServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IAuthenticationContextService).IsAssignableFrom(typeof(AuthenticationContextService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(AuthenticationContextService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("ListAuthenticationContextsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AuthenticationContextClassReference>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("GetAuthenticationContextAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationContextClassReference?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("CreateAuthenticationContextAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationContextClassReference>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(AuthenticationContextClassReference), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("UpdateAuthenticationContextAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationContextClassReference>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(AuthenticationContextClassReference), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("DeleteAuthenticationContextAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAuthenticationContextService).GetMethods();
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
        var methods = typeof(IAuthenticationContextService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
