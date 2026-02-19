using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class IntuneBrandingServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IIntuneBrandingService).IsAssignableFrom(typeof(IntuneBrandingService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(IntuneBrandingService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IIntuneBrandingService).GetMethod("ListIntuneBrandingProfilesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<IntuneBrandingProfile>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IIntuneBrandingService).GetMethod("GetIntuneBrandingProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IntuneBrandingProfile?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IIntuneBrandingService).GetMethod("CreateIntuneBrandingProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IntuneBrandingProfile>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(IntuneBrandingProfile), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IIntuneBrandingService).GetMethod("UpdateIntuneBrandingProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IntuneBrandingProfile>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(IntuneBrandingProfile), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IIntuneBrandingService).GetMethod("DeleteIntuneBrandingProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IIntuneBrandingService).GetMethods();
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
        var methods = typeof(IIntuneBrandingService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
