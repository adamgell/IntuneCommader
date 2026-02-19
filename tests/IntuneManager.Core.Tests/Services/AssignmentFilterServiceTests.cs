using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class AssignmentFilterServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IAssignmentFilterService).IsAssignableFrom(typeof(AssignmentFilterService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(AssignmentFilterService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAssignmentFilterService).GetMethod("ListFiltersAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<DeviceAndAppManagementAssignmentFilter>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAssignmentFilterService).GetMethod("GetFilterAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeviceAndAppManagementAssignmentFilter?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAssignmentFilterService).GetMethods();
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var lastParam = parameters[^1];
            Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
            Assert.True(lastParam.HasDefaultValue);
        }
    }

    [Fact]
    public void Interface_HasTwoMethods()
    {
        var methods = typeof(IAssignmentFilterService).GetMethods();
        Assert.Equal(2, methods.Length);
    }
}
