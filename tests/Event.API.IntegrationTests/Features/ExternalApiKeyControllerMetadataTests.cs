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
    /// <summary>
    /// Verifies API-key management requires controller-level authorization and is classified
    /// as Authenticated because it exposes sensitive owner-specific credential metadata.
    /// </summary>
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

    /// <summary>
    /// Verifies every sensitive management action advertises ProblemDetails for both unauthenticated
    /// and forbidden requests, preserving the endpoint's documented authentication and authorization contract.
    /// </summary>
    [Test]
    public async Task ActionsAdvertiseAuthenticationAndAuthorizationFailures()
    {
        foreach (var action in SensitiveActions())
        {
            AssertProducesProblem(action, StatusCodes.Status401Unauthorized);
            AssertProducesProblem(action, StatusCodes.Status403Forbidden);
        }
    }

    /// <summary>
    /// Verifies sensitive actions have no shared output-cache metadata and explicitly declare
    /// no-store response caching at no cache location, protecting owner metadata and one-time credentials.
    /// </summary>
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

    /// <summary>
    /// Verifies metadata reads select the named authenticated rate-limit policy and credential
    /// mutations select the named write policy rather than an unclassified endpoint contract.
    /// </summary>
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

    /// <summary>
    /// Defines the management surface covered by problem-response and cache contracts:
    /// list, detail, creation, update, deletion, and usage reporting.
    /// </summary>
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

    /// <summary>
    /// Identifies list, detail, and usage-report reads whose endpoint metadata must select
    /// the authenticated rate-limit policy.
    /// </summary>
    private static IReadOnlyList<MethodInfo> ReadActions()
    {
        return
        [
            Action(nameof(ExternalApiKeyController.GetAll)),
            Action(nameof(ExternalApiKeyController.GetById)),
            Action(nameof(ExternalApiKeyController.GetUsageReport))
        ];
    }

    /// <summary>
    /// Identifies creation, update, and deletion mutations whose endpoint metadata must select
    /// the write rate-limit policy.
    /// </summary>
    private static IReadOnlyList<MethodInfo> WriteActions()
    {
        return
        [
            Action(nameof(ExternalApiKeyController.Create)),
            Action(nameof(ExternalApiKeyController.Update)),
            Action(nameof(ExternalApiKeyController.Delete))
        ];
    }

    /// <summary>
    /// Resolves a named controller action and fails if it no longer exists, preventing
    /// a missing management endpoint from silently escaping the metadata assertions.
    /// </summary>
    private static MethodInfo Action(string name)
    {
        var action = typeof(ExternalApiKeyController).GetMethod(name);
        ArgumentNullException.ThrowIfNull(action);
        return action;
    }

    /// <summary>
    /// Reads the action's declared rate-limit policy name, leaving absent metadata null
    /// so policy-selection assertions detect an unprotected declaration.
    /// </summary>
    private static string? GetRateLimitPolicy(MethodInfo method)
    {
        return method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;
    }

    /// <summary>
    /// Requires an exact status-code and ProblemDetails type pairing in the action's response
    /// metadata, rejecting missing or incorrectly typed authentication-failure declarations.
    /// </summary>
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
