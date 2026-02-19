using IntuneManager.Core.Models;
using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class ApplicationServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IApplicationService).IsAssignableFrom(typeof(ApplicationService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(ApplicationService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IApplicationService).GetMethod("ListApplicationsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<MobileApp>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IApplicationService).GetMethod("GetApplicationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<MobileApp?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesGetAssignmentsMethod()
    {
        var method = typeof(IApplicationService).GetMethod("GetAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<MobileAppAssignment>>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IApplicationService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void Interface_HasThreeMethods()
    {
        var methods = typeof(IApplicationService).GetMethods();
        Assert.Equal(3, methods.Length);
    }

    [Fact]
    public void ExportModel_HasRequiredProperties()
    {
        var export = new ApplicationExport
        {
            Application = new MobileApp { Id = "test", DisplayName = "Test App" }
        };

        Assert.NotNull(export.Application);
        Assert.Empty(export.Assignments);
    }

    [Fact]
    public void ExportModel_AssignmentsDefaultsToEmptyList()
    {
        var export = new ApplicationExport
        {
            Application = new MobileApp()
        };

        Assert.NotNull(export.Assignments);
        Assert.IsType<List<MobileAppAssignment>>(export.Assignments);
    }

    [Fact]
    public void ExportModel_CanSetAssignments()
    {
        var assignments = new List<MobileAppAssignment>
        {
            new() { Id = "a1" },
            new() { Id = "a2" }
        };

        var export = new ApplicationExport
        {
            Application = new MobileApp(),
            Assignments = assignments
        };

        Assert.Equal(2, export.Assignments.Count);
    }
}
