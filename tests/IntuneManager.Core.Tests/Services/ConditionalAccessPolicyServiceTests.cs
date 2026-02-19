using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class ConditionalAccessPolicyServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IConditionalAccessPolicyService).IsAssignableFrom(typeof(ConditionalAccessPolicyService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(ConditionalAccessPolicyService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListPoliciesMethod()
    {
        var method = typeof(IConditionalAccessPolicyService).GetMethod("ListPoliciesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<ConditionalAccessPolicy>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetPolicyMethod()
    {
        var method = typeof(IConditionalAccessPolicyService).GetMethod("GetPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ConditionalAccessPolicy?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IConditionalAccessPolicyService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void Interface_HasTwoMethods()
    {
        var methods = typeof(IConditionalAccessPolicyService).GetMethods();
        Assert.Equal(2, methods.Length);
    }
}
