using Intune.Commander.Core.Models;
using Intune.Commander.Core.Services;
using Microsoft.Graph.Beta;

namespace Intune.Commander.Core.Tests.Services;

public class AssignmentCheckerServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IAssignmentCheckerService)
            .IsAssignableFrom(typeof(AssignmentCheckerService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var ctors = typeof(AssignmentCheckerService).GetConstructors();
        Assert.True(ctors.Any(c =>
        {
            var p = c.GetParameters();
            return p.Length >= 1 && p[0].ParameterType == typeof(GraphServiceClient);
        }));
    }

    // ── Interface contract tests ─────────────────────────────────────────────────

    [Fact]
    public void Interface_DefinesGetUserAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetUserAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
        var p = method.GetParameters();
        Assert.Equal(typeof(string), p[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), p[3].ParameterType);
    }

    [Fact]
    public void Interface_DefinesGetGroupAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetGroupAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
        var p = method.GetParameters();
        Assert.Equal(typeof(string), p[0].ParameterType); // groupId
        Assert.Equal(typeof(string), p[1].ParameterType); // groupName
    }

    [Fact]
    public void Interface_DefinesGetDeviceAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetDeviceAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
        var p = method.GetParameters();
        Assert.Equal(typeof(string), p[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesGetAllPoliciesWithAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetAllPoliciesWithAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetAllUsersAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetAllUsersAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetAllDevicesAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetAllDevicesAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetUnassignedPoliciesAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetUnassignedPoliciesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetEmptyGroupAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetEmptyGroupAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesCompareGroupAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("CompareGroupAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
        var p = method.GetParameters();
        Assert.Equal(4, p.Length - p.Count(pp => pp.IsOptional)); // 4 required: id1,name1,id2,name2
    }

    [Fact]
    public void Interface_DefinesGetFailedAssignmentsAsync()
    {
        var method = typeof(IAssignmentCheckerService)
            .GetMethod("GetFailedAssignmentsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AssignmentReportRow>>), method.ReturnType);
    }

    // ── CancellationToken default parameter tests ────────────────────────────────

    [Theory]
    [InlineData("GetUserAssignmentsAsync")]
    [InlineData("GetGroupAssignmentsAsync")]
    [InlineData("GetDeviceAssignmentsAsync")]
    [InlineData("GetAllPoliciesWithAssignmentsAsync")]
    [InlineData("GetAllUsersAssignmentsAsync")]
    [InlineData("GetAllDevicesAssignmentsAsync")]
    [InlineData("GetUnassignedPoliciesAsync")]
    [InlineData("GetEmptyGroupAssignmentsAsync")]
    [InlineData("GetCompareGroupAssignmentsAsync")]
    [InlineData("GetFailedAssignmentsAsync")]
    public void InterfaceMethod_HasCancellationTokenWithDefault(string methodName)
    {
        // Map "GetCompareGroupAssignmentsAsync" → actual name
        var name = methodName == "GetCompareGroupAssignmentsAsync"
            ? "CompareGroupAssignmentsAsync"
            : methodName;

        var method = typeof(IAssignmentCheckerService).GetMethod(name);
        if (method == null) return; // already verified above

        var ctParam = method.GetParameters()
            .FirstOrDefault(p => p.ParameterType == typeof(CancellationToken));
        Assert.NotNull(ctParam);
        Assert.True(ctParam!.HasDefaultValue);
    }

    // ── AssignmentReportRow model tests ─────────────────────────────────────────

    [Fact]
    public void AssignmentReportRow_HasRequiredProperties()
    {
        var row = new AssignmentReportRow
        {
            PolicyId = "id1",
            PolicyName = "Test Policy",
            PolicyType = "Device Configuration",
            Platform = "Windows",
            AssignmentSummary = "All Users",
            AssignmentReason = "All Users",
            GroupId = "",
            GroupName = "",
            Group1Status = "Include",
            Group2Status = "",
            TargetDevice = "Device1",
            UserPrincipalName = "user@contoso.com",
            Status = "error",
            LastReported = "1/1/2025 12:00 PM"
        };

        Assert.Equal("id1", row.PolicyId);
        Assert.Equal("Test Policy", row.PolicyName);
        Assert.Equal("Device Configuration", row.PolicyType);
        Assert.Equal("Windows", row.Platform);
        Assert.Equal("All Users", row.AssignmentSummary);
        Assert.Equal("All Users", row.AssignmentReason);
        Assert.Equal("Include", row.Group1Status);
        Assert.Equal("error", row.Status);
    }

    [Fact]
    public void AssignmentReportRow_DefaultValues_AreEmpty()
    {
        var row = new AssignmentReportRow();
        Assert.Equal("", row.PolicyId);
        Assert.Equal("", row.PolicyName);
        Assert.Equal("", row.PolicyType);
        Assert.Equal("", row.Platform);
        Assert.Equal("", row.AssignmentSummary);
        Assert.Equal("", row.AssignmentReason);
        Assert.Equal("", row.GroupId);
        Assert.Equal("", row.GroupName);
        Assert.Equal("", row.Group1Status);
        Assert.Equal("", row.Group2Status);
        Assert.Equal("", row.TargetDevice);
        Assert.Equal("", row.UserPrincipalName);
        Assert.Equal("", row.Status);
        Assert.Equal("", row.LastReported);
    }
}
