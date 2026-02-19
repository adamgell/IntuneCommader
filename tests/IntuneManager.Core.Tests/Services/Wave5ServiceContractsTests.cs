using IntuneManager.Core.Services;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Services;

public class Wave5ServiceContractsTests
{
    public static IEnumerable<object[]> ServiceContracts()
    {
        yield return [typeof(NamedLocationService), typeof(INamedLocationService)];
        yield return [typeof(AuthenticationStrengthService), typeof(IAuthenticationStrengthService)];
        yield return [typeof(AuthenticationContextService), typeof(IAuthenticationContextService)];
        yield return [typeof(TermsOfUseService), typeof(ITermsOfUseService)];
    }

    [Theory]
    [MemberData(nameof(ServiceContracts))]
    public void Service_ImplementsInterface(Type serviceType, Type interfaceType)
    {
        Assert.True(interfaceType.IsAssignableFrom(serviceType));
    }

    [Theory]
    [MemberData(nameof(ServiceContracts))]
    public void Service_HasGraphClientConstructor(Type serviceType, Type _)
    {
        var constructor = serviceType.GetConstructor([typeof(GraphServiceClient)]);
        Assert.NotNull(constructor);
    }
}

#region NamedLocationService

public class NamedLocationServiceContractTests
{
    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("ListNamedLocationsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<NamedLocation>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("GetNamedLocationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<NamedLocation?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("CreateNamedLocationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<NamedLocation>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(NamedLocation), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("UpdateNamedLocationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<NamedLocation>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(NamedLocation), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(INamedLocationService).GetMethod("DeleteNamedLocationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(INamedLocationService).GetMethods();
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
        var methods = typeof(INamedLocationService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}

#endregion

#region AuthenticationStrengthService

public class AuthenticationStrengthServiceContractTests
{
    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("ListAuthenticationStrengthPoliciesAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AuthenticationStrengthPolicy>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("GetAuthenticationStrengthPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationStrengthPolicy?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("CreateAuthenticationStrengthPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationStrengthPolicy>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(AuthenticationStrengthPolicy), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("UpdateAuthenticationStrengthPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationStrengthPolicy>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(AuthenticationStrengthPolicy), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IAuthenticationStrengthService).GetMethod("DeleteAuthenticationStrengthPolicyAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAuthenticationStrengthService).GetMethods();
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
        var methods = typeof(IAuthenticationStrengthService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}

#endregion

#region AuthenticationContextService

public class AuthenticationContextServiceContractTests
{
    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("ListAuthenticationContextsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<AuthenticationContextClassReference>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("GetAuthenticationContextAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationContextClassReference?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("CreateAuthenticationContextAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationContextClassReference>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(AuthenticationContextClassReference), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("UpdateAuthenticationContextAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<AuthenticationContextClassReference>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(AuthenticationContextClassReference), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(IAuthenticationContextService).GetMethod("DeleteAuthenticationContextAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(IAuthenticationContextService).GetMethods();
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
        var methods = typeof(IAuthenticationContextService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}

#endregion

#region TermsOfUseService

public class TermsOfUseServiceContractTests
{
    [Fact]
    public void Interface_DefinesListMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("ListTermsOfUseAgreementsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<List<Agreement>>), method.ReturnType);
    }

    [Fact]
    public void Interface_DefinesGetMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("GetTermsOfUseAgreementAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Agreement?>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesCreateMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("CreateTermsOfUseAgreementAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Agreement>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(Agreement), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void Interface_DefinesUpdateMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("UpdateTermsOfUseAgreementAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Agreement>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(Agreement), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_DefinesDeleteMethod()
    {
        var method = typeof(ITermsOfUseService).GetMethod("DeleteTermsOfUseAgreementAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void Interface_AllMethodsAcceptCancellationToken()
    {
        var methods = typeof(ITermsOfUseService).GetMethods();
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
        var methods = typeof(ITermsOfUseService).GetMethods();
        Assert.Equal(5, methods.Length);
    }
}

#endregion
