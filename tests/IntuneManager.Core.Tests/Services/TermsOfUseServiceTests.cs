using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class TermsOfUseServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(ITermsOfUseService).IsAssignableFrom(typeof(TermsOfUseService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(TermsOfUseService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("ListTermsOfUseAgreementsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<Agreement>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("GetTermsOfUseAgreementAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Agreement?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("CreateTermsOfUseAgreementAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Agreement>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(Agreement), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("UpdateTermsOfUseAgreementAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Agreement>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(Agreement), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("DeleteTermsOfUseAgreementAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(ITermsOfUseService).GetMethods();
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
        var methods = typeof(ITermsOfUseService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
