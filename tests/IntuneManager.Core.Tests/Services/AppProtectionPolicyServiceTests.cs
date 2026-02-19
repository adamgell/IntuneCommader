using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class AppProtectionPolicyServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IAppProtectionPolicyService).IsAssignableFrom(typeof(AppProtectionPolicyService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(AppProtectionPolicyService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAppProtectionPolicyService).GetMethod("ListAppProtectionPoliciesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<ManagedAppPolicy>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAppProtectionPolicyService).GetMethod("GetAppProtectionPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ManagedAppPolicy?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IAppProtectionPolicyService).GetMethod("CreateAppProtectionPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ManagedAppPolicy>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(ManagedAppPolicy), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IAppProtectionPolicyService).GetMethod("UpdateAppProtectionPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ManagedAppPolicy>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(ManagedAppPolicy), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IAppProtectionPolicyService).GetMethod("DeleteAppProtectionPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAppProtectionPolicyService).GetMethods();
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
        var methods = typeof(IAppProtectionPolicyService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
