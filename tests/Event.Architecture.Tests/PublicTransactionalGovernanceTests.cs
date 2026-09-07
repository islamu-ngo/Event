namespace Event.Architecture.Tests;

using System.Reflection;
using Explore.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

public class PublicTransactionalGovernanceTests
{
    [Test]
    public async Task EndpointClassification_ExposesPublicTransactionalAndRequiredKeyMarker()
    {
        var classification = Enum.GetNames<EndpointClass>()
            .SingleOrDefault(name => name == "PublicTransactional");
        var requiredKeyAttribute = typeof(EndpointClass).Assembly.GetType(
            "Explore.API.Attributes.RequireIdempotencyKeyAttribute");

        await Assert.That(classification).IsEqualTo("PublicTransactional");
        await Assert.That(requiredKeyAttribute).IsNotNull();
    }

    [Test]
    public async Task PublicTransactionalRules_AcceptCompliantSyntheticPost()
    {
        var violations = PublicTransactionalEndpointGovernance.FindViolations(
            new[] { typeof(CompliantPublicTransactionalController) });

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task PublicTransactionalRules_RejectEveryMissingRequiredInvariant()
    {
        var violations = PublicTransactionalEndpointGovernance.FindViolations(
            new[] { typeof(NonCompliantPublicTransactionalController) });

        await Assert.That(violations).Contains("NonCompliantPublicTransactionalController.Get: must use only unsafe HTTP verbs.");
        await Assert.That(violations).Contains("NonCompliantPublicTransactionalController.PostWithoutKey: POST actions must declare [RequireIdempotencyKey].");
        await Assert.That(violations).Contains("NonCompliantPublicTransactionalController.PostWithWrongPolicy: must use [EnableRateLimiting(\"public_transactional\")].");
        await Assert.That(violations).Contains("NonCompliantPublicTransactionalController.PostWithoutAnonymous: must be effectively [AllowAnonymous].");
        await Assert.That(violations).Contains("NonCompliantPublicTransactionalController.PostWithAntiforgery: must not declare API antiforgery metadata.");
    }

    [Test]
    public async Task PublicTransactionalRules_RejectAcceptVerbsPostWithoutIdempotencyKey()
    {
        var violations = PublicTransactionalEndpointGovernance.FindViolations(
            new[] { typeof(AcceptVerbsPublicTransactionalController) });

        await Assert.That(violations).Contains("AcceptVerbsPublicTransactionalController.PostWithoutKey: POST actions must declare [RequireIdempotencyKey].");
    }

    [Test]
    public async Task PublicTransactionalRules_RejectEffectiveRateLimitingDisableMetadata()
    {
        var violations = PublicTransactionalEndpointGovernance.FindViolations(
            new[] { typeof(DisabledRateLimitPublicTransactionalController) });

        await Assert.That(violations).Contains("DisabledRateLimitPublicTransactionalController.Post: must not disable rate limiting.");
    }

    [Test]
    public async Task PublicTransactionalRules_RejectInheritedNoncompliantAction()
    {
        var violations = PublicTransactionalEndpointGovernance.FindViolations(
            new[] { typeof(InheritedActionPublicTransactionalController) });

        await Assert.That(violations).Contains("InheritedActionPublicTransactionalController.PostWithoutKey: POST actions must declare [RequireIdempotencyKey].");
    }

    [EndpointClassification(EndpointClass.PublicTransactional)]
    private sealed class CompliantPublicTransactionalController : ControllerBase
    {
        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("public_transactional")]
        [RequireIdempotencyKey]
        public OkResult Post() => Ok();
    }

    [EndpointClassification(EndpointClass.PublicTransactional)]
    private sealed class NonCompliantPublicTransactionalController : ControllerBase
    {
        [HttpGet]
        [AllowAnonymous]
        [EnableRateLimiting("public_transactional")]
        public OkResult Get() => Ok();

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("public_transactional")]
        public OkResult PostWithoutKey() => Ok();

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("global")]
        [RequireIdempotencyKey]
        public OkResult PostWithWrongPolicy() => Ok();

        [HttpPost]
        [EnableRateLimiting("public_transactional")]
        [RequireIdempotencyKey]
        public OkResult PostWithoutAnonymous() => Ok();

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("public_transactional")]
        [RequireIdempotencyKey]
        [ValidateAntiForgeryToken]
        public OkResult PostWithAntiforgery() => Ok();
    }

    [EndpointClassification(EndpointClass.PublicTransactional)]
    private sealed class AcceptVerbsPublicTransactionalController : ControllerBase
    {
        [AcceptVerbs("POST")]
        [AllowAnonymous]
        [EnableRateLimiting("public_transactional")]
        public OkResult PostWithoutKey() => Ok();
    }

    [EndpointClassification(EndpointClass.PublicTransactional)]
    [AllowAnonymous]
    [EnableRateLimiting("public_transactional")]
    private sealed class DisabledRateLimitPublicTransactionalController : ControllerBase
    {
        [HttpPost]
        [RequireIdempotencyKey]
        [DisableRateLimiting]
        public OkResult Post() => Ok();
    }

    private abstract class InheritedActionControllerBase : ControllerBase
    {
        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("public_transactional")]
        public OkResult PostWithoutKey() => Ok();
    }

    [EndpointClassification(EndpointClass.PublicTransactional)]
    private sealed class InheritedActionPublicTransactionalController : InheritedActionControllerBase;
}

internal static class PublicTransactionalEndpointGovernance
{
    private const string PublicTransactionalRateLimitPolicy = "public_transactional";

    private static readonly HashSet<string> UnsafeHttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private static readonly HashSet<string> AntiforgeryMetadataNames = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata",
        "Microsoft.AspNetCore.Mvc.ViewFeatures.IAntiforgeryPolicy",
        "Microsoft.AspNetCore.Antiforgery.DisableAntiforgeryAttribute",
        "Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute",
        "Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute",
        "Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute"
    };

    public static IReadOnlyList<string> FindViolations(IEnumerable<Type> controllerTypes)
    {
        var violations = new List<string>();

        foreach (var controller in controllerTypes)
        {
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(IsHttpAction);

            foreach (var action in actions)
            {
                if (ResolveAttribute<EndpointClassificationAttribute>(controller, action)?.Class
                    != EndpointClass.PublicTransactional)
                {
                    continue;
                }

                var actionId = $"{controller.Name}.{action.Name}";

                if (!HasEffectiveAttribute<AllowAnonymousAttribute>(controller, action))
                {
                    violations.Add($"{actionId}: must be effectively [AllowAnonymous].");
                }

                if (!UsesOnlyUnsafeHttpVerbs(action))
                {
                    violations.Add($"{actionId}: must use only unsafe HTTP verbs.");
                }

                if (ResolveAttribute<EnableRateLimitingAttribute>(controller, action)?.PolicyName
                    != PublicTransactionalRateLimitPolicy)
                {
                    violations.Add($"{actionId}: must use [EnableRateLimiting(\"public_transactional\")].");
                }

                if (HasEffectiveAttribute<DisableRateLimitingAttribute>(controller, action))
                {
                    violations.Add($"{actionId}: must not disable rate limiting.");
                }

                if (HasEffectiveAntiforgeryMetadata(controller, action))
                {
                    violations.Add($"{actionId}: must not declare API antiforgery metadata.");
                }

                if (UsesHttpMethod(action, HttpMethods.Post)
                    && !HasEffectiveAttribute<RequireIdempotencyKeyAttribute>(controller, action))
                {
                    violations.Add($"{actionId}: POST actions must declare [RequireIdempotencyKey].");
                }
            }
        }

        return violations;
    }

    private static bool IsHttpAction(MethodInfo method) =>
        !method.IsSpecialName
        && method.DeclaringType != typeof(object)
        && !HasAttribute<NonActionAttribute>(method)
        && GetEffectiveHttpMethods(method).Length > 0;

    private static bool UsesOnlyUnsafeHttpVerbs(MethodInfo method)
    {
        var httpMethods = GetEffectiveHttpMethods(method);

        return httpMethods.Length > 0 && httpMethods.All(UnsafeHttpMethods.Contains);
    }

    private static bool UsesHttpMethod(MethodInfo method, string httpMethod) =>
        GetEffectiveHttpMethods(method).Contains(httpMethod, StringComparer.OrdinalIgnoreCase);

    private static string[] GetEffectiveHttpMethods(MethodInfo method) =>
        method.GetCustomAttributes(inherit: true)
            .OfType<IActionHttpMethodProvider>()
            .SelectMany(provider => provider.HttpMethods)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HasEffectiveAntiforgeryMetadata(Type controller, MethodInfo action) =>
        HasAntiforgeryMetadata(controller) || HasAntiforgeryMetadata(action);

    private static bool HasAntiforgeryMetadata(MemberInfo member) =>
        member.GetCustomAttributes(inherit: true).Any(attribute =>
            AntiforgeryMetadataNames.Contains(attribute.GetType().FullName ?? string.Empty)
            || attribute.GetType().GetInterfaces().Any(interfaceType =>
                AntiforgeryMetadataNames.Contains(interfaceType.FullName ?? string.Empty)));

    private static bool HasEffectiveAttribute<TAttribute>(Type controller, MethodInfo action)
        where TAttribute : Attribute =>
        HasAttribute<TAttribute>(controller) || HasAttribute<TAttribute>(action);

    private static TAttribute? ResolveAttribute<TAttribute>(Type controller, MethodInfo action)
        where TAttribute : Attribute =>
        action.GetCustomAttributes<TAttribute>(inherit: true).FirstOrDefault()
        ?? controller.GetCustomAttributes<TAttribute>(inherit: true).FirstOrDefault();

    private static bool HasAttribute<TAttribute>(MemberInfo member)
        where TAttribute : Attribute =>
        member.GetCustomAttributes<TAttribute>(inherit: true).Any();

}
