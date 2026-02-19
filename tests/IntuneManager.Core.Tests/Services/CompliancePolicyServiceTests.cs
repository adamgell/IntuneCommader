using IntuneManager.Core.Models;
using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class CompliancePolicyServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(ICompliancePolicyService).IsAssignableFrom(typeof(CompliancePolicyService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(CompliancePolicyService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(ICompliancePolicyService).GetMethod("ListCompliancePoliciesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceCompliancePolicy>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(ICompliancePolicyService).GetMethod("GetCompliancePolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceCompliancePolicy?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(ICompliancePolicyService).GetMethod("CreateCompliancePolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceCompliancePolicy>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceCompliancePolicy), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(ICompliancePolicyService).GetMethod("UpdateCompliancePolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceCompliancePolicy>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(DeviceCompliancePolicy), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(ICompliancePolicyService).GetMethod("DeleteCompliancePolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesGetAssignmentsMethod()
    {
        var method = typeof(ICompliancePolicyService).GetMethod("GetAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceCompliancePolicyAssignment>>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesAssignPolicyMethod()
    {
        var method = typeof(ICompliancePolicyService).GetMethod("AssignPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(List<DeviceCompliancePolicyAssignment>), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(ICompliancePolicyService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void Interface_HasSevenMethods()
    {
        var methods = typeof(ICompliancePolicyService).GetMethods();
        Assert.Equal(7, methods.Length);
    }

    [Fact]
    public void ExportModel_HasRequiredProperties()
    {
        var export = new CompliancePolicyExport
        {
            Policy = new DeviceCompliancePolicy { Id = "test", DisplayName = "Test Policy" }
        };

        Assert.NotNull(export.Policy);
        Assert.Empty(export.Assignments);
    }

    [Fact]
    public void ExportModel_AssignmentsDefaultsToEmptyList()
    {
        var export = new CompliancePolicyExport
        {
            Policy = new DeviceCompliancePolicy()
        };

        Assert.NotNull(export.Assignments);
        Assert.IsType<List<DeviceCompliancePolicyAssignment>>(export.Assignments);
    }

    [Fact]
    public void ExportModel_CanSetAssignments()
    {
        var assignments = new List<DeviceCompliancePolicyAssignment>
        {
            new() { Id = "a1" },
            new() { Id = "a2" }
        };

        var export = new CompliancePolicyExport
        {
            Policy = new DeviceCompliancePolicy(),
            Assignments = assignments
        };

        Assert.Equal(2, export.Assignments.Count);
    }
}
