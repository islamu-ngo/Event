// ABOUTME: Architecture tests enforcing that every public HTTP action carries an EndpointClassification.
// ABOUTME: Fails the build when a controller or action is not tagged with a valid endpoint class.

namespace Event.Architecture.Tests;

using System.Linq;
using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;

/// <summary>
/// Enforces that every controller action in Explore.API has an effective
/// <see cref="EndpointClassificationAttribute"/>. An effective attribute is one declared
/// on the action itself OR inherited from the controller type. Missing classifications
/// break OpenAPI vendor-extension emission and downstream client ergonomics.
/// See <c>docs/GOVERNANCE.md#api-contract-rules</c> for policy details.
/// </summary>
public class EndpointClassificationArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(EndpointClassificationAttribute).Assembly;

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

    private static readonly Type[] MutatingHttpVerbAttributes =
    {
        typeof(HttpPostAttribute),
        typeof(HttpPutAttribute),
        typeof(HttpPatchAttribute),
        typeof(HttpDeleteAttribute)
    };

    private static readonly HashSet<string> AnonymousMutatingEndpointExceptions = new(StringComparer.Ordinal)
    {
        "AdmissionTicketRecoveryController.Consume",
        "InstanceOnboardingController.ValidateSecret",
        "AnalyticsRelayController.Relay",
        "EmailUnsubscribeController.Post",
        "IncomingWebhooksController.RecordSvixOperationalCallback",
        "IncomingWebhooksController.RecordStripeConnectCallback",
        "LocalAuthController.Login",
        "LocalAuthController.Register",
        "RegistrationProviderCallbackController.RecordCallback"
    };

    [Test]
    [DisplayName("Every controller must declare an [EndpointClassification] (controller-level or action-level on every action)")]
    public async Task EveryController_DeclaresEndpointClassification_EffectivelyOnEveryAction()
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
            var controllerHas = controller
                .GetCustomAttributes(typeof(EndpointClassificationAttribute), inherit: true)
                .Length > 0;

            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsHttpAction)
                .ToList();

            if (actions.Count == 0)
            {
                continue;
            }

            foreach (var action in actions)
            {
                var actionHas = action
                    .GetCustomAttributes(typeof(EndpointClassificationAttribute), inherit: true)
                    .Length > 0;

                if (!controllerHas && !actionHas)
                {
                    violations.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("every HTTP action must be classified Public/Authenticated/Admin; see docs/GOVERNANCE.md#api-contract-rules");
    }

    [Test]
    [DisplayName("Admin-classified endpoints must require authorization or setup-secret gating")]
    public async Task AdminClassifiedEndpoints_ShouldRequireAuthorizationOrSetupSecret()
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
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsHttpAction)
                .ToList();

            foreach (var action in actions)
            {
                var classification = ResolveEndpointClassification(controller, action);
                if (classification?.Class != EndpointClass.Admin)
                {
                    continue;
                }

                var isSetupSecretGated = HasAttribute<SetupSecretRequiredAttribute>(controller)
                    || HasAttribute<SetupSecretRequiredAttribute>(action);
                var hasAuthorize = HasAttribute<AuthorizeAttribute>(controller)
                    || HasAttribute<AuthorizeAttribute>(action);
                var allowsAnonymous = HasAttribute<AllowAnonymousAttribute>(controller)
                    || HasAttribute<AllowAnonymousAttribute>(action);

                if (!isSetupSecretGated && (!hasAuthorize || allowsAnonymous))
                {
                    violations.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("Admin-classified endpoints must not be anonymously reachable unless explicitly protected by SetupSecretRequired.");
    }

    [Test]
    [DisplayName("PublicTransactional endpoints must satisfy anonymous write, rate-limit, antiforgery, and idempotency governance")]
    public async Task PublicTransactionalEndpoints_ShouldSatisfyGovernance()
    {
        var controllerTypes = Types.InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreNotAbstract()
            .GetTypes()
            .ToList();

        var violations = PublicTransactionalEndpointGovernance.FindViolations(controllerTypes);

        await Assert.That(violations).IsEmpty()
            .Because("PublicTransactional endpoints require governed anonymous unsafe writes, effective rate limiting, no API antiforgery metadata, and POST keys except the exact reviewed no-replay capability cancellation.");
    }

    [Test]
    [DisplayName("Mutating endpoints must require auth metadata or a documented anonymous exception")]
    public async Task MutatingEndpoints_ShouldRequireAuthorizationMetadataOrDocumentedException()
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
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(IsMutatingHttpAction)
                .ToList();

            foreach (var action in actions)
            {
                var actionId = $"{controller.Name}.{action.Name}";
                var isDocumentedException = AnonymousMutatingEndpointExceptions.Contains(actionId)
                    || ReviewedAnonymousEndpointGovernance.IsLocalLifecycle(controller, action);
                var isPublicTransactional = ResolveEndpointClassification(controller, action)?.Class
                    == EndpointClass.PublicTransactional;
                var isSetupSecretGated = HasAttribute<SetupSecretRequiredAttribute>(controller)
                    || HasAttribute<SetupSecretRequiredAttribute>(action);
                var hasAuthorize = HasAttribute<AuthorizeAttribute>(controller)
                    || HasAttribute<AuthorizeAttribute>(action);
                var allowsAnonymous = HasAttribute<AllowAnonymousAttribute>(controller)
                    || HasAttribute<AllowAnonymousAttribute>(action);

                if (!isDocumentedException && !isPublicTransactional && !isSetupSecretGated && (!hasAuthorize || allowsAnonymous))
                {
                    violations.Add(actionId);
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("POST/PUT/PATCH/DELETE endpoints must require authorization metadata unless setup-secret gated or explicitly classified PublicTransactional.");
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

    private static bool IsMutatingHttpAction(MethodInfo method)
    {
        if (method.IsSpecialName)
        {
            return false;
        }

        foreach (var attr in MutatingHttpVerbAttributes)
        {
            if (method.GetCustomAttributes(attr, inherit: true).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static EndpointClassificationAttribute? ResolveEndpointClassification(
        Type controller,
        MethodInfo action)
    {
        return action.GetCustomAttributes<EndpointClassificationAttribute>(inherit: true).FirstOrDefault()
            ?? controller.GetCustomAttributes<EndpointClassificationAttribute>(inherit: true).FirstOrDefault();
    }

    private static bool HasAttribute<TAttribute>(MemberInfo member)
        where TAttribute : Attribute
    {
        return member.GetCustomAttributes<TAttribute>(inherit: true).Any();
    }
}
