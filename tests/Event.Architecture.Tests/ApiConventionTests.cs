namespace Event.Architecture.Tests;

using System.Reflection;
using Asp.Versioning;
using Explore.API.Controllers;
using Explore.API.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;

/// <summary>
/// Tests that enforce API conventions across all controllers.
/// Ensures consistent versioning, authorization, and middleware usage.
/// </summary>
public class ApiConventionTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;
    private static readonly string[] TenantFilterBypassTokens =
    [
        "IgnoreQueryFilters(",
        "IgnoreTenantFilter(",
        "IgnoreAllFilters("
    ];

    #region API Versioning Conventions

    [Test]
    [DisplayName("All controllers must have [ApiVersion] attribute")]
    public async Task AllControllers_ShouldHave_ApiVersionAttribute()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var hasApiVersion = controller.GetCustomAttributes(typeof(ApiVersionAttribute), true).Length > 0;
            if (!hasApiVersion)
            {
                violations.Add(controller.Name);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("all controllers must declare [ApiVersion(\"1.0\")] for formal API versioning");
    }

    [Test]
    [DisplayName("All controller classes must inherit from ControllerBase")]
    public async Task AllControllers_ShouldInherit_ControllerBase()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("Explore.API.Controllers")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .HaveNameEndingWith("Controller")
            .Should()
            .Inherit(typeof(ControllerBase))
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    #endregion

    #region Authorization Conventions

    [Test]
    [DisplayName("All controllers must have [ApiController] attribute")]
    public async Task AllControllers_ShouldHave_ApiControllerAttribute()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var hasApiController = controller.GetCustomAttributes(typeof(ApiControllerAttribute), true).Length > 0;
            if (!hasApiController)
            {
                violations.Add(controller.Name);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("all controllers must have [ApiController] for consistent model validation and error responses");
    }

    [Test]
    [DisplayName("All controllers must have [Route] attribute")]
    public async Task AllControllers_ShouldHave_RouteAttribute()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var hasRoute = controller.GetCustomAttributes(typeof(RouteAttribute), true).Length > 0;
            if (!hasRoute)
            {
                violations.Add(controller.Name);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("all controllers must declare an explicit [Route] attribute");
    }

    #endregion

    #region Clean Architecture Boundary Conventions

    [Test]
    [DisplayName("Controllers must not inject repositories directly")]
    public async Task Controllers_ShouldNotInject_RepositoriesDirectly()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var constructors = controller.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var constructor in constructors)
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    if (IsRepositoryContract(parameter.ParameterType))
                    {
                        violations.Add($"{controller.Name} constructor parameter '{parameter.Name}' uses {parameter.ParameterType.FullName}");
                    }
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("API controllers must stay thin HTTP adapters; repository access belongs behind Application/Persistence boundaries.");
    }

    [Test]
    [DisplayName("Controller actions must not return Domain entities")]
    public async Task ControllerActions_ShouldNotReturn_DomainEntities()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsHttpAction);

            foreach (var action in actions)
            {
                var domainTypes = EnumerateContractTypes(action.ReturnType)
                    .Where(IsDomainContract)
                    .Select(type => type.FullName ?? type.Name)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                foreach (var domainType in domainTypes)
                {
                    violations.Add($"{controller.Name}.{action.Name} returns {domainType}");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("API actions must expose DTO/HAL/ProblemDetails contracts, never Domain entities; mapping belongs in handlers and assemblers.");
    }

    [Test]
    [DisplayName("Controllers must not call tenant filter bypass APIs")]
    public async Task Controllers_ShouldNotCall_TenantFilterBypassApis()
    {
        var controllerSourceFiles = Directory
            .GetFiles(ContextSystemHelpers.RepoPath("src", "Explore.API", "Controllers"), "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var violations = new List<string>();

        foreach (var sourceFile in controllerSourceFiles)
        {
            var source = await File.ReadAllTextAsync(sourceFile);
            foreach (var token in TenantFilterBypassTokens)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(ContextSystemHelpers.RepoRoot, sourceFile)} uses {token}");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("controllers must never bypass tenant filters; cross-tenant reads require explicit host/system execution APIs with reason logging.");
    }

    #endregion

    #region Middleware Conventions

    [Test]
    [DisplayName("Middleware classes should be sealed")]
    public async Task Middleware_ShouldBe_Sealed()
    {
        var middlewareTypes = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("Explore.API.Middleware")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .HaveNameEndingWith("Middleware")
            .GetTypes();

        var violations = new List<string>();

        foreach (var middleware in middlewareTypes)
        {
            if (!middleware.IsSealed)
            {
                violations.Add(middleware.Name);
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("middleware classes should be sealed for performance (prevents virtual dispatch)");
    }

    #endregion

    #region Onboarding API Conventions

    [Test]
    [DisplayName("Tenant onboarding API must require MultiTenant deployment mode")]
    public async Task TenantOnboardingController_ShouldRequire_MultiTenantMode()
    {
        var hasRequireMultiTenant = typeof(TenantOnboardingController)
            .GetCustomAttributes(typeof(RequireMultiTenantAttribute), inherit: true)
            .Length > 0;

        await Assert.That(hasRequireMultiTenant).IsTrue()
            .Because("SingleTenant onboarding hides tenant concepts, so the matching API must not remain normally usable.");
    }

    #endregion

    private static bool IsHttpAction(MethodInfo method)
    {
        if (method.IsSpecialName)
        {
            return false;
        }

        return method.GetCustomAttributes(inherit: true)
            .Any(attribute => attribute is HttpMethodAttribute);
    }

    private static bool IsRepositoryContract(Type type)
    {
        var namespaceName = type.Namespace ?? string.Empty;
        return type.Name.EndsWith("Repository", StringComparison.Ordinal)
            || type.Name.EndsWith("Repository`1", StringComparison.Ordinal)
            || namespaceName.Contains(".Repositories", StringComparison.Ordinal)
            || namespaceName.Contains(".Persistence", StringComparison.Ordinal);
    }

    private static bool IsDomainContract(Type type)
    {
        var unwrapped = Nullable.GetUnderlyingType(type) ?? type;
        return string.Equals(unwrapped.Namespace, "Explore.Domain", StringComparison.Ordinal)
            || (unwrapped.Namespace?.StartsWith("Explore.Domain.", StringComparison.Ordinal) ?? false);
    }

    private static IEnumerable<Type> EnumerateContractTypes(Type type)
    {
        var unwrapped = Nullable.GetUnderlyingType(type) ?? type;

        if (unwrapped == typeof(void)
            || unwrapped == typeof(Task)
            || unwrapped == typeof(ValueTask)
            || unwrapped == typeof(string)
            || unwrapped.IsPrimitive
            || unwrapped.IsEnum)
        {
            yield break;
        }

        if (unwrapped.IsGenericType)
        {
            var genericType = unwrapped.GetGenericTypeDefinition();
            if (genericType == typeof(Task<>)
                || genericType == typeof(ValueTask<>)
                || genericType == typeof(ActionResult<>))
            {
                foreach (var nested in EnumerateContractTypes(unwrapped.GetGenericArguments()[0]))
                {
                    yield return nested;
                }

                yield break;
            }

            foreach (var argument in unwrapped.GetGenericArguments())
            {
                foreach (var nested in EnumerateContractTypes(argument))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        if (unwrapped.IsArray)
        {
            foreach (var nested in EnumerateContractTypes(unwrapped.GetElementType()!))
            {
                yield return nested;
            }

            yield break;
        }

        yield return unwrapped;
    }
}
