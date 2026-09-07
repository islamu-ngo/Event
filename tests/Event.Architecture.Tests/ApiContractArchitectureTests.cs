namespace Event.Architecture.Tests;

using System.Linq;
using System.Reflection;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Features.Events.Requests.Queries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;

public class ApiContractArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(GetEventListRequest).Assembly;

    private static readonly Type[] HttpVerbAttributes =
    {
        typeof(HttpGetAttribute),
        typeof(HttpPostAttribute),
        typeof(HttpPutAttribute),
        typeof(HttpPatchAttribute),
        typeof(HttpDeleteAttribute),
        typeof(HttpOptionsAttribute),
        typeof(HttpHeadAttribute)
    };

    [Test]
    [DisplayName("ApiProblemCodes must expose every initial catalog code")]
    public async Task ApiProblemCodes_MustExpose_EveryInitialCatalogCode()
    {
        var apiProblemCodesType = ApiAssembly.GetType("Explore.API.ExceptionHandling.ApiProblemCodes")
            ?? throw new InvalidOperationException("ApiProblemCodes type not found.");
        var codes = apiProblemCodesType
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var expectedCodes = new[]
        {
            "validation_failed",
            "tenant_required",
            "authentication_required",
            "forbidden",
            "resource_not_found",
            "resource_conflict",
            "concurrency_conflict",
            "duplicate_request",
            "rate_limited",
            "unexpected_error"
        };

        await Assert.That(expectedCodes.Except(codes, StringComparer.Ordinal)).IsEmpty()
            .Because("the Phase 2 API error catalog requires stable machine-readable codes for every initial ProblemDetails category.");
    }

    [Test]
    [DisplayName("Central API exception and validation writers must emit ProblemDetails code extensions")]
    public async Task CentralProblemDetailsWriters_MustEmit_CodeExtensions()
    {
        var sourceRoot = LocateSourceRoot();
        var sourceFiles = new[]
        {
            Path.Combine(sourceRoot, "src", "Explore.API", "ExceptionHandling", "GlobalExceptionHandler.cs"),
            Path.Combine(sourceRoot, "src", "Explore.API", "ExceptionHandling", "ValidationExceptionHandler.cs"),
            Path.Combine(sourceRoot, "src", "Explore.API", "ExceptionHandling", "ApiValidationProblemDetailsFactory.cs")
        };

        var violations = new List<string>();
        foreach (var sourceFile in sourceFiles)
        {
            var source = await File.ReadAllTextAsync(sourceFile);
            if (!source.Contains("Extensions[\"code\"]", StringComparison.Ordinal))
            {
                violations.Add(Path.GetRelativePath(sourceRoot, sourceFile));
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("every central ProblemDetails writer must attach the stable machine-readable `code` extension.");
    }

    [Test]
    [DisplayName("DTO enum properties must be registered in the OpenAPI string-enum schema catalog")]
    public async Task DtoEnumProperties_MustBeRegisteredIn_OpenApiStringEnumSchemaCatalog()
    {
        var dtoEnumTypes = ApplicationAssembly
            .GetExportedTypes()
            .Where(IsPublicDtoContractType)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(property => CollectEnumTypes(property.PropertyType))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        var catalogType = ApiAssembly.GetType("Explore.API.OpenApi.OpenApiStringEnumSchemaCatalog")
            ?? throw new InvalidOperationException("OpenApiStringEnumSchemaCatalog type not found.");
        var enumTypesProperty = catalogType.GetProperty("EnumTypes", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("OpenApiStringEnumSchemaCatalog.EnumTypes property not found.");
        var catalogEnumTypes = ((IEnumerable<Type>?)enumTypesProperty.GetValue(null) ?? Enumerable.Empty<Type>())
            .ToHashSet();

        var missing = dtoEnumTypes
            .Where(enumType => !catalogEnumTypes.Contains(enumType))
            .Select(enumType => enumType.FullName ?? enumType.Name)
            .ToList();

        await Assert.That(missing).IsEmpty()
            .Because("every enum exposed by public API DTOs must be registered in OpenApiStringEnumSchemaCatalog so OpenAPI emits string enum schemas matching the API JSON string-enum contract.");
    }


    [Test]
    [DisplayName("Controllers must not return raw missing-user Unauthorized strings")]
    public async Task Controllers_MustNotReturn_RawMissingUserUnauthorizedStrings()
    {
        var sourceRoot = LocateSourceRoot();
        var controllersRoot = Path.Combine(sourceRoot, "src", "Explore.API", "Controllers");
        var violations = Directory
            .EnumerateFiles(controllersRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(file => new
            {
                File = file,
                Source = File.ReadAllText(file)
            })
            .Where(candidate => candidate.Source.Contains("Unauthorized(\"User ID not found in token\")", StringComparison.Ordinal))
            .Select(candidate => Path.GetRelativePath(sourceRoot, candidate.File))
            .ToList();

        await Assert.That(violations).IsEmpty()
            .Because("missing authenticated user identifiers must use catalog-backed ProblemDetails with the `authentication_required` code instead of raw string 401 responses.");
    }

    [Test]
    [DisplayName("Every non-hidden [Http*] action must have Name= set")]
    public async Task EveryNonHiddenAction_MustHave_RouteName()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .ToList();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var controllerHidden = controller
                .GetCustomAttributes(typeof(ApiExplorerSettingsAttribute), true)
                .Cast<ApiExplorerSettingsAttribute>()
                .Any(a => a.IgnoreApi);

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsHttpAction)
                .ToList();

            foreach (var action in actions)
            {
                if (controllerHidden)
                {
                    continue;
                }

                var actionHidden = action
                    .GetCustomAttributes(typeof(ApiExplorerSettingsAttribute), true)
                    .Cast<ApiExplorerSettingsAttribute>()
                    .Any(a => a.IgnoreApi);

                if (actionHidden)
                {
                    continue;
                }

                var httpAttr = action.GetCustomAttributes(true)
                    .FirstOrDefault(a => HttpVerbAttributes.Any(h => h.IsInstanceOfType(a)));

                if (httpAttr is not IRouteTemplateProvider { Name: not null and not "" })
                {
                    violations.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("every non-hidden HTTP action must set Name= on its [Http*] attribute for stable operationIds; see docs/GOVERNANCE.md#api-contract-rules");
    }

    [Test]
    [Skip("Category: API contract. Removal: enable after every public operation declares explicit response metadata.")]
    [DisplayName("Every non-hidden [Http*] action must declare response metadata")]
    public async Task EveryNonHiddenAction_MustDeclare_ResponseMetadata()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .ToList();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            if (IsHiddenFromApiExplorer(controller))
            {
                continue;
            }

            var controllerResponseMetadata = controller
                .GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true)
                .ToList();

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsHttpAction)
                .ToList();

            foreach (var action in actions)
            {
                if (IsHiddenFromApiExplorer(action))
                {
                    continue;
                }

                var hasResponseMetadata = controllerResponseMetadata.Count > 0
                    || action.GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true).Any();

                if (!hasResponseMetadata)
                {
                    violations.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("every public API operation must declare explicit response metadata for generated clients and ProblemDetails contracts.");
    }

    [Test]
    [DisplayName("Actions with response metadata must declare a successful or redirect response")]
    public async Task ActionsWithResponseMetadata_MustDeclare_NonErrorResponse()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .ToList();

        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            if (IsHiddenFromApiExplorer(controller))
            {
                continue;
            }

            var controllerResponseMetadata = controller
                .GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true)
                .ToList();

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsHttpAction)
                .ToList();

            foreach (var action in actions)
            {
                if (IsHiddenFromApiExplorer(action))
                {
                    continue;
                }

                var responseMetadata = controllerResponseMetadata
                    .Concat(action.GetCustomAttributes<ProducesResponseTypeAttribute>(inherit: true))
                    .ToList();

                if (responseMetadata.Count == 0)
                {
                    continue;
                }

                if (!responseMetadata.Any(attribute => IsNonErrorStatusCode(attribute.StatusCode)))
                {
                    violations.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("actions that opt into response metadata must include at least one explicit 2xx success or 3xx redirect response so OpenAPI consumers can distinguish valid outcomes from ProblemDetails/error responses without inventing a false 2xx contract for redirects.");
    }

    [Test]
    [DisplayName("Route names must be unique across all controllers")]
    public async Task RouteNames_MustBe_Unique()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .ToList();

        var names = new Dictionary<string, string>();

        foreach (var controller in controllerTypes)
        {
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsHttpAction)
                .ToList();

            foreach (var action in actions)
            {
                var httpAttr = action.GetCustomAttributes(true)
                    .FirstOrDefault(a => HttpVerbAttributes.Any(h => h.IsInstanceOfType(a)));

                if (httpAttr is IRouteTemplateProvider { Name: string name })
                {
                    if (names.TryGetValue(name, out var existing))
                    {
                        names[name] = $"{existing}; {controller.Name}.{action.Name}";
                    }
                    else
                    {
                        names[name] = $"{controller.Name}.{action.Name}";
                    }
                }
            }
        }

        var duplicates = names
            .Where(kvp => kvp.Value.Contains(';'))
            .Select(kvp => $"{kvp.Key}: used by {kvp.Value}")
            .ToList();

        await Assert.That(duplicates).IsEmpty()
            .Because("route names must be unique; duplicates cause operationId collisions in OpenAPI");
    }

    [Test]
    [DisplayName("Legacy storage writes must stay absent while provider-neutral upload sessions remain")]
    public async Task StorageWrites_MustExposeOnlyProviderNeutralUploadSessions()
    {
        var controller = ApiAssembly.GetType("Explore.API.Controllers.StorageObjectController")!;
        var routes = controller
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Select(attribute => (attribute.HttpMethods.Single(), attribute.Template))
            .ToArray();

        await Assert.That(routes).DoesNotContain(("POST", "generate-upload-url"));
        await Assert.That(routes).DoesNotContain(("POST", null));
        await Assert.That(routes).Contains(("POST", "upload-sessions"));
        await Assert.That(routes).Contains(("PUT", "upload-sessions/{uploadSessionId:guid}/content"));
        await Assert.That(typeof(RouteNames).GetField("GenerateStorageObjectUploadUrl")).IsNull();
        await Assert.That(typeof(RouteNames).GetField("CreateStorageObject")).IsNull();
        await Assert.That(ApplicationAssembly.GetType(
            "Explore.Application.Features.StorageObjects.Requests.Commands.GenerateUploadUrlCommand")).IsNull();
        await Assert.That(ApplicationAssembly.GetType(
            "Explore.Application.Features.StorageObjects.Requests.Commands.CreateStorageObjectCommand")).IsNull();
    }

    [Test]
    [DisplayName("Every controller action must have a unique operation identity (controller.action)")]
    public async Task EveryAction_MustHave_UniqueIdentity()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .ToList();

        var identities = new HashSet<string>();
        var duplicates = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsHttpAction)
                .ToList();

            foreach (var action in actions)
            {
                var identity = $"{controller.Name}.{action.Name}";
                if (!identities.Add(identity))
                {
                    duplicates.Add(identity);
                }
            }
        }

        await Assert.That(duplicates).IsEmpty()
            .Because("every action must be uniquely identifiable by controller.action; overloaded actions break OpenAPI generation");
    }

    [Test]
    [DisplayName("Public event-list ownership filters must stay actor-backed and nullable")]
    public async Task EventListOwnershipFilters_MustBe_NullableActorBackedContractOnly()
    {
        var requiredFilterNames = new[] { "ActorId", "OrganizationId", "GroupId" };
        var forbiddenContractNames = new[] { "WorkspaceId", "OrganizerScopeId", "OrganizationScopeId", "OrganizationScope", "TenantWorkspace", "ScopeId" };

        var apiProperties = typeof(EventFilterRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var requestProperties = typeof(GetEventListRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var violations = new List<string>();

        foreach (var propertyName in requiredFilterNames)
        {
            if (apiProperties.SingleOrDefault(p => p.Name == propertyName)?.PropertyType != typeof(Guid?))
                violations.Add($"EventFilterRequest.{propertyName} must be Guid? for optional query binding");

            if (requestProperties.SingleOrDefault(p => p.Name == propertyName)?.PropertyType != typeof(Guid?))
                violations.Add($"GetEventListRequest.{propertyName} must be Guid? for optional application filtering");
        }

        foreach (var forbiddenName in forbiddenContractNames)
        {
            if (apiProperties.Any(p => p.Name == forbiddenName))
                violations.Add($"EventFilterRequest must not expose {forbiddenName}; use ActorId/OrganizationId/GroupId only");

            if (requestProperties.Any(p => p.Name == forbiddenName))
                violations.Add($"GetEventListRequest must not expose {forbiddenName}; use ActorId/OrganizationId/GroupId only");
        }

        await Assert.That(violations).IsEmpty()
            .Because("the public /events list ownership contract must remain precise and actor-backed without introducing workspace/scope concepts");
    }

    [Test]
    [DisplayName("Event creation must not reintroduce child-event hierarchy contracts")]
    public async Task EventCreationContracts_MustNot_ReintroduceChildEventHierarchy()
    {
        var forbiddenTokens = new[] { "ParentEventId", "ChildEvent", "ChildEvents", "Subevent", "SubEvent", "subevent", "sub-event", "child-event" };
        var violations = new List<string>();

        foreach (var token in forbiddenTokens)
        {
            if (typeof(Explore.Domain.Event).GetProperty(token, BindingFlags.Public | BindingFlags.Instance) is not null)
                violations.Add($"Explore.Domain.Event must not expose {token}; program items belong to EventSession.");
        }

        var routeNameValues = typeof(RouteNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (Name: field.Name, Value: (string?)field.GetRawConstantValue() ?? string.Empty));

        foreach (var routeName in routeNameValues)
        {
            foreach (var token in forbiddenTokens.Where(token => !string.Equals(token, "ParentEventId", StringComparison.Ordinal)))
            {
                if (routeName.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || routeName.Value.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"RouteNames.{routeName.Name} must not expose rejected child-event route '{routeName.Value}'.");
                }
            }
        }

        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .ToList();

        foreach (var controller in controllerTypes)
        {
            var controllerRouteTokens = controller
                .GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>()
                .Select(attribute => attribute.Template ?? string.Empty);

            var actionRouteTokens = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes(true)
                    .OfType<IRouteTemplateProvider>()
                    .Select(attribute => attribute.Template ?? string.Empty));

            foreach (var template in controllerRouteTokens.Concat(actionRouteTokens))
            {
                foreach (var token in forbiddenTokens.Where(token => !string.Equals(token, "ParentEventId", StringComparison.Ordinal)))
                {
                    if (template.Contains(token, StringComparison.OrdinalIgnoreCase))
                        violations.Add($"Route template '{template}' must not expose rejected child-event hierarchy language.");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("event creation progressive disclosure must model talks/workshops as EventSession, not child Event/ParentEventId hierarchy");
    }

    private static bool IsHttpAction(MethodInfo method)
    {
        if (method.IsSpecialName)
        {
            return false;
        }

        foreach (var attr in HttpVerbAttributes)
        {
            if (method.GetCustomAttributes(attr, inherit: true).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> CollectEnumTypes(Type type)
    {
        var underlyingNullableType = Nullable.GetUnderlyingType(type);
        if (underlyingNullableType is not null)
        {
            type = underlyingNullableType;
        }

        if (type.IsEnum)
        {
            yield return type;
            yield break;
        }

        if (type.IsArray && type.GetElementType() is { } elementType)
        {
            foreach (var enumType in CollectEnumTypes(elementType))
            {
                yield return enumType;
            }

            yield break;
        }

        if (!type.IsGenericType || type == typeof(string))
        {
            yield break;
        }

        foreach (var genericArgument in type.GetGenericArguments())
        {
            foreach (var enumType in CollectEnumTypes(genericArgument))
            {
                yield return enumType;
            }
        }
    }

    private static bool IsPublicDtoContractType(Type type)
        => type.Namespace?.StartsWith("Explore.Application.DTOs", StringComparison.Ordinal) == true
           && !type.Namespace.Contains(".Validators", StringComparison.Ordinal)
           && !type.Name.EndsWith("Validator", StringComparison.Ordinal);

    private static string LocateSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx")) &&
                (Directory.Exists(Path.Combine(current.FullName, "Explore.API")) ||
                 Directory.Exists(Path.Combine(current.FullName, "src", "Explore.API"))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Explore.slnx and Explore.API.");
    }

    private static bool IsHiddenFromApiExplorer(MemberInfo member)
    {
        return member.GetCustomAttributes<ApiExplorerSettingsAttribute>(inherit: true)
            .Any(attribute => attribute.IgnoreApi);
    }

    private static bool IsNonErrorStatusCode(int statusCode)
    {
        return statusCode is >= StatusCodes.Status200OK and < StatusCodes.Status400BadRequest;
    }
}
