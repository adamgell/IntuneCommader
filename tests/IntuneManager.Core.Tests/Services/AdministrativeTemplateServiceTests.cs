using IntuneManager.Core.Models;
using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class AdministrativeTemplateServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IAdministrativeTemplateService).IsAssignableFrom(typeof(AdministrativeTemplateService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(AdministrativeTemplateService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAdministrativeTemplateService).GetMethod("ListAdministrativeTemplatesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<GroupPolicyConfiguration>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAdministrativeTemplateService).GetMethod("GetAdministrativeTemplateAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<GroupPolicyConfiguration?>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IAdministrativeTemplateService).GetMethod("CreateAdministrativeTemplateAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<GroupPolicyConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(GroupPolicyConfiguration), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IAdministrativeTemplateService).GetMethod("UpdateAdministrativeTemplateAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<GroupPolicyConfiguration>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(GroupPolicyConfiguration), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IAdministrativeTemplateService).GetMethod("DeleteAdministrativeTemplateAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesGetAssignmentsMethod()
    {
        var method = typeof(IAdministrativeTemplateService).GetMethod("GetAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<GroupPolicyConfigurationAssignment>>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesAssignMethod()
    {
        var method = typeof(IAdministrativeTemplateService).GetMethod("AssignAdministrativeTemplateAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(List<GroupPolicyConfigurationAssignment>), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAdministrativeTemplateService).GetMethods();
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
        var export = new AdministrativeTemplateExport
        {
            Template = new GroupPolicyConfiguration { Id = "test", DisplayName = "Test" }
        };

        Assert.NotNull(export.Template);
        Assert.Empty(export.Assignments);
    }

    [Fact]
    public void ExportModel_AssignmentsDefaultsToEmptyList()
    {
        var export = new AdministrativeTemplateExport
        {
            Template = new GroupPolicyConfiguration()
        };

        Assert.NotNull(export.Assignments);
        Assert.IsType<List<GroupPolicyConfigurationAssignment>>(export.Assignments);
    }

    [Fact]
    public void ExportModel_CanSetAssignments()
    {
        var assignments = new List<GroupPolicyConfigurationAssignment>
        {
            new() { Id = "a1" },
            new() { Id = "a2" }
        };

        var export = new AdministrativeTemplateExport
        {
            Template = new GroupPolicyConfiguration(),
            Assignments = assignments
        };

        Assert.Equal(2, export.Assignments.Count);
    }
}
