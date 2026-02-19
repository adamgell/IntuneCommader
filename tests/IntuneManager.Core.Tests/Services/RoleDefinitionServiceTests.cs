using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class RoleDefinitionServiceTests
{
    [Fact]
    public void Service_ImplementsInterface()
    {
        Assert.True(typeof(IRoleDefinitionService).IsAssignableFrom(typeof(RoleDefinitionService)));
    }

    [Fact]
    public void Service_HasGraphClientConstructor()
    {
        var constructor = typeof(RoleDefinitionService).GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IRoleDefinitionService).GetMethod("ListRoleDefinitionsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<RoleDefinition>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IRoleDefinitionService).GetMethod("GetRoleDefinitionAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<RoleDefinition?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IRoleDefinitionService).GetMethod("CreateRoleDefinitionAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<RoleDefinition>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(RoleDefinition), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IRoleDefinitionService).GetMethod("UpdateRoleDefinitionAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<RoleDefinition>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(RoleDefinition), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IRoleDefinitionService).GetMethod("DeleteRoleDefinitionAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IRoleDefinitionService).GetMethods();
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
        var methods = typeof(IRoleDefinitionService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}
