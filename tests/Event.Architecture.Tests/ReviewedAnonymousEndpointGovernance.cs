namespace Event.Architecture.Tests;

using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

internal static class ReviewedAnonymousEndpointGovernance
{
    private static readonly ReviewedLifecycleAction[] LocalLifecycleActions =
    [
        new(typeof(LocalEmailVerificationController), nameof(LocalEmailVerificationController.RequestVerification),
            RouteNames.RequestLocalEmailVerification, null,
            "Bounded current-address admission is non-enumerating; proposed addresses require fresh ordinary Local authority and cannot select another account. Native evidence: LocalIdentityLifecycleHttpTests.ProposedAddressRequiresCurrentLocalSessionAndConsumesItsExactPendingAddress."),
        new(typeof(LocalEmailVerificationController), nameof(LocalEmailVerificationController.Consume),
            RouteNames.ConfirmLocalEmail, "consume",
            "Exact native email-purpose token, operation, subject, actor, link and generation authorize one-use consumption and token-authorized mirror retry, never login. Native evidence: LocalIdentityLifecycleHttpTests.VerificationChangesOnlyExactNativeAndDomainBindingWithoutSigningIn."),
        new(typeof(LocalPasswordRecoveryController), nameof(LocalPasswordRecoveryController.RequestRecovery),
            RouteNames.RequestLocalPasswordRecovery, null,
            "Bounded non-enumerating admission resolves only a ready linked Local identity with verified email, without account creation or login. Native evidence: LocalIdentityLifecycleHttpTests.MissingRecoveryTargetIsAcceptedWithoutSessionOrAccountCreation."),
        new(typeof(LocalPasswordRecoveryController), nameof(LocalPasswordRecoveryController.Consume),
            RouteNames.CompleteLocalPasswordRecovery, "consume",
            "Exact recovery-purpose token and current native receipt authorize one password mutation; retries may repair the mirror but cannot mutate the password again or issue a session. Native evidence: LocalIdentityLifecycleHttpTests.RecoveryDeduplicatesDeliveryAndReplayCannotReplacePasswordAgain.")
    ];

    internal static IEnumerable<InventoryEntry> LocalLifecycleExceptions => LocalLifecycleActions.Select(entry =>
        new InventoryEntry($"{entry.Controller.Name}.{entry.Action}", "purpose-owned-local-lifecycle", entry.Reason));

    internal static EndpointMetadataCollection GetMetadata(Type controller, MethodInfo action) =>
        new(controller.GetCustomAttributes(inherit: true).Concat(action.GetCustomAttributes(inherit: true)));

    internal static bool IsLocalLifecycle(Type controller, MethodInfo action) =>
        IsLocalLifecycle(controller, action, GetMetadata(controller, action));

    internal static bool IsLocalLifecycle(Type controller, MethodInfo action, EndpointMetadataCollection metadata)
    {
        var reviewed = LocalLifecycleActions.SingleOrDefault(entry =>
            entry.Controller == controller && controller.GetMethod(entry.Action) == action);
        return reviewed is not null
            && HasSafeguards(metadata, EndpointClass.Public, reviewed.RouteName, reviewed.Template)
            && metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize == 16384;
    }

    internal static bool IsCapabilityCancellation(Type controller, MethodInfo action) =>
        IsCapabilityCancellation(controller, action, GetMetadata(controller, action));

    internal static bool IsCapabilityCancellation(Type controller, MethodInfo action, EndpointMetadataCollection metadata) =>
        controller == typeof(GuestRegistrationStatusController)
        && action == typeof(GuestRegistrationStatusController).GetMethod(nameof(GuestRegistrationStatusController.CancelConfirmed))
        && HasSafeguards(metadata, EndpointClass.PublicTransactional, RouteNames.CancelConfirmedGuestRegistration, "cancellation");

    private static bool HasSafeguards(EndpointMetadataCollection metadata, EndpointClass classification,
        string routeName, string? template)
    {
        var verbs = metadata.OfType<IActionHttpMethodProvider>().ToArray();
        return metadata.GetMetadata<EndpointClassificationAttribute>()?.Class == classification
            && metadata.GetMetadata<IAllowAnonymous>() is not null
            && metadata.GetMetadata<PrivateNoStoreAttribute>() is not null
            && metadata.GetMetadata<SuppressIdempotencyResponseStorageAttribute>() is not null
            && metadata.GetMetadata<RequireIdempotencyKeyAttribute>() is null
            && metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == RateLimitingExtensions.PublicTransactionalPolicy
            && metadata.GetMetadata<DisableRateLimitingAttribute>() is null
            && !PublicTransactionalEndpointGovernance.HasAntiforgeryMetadata(metadata)
            && verbs is [HttpPostAttribute post] && post.Name == routeName && post.Template == template;
    }

    private sealed record ReviewedLifecycleAction(Type Controller, string Action, string RouteName, string? Template, string Reason);
}
