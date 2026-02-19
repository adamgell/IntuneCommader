using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class TermsAndConditionsServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(ITermsAndConditionsService).IsAssignableFrom(typeof(TermsAndConditionsService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(TermsAndConditionsService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(ITermsAndConditionsService).GetMethod("ListTermsAndConditionsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<TermsAndConditions>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(ITermsAndConditionsService).GetMethod("GetTermsAndConditionsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<TermsAndConditions?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(ITermsAndConditionsService).GetMethod("CreateTermsAndConditionsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<TermsAndConditions>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(TermsAndConditions), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(ITermsAndConditionsService).GetMethod("UpdateTermsAndConditionsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<TermsAndConditions>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(TermsAndConditions), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(ITermsAndConditionsService).GetMethod("DeleteTermsAndConditionsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(ITermsAndConditionsService).GetMethods();
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
        var methods = typeof(ITermsAndConditionsService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
