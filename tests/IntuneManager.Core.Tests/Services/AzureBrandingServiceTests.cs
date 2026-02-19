using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class AzureBrandingServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IAzureBrandingService).IsAssignableFrom(typeof(AzureBrandingService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(AzureBrandingService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAzureBrandingService).GetMethod("ListBrandingLocalizationsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<OrganizationalBrandingLocalization>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAzureBrandingService).GetMethod("GetBrandingLocalizationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<OrganizationalBrandingLocalization?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IAzureBrandingService).GetMethod("CreateBrandingLocalizationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<OrganizationalBrandingLocalization>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(OrganizationalBrandingLocalization), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IAzureBrandingService).GetMethod("UpdateBrandingLocalizationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<OrganizationalBrandingLocalization>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(OrganizationalBrandingLocalization), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IAzureBrandingService).GetMethod("DeleteBrandingLocalizationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAzureBrandingService).GetMethods();
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
        var methods = typeof(IAzureBrandingService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
