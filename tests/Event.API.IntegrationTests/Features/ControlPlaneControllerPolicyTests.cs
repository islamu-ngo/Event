using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class ControlPlaneControllerPolicyTests
{
    [Test]
    public async Task PublicActions_UseDedicatedControlPlaneRateLimitAndTimeoutPolicies()
    {
        foreach (var method in PublicActionMethods())
        {
            var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>();
            var timeout = method.GetCustomAttribute<RequestTimeoutAttribute>();

            await Assert.That(rateLimit).IsNotNull();
            await Assert.That(rateLimit!.PolicyName).IsEqualTo(RateLimitingExtensions.ControlPlanePolicy);
            await Assert.That(timeout).IsNotNull();
            await Assert.That(timeout!.PolicyName).IsEqualTo(RequestTimeoutExtensions.ControlPlanePolicy);
        }
    }

    [Test]
    public async Task TenantLifecycleActions_DeclareConflictProblemDetails()
    {
        string[] actionNames =
        [
            nameof(ControlPlaneTenantLifecycleController.ActivateTenant),
            nameof(ControlPlaneTenantLifecycleController.SuspendTenant),
            nameof(ControlPlaneTenantLifecycleController.ArchiveTenant),
            nameof(ControlPlaneTenantLifecycleController.ReactivateTenant),
            nameof(ControlPlaneTenantLifecycleController.ScheduleTenantPurge)
        ];

        foreach (var actionName in actionNames)
        {
            var action = ControlPlaneFamilyAction(actionName);
            var conflict = action?.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .SingleOrDefault(attribute => attribute.StatusCode == StatusCodes.Status409Conflict);

            await Assert.That(conflict).IsNotNull();
            await Assert.That(conflict!.Type).IsEqualTo(typeof(ProblemDetails));
        }
    }

    private static IEnumerable<MethodInfo> PublicActionMethods()
    {
        return ControlPlaneFamily
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName);
    }

    /// <summary>The controllers the original ControlPlaneController was partitioned into.</summary>
    private static readonly Type[] ControlPlaneFamily =
    [
        typeof(ControlPlaneController),
        typeof(ControlPlaneTenantPlanController),
        typeof(ControlPlaneTenantConfigurationController),
        typeof(ControlPlaneTenantLifecycleController),
    ];

    private static MethodInfo? ControlPlaneFamilyAction(string actionName) =>
        ControlPlaneFamily.Select(type => type.GetMethod(actionName)).FirstOrDefault(method => method is not null);
}
