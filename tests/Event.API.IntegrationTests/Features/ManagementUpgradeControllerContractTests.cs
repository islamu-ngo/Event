using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ManagementUpgradeControllerContractTests
{
    [Test]
    public async Task UpgradeAssessmentActions_UseProtectedPostBodyContracts()
    {
        await AssertAction(
            nameof(ManagementController.EvaluateUpgradePreflight),
            "upgrade/preflight",
            RouteNames.EvaluateManagementUpgradePreflight,
            typeof(ManagementUpgradePreflightRequestDto),
            typeof(ManagementUpgradePreflightDto));

        await AssertAction(
            nameof(ManagementController.VerifyUpgradePostflight),
            "upgrade/postflight",
            RouteNames.VerifyManagementUpgradePostflight,
            typeof(ManagementUpgradePostflightRequestDto),
            typeof(ManagementUpgradePostflightDto));
    }

    [Test]
    public async Task TenantProvisioningActions_UseDirectionalMachineContracts()
    {
        MethodInfo schedule = GetAction(nameof(ManagementController.ScheduleTenantProvisioning));
        MethodInfo status = GetAction(nameof(ManagementController.GetTenantProvisioningOperation));
        MethodInfo cancel = GetAction(nameof(ManagementController.CancelTenantProvisioningOperation));

        AssertRoute(
            schedule.GetCustomAttribute<HttpPostAttribute>(),
            "tenants/provision",
            RouteNames.ScheduleManagedTenantProvisioning);
        AssertRoute(
            status.GetCustomAttribute<HttpGetAttribute>(),
            "tenant-provisioning/{operationId:guid}",
            RouteNames.GetManagedTenantProvisioningOperation);
        AssertRoute(
            cancel.GetCustomAttribute<HttpPostAttribute>(),
            "tenant-provisioning/{operationId:guid}/cancel",
            RouteNames.CancelManagedTenantProvisioningOperation);

        await Assert.That(schedule.GetCustomAttribute<AuthorizeAttribute>()?.Policy)
            .IsEqualTo(ManagedControlPlaneAuthorizationPolicies.Write);
        await Assert.That(status.GetCustomAttribute<AuthorizeAttribute>()?.Policy)
            .IsEqualTo(ManagedControlPlaneAuthorizationPolicies.Read);
        await Assert.That(cancel.GetCustomAttribute<AuthorizeAttribute>()?.Policy)
            .IsEqualTo(ManagedControlPlaneAuthorizationPolicies.Write);
        await Assert.That(new[] { schedule, status, cancel }
                .Any(action => action.GetCustomAttribute<AllowAnonymousAttribute>() is not null))
            .IsFalse();

        ParameterInfo request = schedule.GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(ManagementTenantProvisioningRequestDto));
        await Assert.That(request.GetCustomAttribute<FromBodyAttribute>()).IsNotNull();
        await Assert.That(schedule.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(status.GetCustomAttribute<EnableRateLimitingAttribute>()).IsNull();
        await Assert.That(cancel.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);

        AssertResponse(schedule, StatusCodes.Status202Accepted, typeof(ManagementTenantProvisioningOperationDto));
        AssertResponse(schedule, StatusCodes.Status400BadRequest, typeof(ValidationProblemDetails));
        AssertResponse(schedule, StatusCodes.Status401Unauthorized, typeof(ProblemDetails));
        AssertResponse(schedule, StatusCodes.Status403Forbidden, typeof(ProblemDetails));
        AssertResponse(schedule, StatusCodes.Status409Conflict, typeof(ProblemDetails));
        AssertResponse(schedule, StatusCodes.Status429TooManyRequests, typeof(ProblemDetails));

        AssertResponse(status, StatusCodes.Status200OK, typeof(ManagementTenantProvisioningOperationDto));
        AssertResponse(status, StatusCodes.Status401Unauthorized, typeof(ProblemDetails));
        AssertResponse(status, StatusCodes.Status403Forbidden, typeof(ProblemDetails));
        AssertResponse(status, StatusCodes.Status404NotFound, typeof(ProblemDetails));

        AssertResponse(cancel, StatusCodes.Status200OK, typeof(ManagementTenantProvisioningOperationDto));
        AssertResponse(cancel, StatusCodes.Status401Unauthorized, typeof(ProblemDetails));
        AssertResponse(cancel, StatusCodes.Status403Forbidden, typeof(ProblemDetails));
        AssertResponse(cancel, StatusCodes.Status404NotFound, typeof(ProblemDetails));
        AssertResponse(cancel, StatusCodes.Status409Conflict, typeof(ProblemDetails));
        AssertResponse(cancel, StatusCodes.Status429TooManyRequests, typeof(ProblemDetails));
    }

    [Test]
    public async Task TenantProvisioningPreflight_UsesReadCredentialAndGovernedReadMetadata()
    {
        MethodInfo action = GetAction(nameof(ManagementController.EvaluateTenantProvisioningPreflight));

        AssertRoute(
            action.GetCustomAttribute<HttpPostAttribute>(),
            "tenants/preflight",
            RouteNames.EvaluateManagedTenantProvisioningPreflight);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()?.Policy)
            .IsEqualTo(ManagedControlPlaneAuthorizationPolicies.Read);
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        await Assert.That(action.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName)
            .IsEqualTo(RequestTimeoutExtensions.ControlPlanePolicy);

        ParameterInfo request = action.GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(ManagementTenantProvisioningRequestDto));
        await Assert.That(request.GetCustomAttribute<FromBodyAttribute>()).IsNotNull();

        AssertResponse(action, StatusCodes.Status200OK, typeof(ManagementTenantProvisioningPreflightDto));
        AssertResponse(action, StatusCodes.Status400BadRequest, typeof(ValidationProblemDetails));
        AssertResponse(action, StatusCodes.Status401Unauthorized, typeof(ProblemDetails));
        AssertResponse(action, StatusCodes.Status403Forbidden, typeof(ProblemDetails));
        AssertResponse(action, StatusCodes.Status429TooManyRequests, typeof(ProblemDetails));
        AssertResponse(action, StatusCodes.Status504GatewayTimeout, typeof(ProblemDetails));
    }

    [Test]
    public async Task ManagementEndpointMetadata_OverridesPublicCapabilitiesAndRateLimitsWrites()
    {
        MethodInfo capabilities = GetAction(nameof(ManagementController.GetCapabilities));
        await Assert.That(capabilities.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Public);
        await Assert.That(capabilities.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();

        foreach (MethodInfo action in new[]
                 {
                     GetAction(nameof(ManagementController.TriggerRegistration)),
                     GetAction(nameof(ManagementController.RotateCredential)),
                     GetAction(nameof(ManagementController.RevokeCredential))
                 })
        {
            await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
                .IsEqualTo(RateLimitingExtensions.WritePolicy);
            AssertResponse(action, StatusCodes.Status429TooManyRequests, typeof(ProblemDetails));
        }
    }

    private static async Task AssertAction(
        string actionName,
        string routeTemplate,
        string routeName,
        Type requestType,
        Type responseType)
    {
        var action = typeof(ManagementController).GetMethod(actionName)
            ?? throw new InvalidOperationException($"Action {actionName} not found.");
        var route = action.GetCustomAttribute<HttpPostAttribute>();
        var authorize = action.GetCustomAttribute<AuthorizeAttribute>();
        var requestParameter = action.GetParameters().Single(parameter => parameter.ParameterType == requestType);

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo(routeTemplate);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(authorize?.Policy).IsEqualTo(ManagedControlPlaneAuthorizationPolicies.Read);
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(requestParameter.GetCustomAttribute<FromBodyAttribute>()).IsNotNull();

        AssertResponse(action, StatusCodes.Status200OK, responseType);
        AssertResponse(action, StatusCodes.Status400BadRequest, typeof(ValidationProblemDetails));
        AssertResponse(action, StatusCodes.Status401Unauthorized, typeof(ProblemDetails));
        AssertResponse(action, StatusCodes.Status403Forbidden, typeof(ProblemDetails));
    }

    private static MethodInfo GetAction(string actionName) =>
        typeof(ManagementController).GetMethod(actionName)
        ?? throw new InvalidOperationException($"Action {actionName} not found.");

    private static void AssertRoute(HttpMethodAttribute? route, string template, string name)
    {
        if (route?.Template != template || route.Name != name)
        {
            throw new InvalidOperationException(
                $"Expected route '{template}' named '{name}'.");
        }
    }

    private static void AssertResponse(MethodInfo action, int statusCode, Type responseType)
    {
        var declared = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == responseType);

        if (!declared)
        {
            throw new InvalidOperationException(
                $"{action.Name} must advertise {responseType.Name} for HTTP {statusCode}.");
        }
    }
}
