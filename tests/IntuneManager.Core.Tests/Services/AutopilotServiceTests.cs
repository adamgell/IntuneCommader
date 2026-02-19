using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class AutopilotServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IAutopilotService).IsAssignableFrom(typeof(AutopilotService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(AutopilotService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAutopilotService).GetMethod("ListAutopilotProfilesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<WindowsAutopilotDeploymentProfile>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAutopilotService).GetMethod("GetAutopilotProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WindowsAutopilotDeploymentProfile?>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IAutopilotService).GetMethod("CreateAutopilotProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WindowsAutopilotDeploymentProfile>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(WindowsAutopilotDeploymentProfile), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IAutopilotService).GetMethod("UpdateAutopilotProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WindowsAutopilotDeploymentProfile>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(WindowsAutopilotDeploymentProfile), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IAutopilotService).GetMethod("DeleteAutopilotProfileAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAutopilotService).GetMethods();
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
        var methods = typeof(IAutopilotService).GetMethods();
        Assert.Equal(5, methods.Length);
    }

    [Fact]
    public void ListMethod_HasCancellationTokenWithDefault()
    {
        var method = typeof(IAutopilotService).GetMethod("ListAutopilotProfilesAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(CancellationToken), parameters[0].ParameterType);
        Assert.True(parameters[0].HasDefaultValue);
    }

    [Fact]
    public void GetMethod_AcceptsStringId()
    {
        var method = typeof(IAutopilotService).GetMethod("GetAutopilotProfileAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void DeleteMethod_AcceptsStringId()
    {
        var method = typeof(IAutopilotService).GetMethod("DeleteAutopilotProfileAsync");
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Service_HasPrivateGraphClientField()
    {
        var field = typeof(AutopilotService).GetField("_graphClient",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(GraphServiceClient), field.FieldType);
    }
}
