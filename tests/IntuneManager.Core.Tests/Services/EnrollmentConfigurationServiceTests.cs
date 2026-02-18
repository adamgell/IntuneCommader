using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class EnrollmentConfigurationServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IEnrollmentConfigurationService).IsAssignableFrom(typeof(EnrollmentConfigurationService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(EnrollmentConfigurationService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IEnrollmentConfigurationService).GetMethod("ListEnrollmentConfigurationsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceEnrollmentConfiguration>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesListEnrollmentStatusPagesMethod()
    {
        var method = typeof(IEnrollmentConfigurationService).GetMethod("ListEnrollmentStatusPagesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceEnrollmentConfiguration>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesListEnrollmentRestrictionsMethod()
    {
        var method = typeof(IEnrollmentConfigurationService).GetMethod("ListEnrollmentRestrictionsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceEnrollmentConfiguration>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesListCoManagementSettingsMethod()
    {
        var method = typeof(IEnrollmentConfigurationService).GetMethod("ListCoManagementSettingsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceEnrollmentConfiguration>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IEnrollmentConfigurationService).GetMethod("GetEnrollmentConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceEnrollmentConfiguration?>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IEnrollmentConfigurationService).GetMethod("CreateEnrollmentConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceEnrollmentConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceEnrollmentConfiguration), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IEnrollmentConfigurationService).GetMethod("UpdateEnrollmentConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceEnrollmentConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceEnrollmentConfiguration), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IEnrollmentConfigurationService).GetMethod("DeleteEnrollmentConfigurationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IEnrollmentConfigurationService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void Interface_HasEightMethods()
    {
        var methods = typeof(IEnrollmentConfigurationService).GetMethods();
        Assert.Equal(8, methods.Length);
    }
}
