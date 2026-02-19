using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class FeatureUpdateProfileServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IFeatureUpdateProfileService).IsAssignableFrom(typeof(FeatureUpdateProfileService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(FeatureUpdateProfileService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IFeatureUpdateProfileService).GetMethod("ListFeatureUpdateProfilesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<WindowsFeatureUpdateProfile>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IFeatureUpdateProfileService).GetMethod("GetFeatureUpdateProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WindowsFeatureUpdateProfile?>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IFeatureUpdateProfileService).GetMethod("CreateFeatureUpdateProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WindowsFeatureUpdateProfile>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(WindowsFeatureUpdateProfile), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IFeatureUpdateProfileService).GetMethod("UpdateFeatureUpdateProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WindowsFeatureUpdateProfile>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(WindowsFeatureUpdateProfile), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IFeatureUpdateProfileService).GetMethod("DeleteFeatureUpdateProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IFeatureUpdateProfileService).GetMethods();
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
        var methods = typeof(IFeatureUpdateProfileService).GetMethods();
        Assert.Equal(5, methods.Length);
    }

    [Fact]
    public void ListMethod_HasCancellationTokenWithDefault()
    {
        var method = typeof(IFeatureUpdateProfileService).GetMethod("ListFeatureUpdateProfilesAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(CancellationToken), parameters[0].ParameterType);
        Assert.True(parameters[0].HasDefaultValue);
    }

    [Fact]
    public void GetMethod_AcceptsStringId()
    {
        var method = typeof(IFeatureUpdateProfileService).GetMethod("GetFeatureUpdateProfileAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void DeleteMethod_AcceptsStringId()
    {
        var method = typeof(IFeatureUpdateProfileService).GetMethod("DeleteFeatureUpdateProfileAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Service_HasPrivateGraphClientField()
    {
        var field = typeof(FeatureUpdateProfileService).GetField("_graphClient",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(GraphServiceClient), field.FieldType);
    }
}
