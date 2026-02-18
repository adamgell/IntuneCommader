using IntuneManager.Core.Models;
using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class EndpointSecurityServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IEndpointSecurityService).IsAssignableFrom(typeof(EndpointSecurityService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(EndpointSecurityService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IEndpointSecurityService).GetMethod("ListEndpointSecurityIntentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceManagementIntent>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IEndpointSecurityService).GetMethod("GetEndpointSecurityIntentAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceManagementIntent?>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IEndpointSecurityService).GetMethod("CreateEndpointSecurityIntentAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceManagementIntent>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceManagementIntent), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IEndpointSecurityService).GetMethod("UpdateEndpointSecurityIntentAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceManagementIntent>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceManagementIntent), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IEndpointSecurityService).GetMethod("DeleteEndpointSecurityIntentAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesGetAssignmentsMethod()
    {
        var method = typeof(IEndpointSecurityService).GetMethod("GetAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceManagementIntentAssignment>>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesAssignIntentMethod()
    {
        var method = typeof(IEndpointSecurityService).GetMethod("AssignIntentAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(List<DeviceManagementIntentAssignment>), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IEndpointSecurityService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void ExportModel_HasRequiredProperties()
    {
        var export = new EndpointSecurityExport
        {
            Intent = new DeviceManagementIntent { Id = "test", DisplayName = "Test" }
        };

        Assert.NotNull(export.Intent);
        Assert.Empty(export.Assignments);
    }

    [Fact]
    public void ExportModel_AssignmentsDefaultsToEmptyList()
    {
        var export = new EndpointSecurityExport
        {
            Intent = new DeviceManagementIntent()
        };

        Assert.NotNull(export.Assignments);
        Assert.IsType<List<DeviceManagementIntentAssignment>>(export.Assignments);
    }

    [Fact]
    public void ExportModel_CanSetAssignments()
    {
        var assignments = new List<DeviceManagementIntentAssignment>
        {
            new() { Id = "a1" },
            new() { Id = "a2" }
        };

        var export = new EndpointSecurityExport
        {
            Intent = new DeviceManagementIntent(),
            Assignments = assignments
        };

        Assert.Equal(2, export.Assignments.Count);
    }
}
