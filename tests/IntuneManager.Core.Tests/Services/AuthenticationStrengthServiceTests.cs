using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class AuthenticationStrengthServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IAuthenticationStrengthService).IsAssignableFrom(typeof(AuthenticationStrengthService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(AuthenticationStrengthService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("ListAuthenticationStrengthPoliciesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AuthenticationStrengthPolicy>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("GetAuthenticationStrengthPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationStrengthPolicy?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("CreateAuthenticationStrengthPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationStrengthPolicy>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(AuthenticationStrengthPolicy), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("UpdateAuthenticationStrengthPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationStrengthPolicy>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(AuthenticationStrengthPolicy), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("DeleteAuthenticationStrengthPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAuthenticationStrengthService).GetMethods();
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
        var methods = typeof(IAuthenticationStrengthService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
