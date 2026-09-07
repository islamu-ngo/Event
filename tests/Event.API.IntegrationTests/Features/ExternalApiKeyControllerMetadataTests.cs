using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ExternalApiKeyControllerMetadataTests
{
    [Test]
    public async Task ControllerIsAuthenticatedEndpointClass()
    {
        var controllerType = typeof(ExternalApiKeyController);

        await Assert.That(controllerType.GetCustomAttribute<AuthorizeAttribute>())
            .IsNotNull()
            .Because("External API key management exposes sensitive per-owner credential metadata.");

        await Assert.That(controllerType.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
    }

    [Test]
    public async Task ActionsAdvertiseAuthenticationAndAuthorizationFailures()
    {
        foreach (var action in SensitiveActions())
        {
            AssertProducesProblem(action, StatusCodes.Status401Unauthorized);
            AssertProducesProblem(action, StatusCodes.Status403Forbidden);
        }
    }

    [Test]
    public async Task ActionsDoNotUseSharedOutputCache()
    {
        foreach (var action in SensitiveActions())
        {
            await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>())
                .IsNull()
                .Because($"{action.Name} returns API-key management metadata or one-time secret material.");

            var responseCache = action.GetCustomAttribute<ResponseCacheAttribute>();

            await Assert.That(responseCache).IsNotNull();
            await Assert.That(responseCache!.NoStore).IsTrue();
            await Assert.That(responseCache.Location).IsEqualTo(ResponseCacheLocation.None);
        }
    }

    [Test]
    public async Task ActionsUseNamedRateLimitPolicies()
    {
        foreach (var action in ReadActions())
        {
            await Assert.That(GetRateLimitPolicy(action)).IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        }

        foreach (var action in WriteActions())
        {
            await Assert.That(GetRateLimitPolicy(action)).IsEqualTo(RateLimitingExtensions.WritePolicy);
        }
    }

    private static IReadOnlyList<MethodInfo> SensitiveActions()
    {
        return
        [
            Action(nameof(ExternalApiKeyController.GetAll)),
            Action(nameof(ExternalApiKeyController.GetById)),
            Action(nameof(ExternalApiKeyController.Create)),
            Action(nameof(ExternalApiKeyController.Update)),
            Action(nameof(ExternalApiKeyController.Delete)),
            Action(nameof(ExternalApiKeyController.GetUsageReport))
        ];
    }

    private static IReadOnlyList<MethodInfo> ReadActions()
    {
        return
        [
            Action(nameof(ExternalApiKeyController.GetAll)),
            Action(nameof(ExternalApiKeyController.GetById)),
            Action(nameof(ExternalApiKeyController.GetUsageReport))
        ];
    }

    private static IReadOnlyList<MethodInfo> WriteActions()
    {
        return
        [
            Action(nameof(ExternalApiKeyController.Create)),
            Action(nameof(ExternalApiKeyController.Update)),
            Action(nameof(ExternalApiKeyController.Delete))
        ];
    }

    private static MethodInfo Action(string name)
    {
        var action = typeof(ExternalApiKeyController).GetMethod(name);
        ArgumentNullException.ThrowIfNull(action);
        return action;
    }

    private static string? GetRateLimitPolicy(MethodInfo method)
    {
        return method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;
    }

    private static void AssertProducesProblem(MethodInfo method, int statusCode)
    {
        var hasProblemMetadata = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == typeof(ProblemDetails));

        if (!hasProblemMetadata)
        {
            throw new InvalidOperationException(
                $"{method.Name} must advertise ProblemDetails for HTTP {statusCode}.");
        }
    }
}
