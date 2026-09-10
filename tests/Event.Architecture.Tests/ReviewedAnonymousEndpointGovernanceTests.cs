namespace Event.Architecture.Tests;

using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

public sealed class ReviewedAnonymousEndpointGovernanceTests
{
    [Test]
    public async Task LocalLifecycleRecognition_RequiresEveryExactActionAndEffectiveSafeguard()
    {
        (Type Controller, string Action)[] actions =
        [
            (typeof(LocalEmailVerificationController), nameof(LocalEmailVerificationController.RequestVerification)),
            (typeof(LocalEmailVerificationController), nameof(LocalEmailVerificationController.Consume)),
            (typeof(LocalPasswordRecoveryController), nameof(LocalPasswordRecoveryController.RequestRecovery)),
            (typeof(LocalPasswordRecoveryController), nameof(LocalPasswordRecoveryController.Consume))
        ];
        foreach (var (controller, actionName) in actions)
        {
            MethodInfo action = controller.GetMethod(actionName)!;
            EndpointMetadataCollection metadata = ReviewedAnonymousEndpointGovernance.GetMetadata(controller, action);
            await Assert.That(ReviewedAnonymousEndpointGovernance.IsLocalLifecycle(controller, action, metadata)).IsTrue();
            foreach (Type required in RequiredSafeguards.Append(typeof(RequestSizeLimitAttribute)))
            {
                var missing = new EndpointMetadataCollection(metadata.Where(item => item.GetType() != required));
                await Assert.That(ReviewedAnonymousEndpointGovernance.IsLocalLifecycle(controller, action, missing)).IsFalse();
            }
            foreach (object overrideMetadata in InvalidOverrides
                .Append(new RequestSizeLimitAttribute(32768))
                .Append(new DisableRequestSizeLimitAttribute())
                .Append(new EndpointClassificationAttribute(EndpointClass.Authenticated)))
            {
                var overridden = new EndpointMetadataCollection(metadata.Append(overrideMetadata));
                await Assert.That(ReviewedAnonymousEndpointGovernance.IsLocalLifecycle(controller, action, overridden)).IsFalse();
            }
        }
    }

    [Test]
    public async Task CapabilityCancellationRecognition_RequiresExactActionAndEveryEffectiveSafeguard()
    {
        Type controller = typeof(GuestRegistrationStatusController);
        MethodInfo action = controller.GetMethod(nameof(GuestRegistrationStatusController.CancelConfirmed))!;
        EndpointMetadataCollection metadata = ReviewedAnonymousEndpointGovernance.GetMetadata(controller, action);
        await Assert.That(ReviewedAnonymousEndpointGovernance.IsCapabilityCancellation(controller, action, metadata)).IsTrue();
        foreach (Type required in RequiredSafeguards)
        {
            var missing = new EndpointMetadataCollection(metadata.Where(item => item.GetType() != required));
            await Assert.That(ReviewedAnonymousEndpointGovernance.IsCapabilityCancellation(controller, action, missing)).IsFalse();
        }
        foreach (object overrideMetadata in InvalidOverrides)
        {
            var overridden = new EndpointMetadataCollection(metadata.Append(overrideMetadata));
            await Assert.That(ReviewedAnonymousEndpointGovernance.IsCapabilityCancellation(controller, action, overridden)).IsFalse();
        }
        var wrongClass = new EndpointMetadataCollection(metadata.Append(new EndpointClassificationAttribute(EndpointClass.Public)));
        await Assert.That(ReviewedAnonymousEndpointGovernance.IsCapabilityCancellation(controller, action, wrongClass)).IsFalse();
        await Assert.That(ReviewedAnonymousEndpointGovernance.IsCapabilityCancellation(controller,
            controller.GetMethod(nameof(GuestRegistrationStatusController.GetStatus))!, metadata)).IsFalse();
    }

    [Test]
    public async Task Recognition_RejectsUnrelatedControllersEvenWithCopiedNamesRoutesAndSafeguards()
    {
        Type controller = typeof(LocalPasswordRecoveryControllerLookalike);
        MethodInfo action = controller.GetMethod(nameof(LocalPasswordRecoveryControllerLookalike.Consume))!;
        await Assert.That(ReviewedAnonymousEndpointGovernance.IsLocalLifecycle(controller, action)).IsFalse();
        controller = typeof(GuestRegistrationStatusControllerLookalike);
        action = controller.GetMethod(nameof(GuestRegistrationStatusControllerLookalike.CancelConfirmed))!;
        await Assert.That(ReviewedAnonymousEndpointGovernance.IsCapabilityCancellation(controller, action)).IsFalse();
        var violations = PublicTransactionalEndpointGovernance.FindViolations([controller]);
        await Assert.That(violations).Contains("GuestRegistrationStatusControllerLookalike.CancelConfirmed: POST actions must declare [RequireIdempotencyKey].");
    }

    private static readonly Type[] RequiredSafeguards =
    [
        typeof(AllowAnonymousAttribute), typeof(EndpointClassificationAttribute), typeof(PrivateNoStoreAttribute),
        typeof(SuppressIdempotencyResponseStorageAttribute), typeof(EnableRateLimitingAttribute), typeof(HttpPostAttribute)
    ];

    private static readonly object[] InvalidOverrides =
    [
        new DisableRateLimitingAttribute(), new EnableRateLimitingAttribute("global"),
        new RequireIdempotencyKeyAttribute(), new ValidateAntiForgeryTokenAttribute(),
        new HttpGetAttribute(), new HttpPostAttribute("unrelated") { Name = "Unrelated" }
    ];

    [AllowAnonymous, EndpointClassification(EndpointClass.Public), PrivateNoStore,
     EnableRateLimiting("public_transactional"), RequestSizeLimit(16384)]
    private sealed class LocalPasswordRecoveryControllerLookalike : ControllerBase
    {
        [HttpPost("consume", Name = RouteNames.CompleteLocalPasswordRecovery), SuppressIdempotencyResponseStorage]
        public NoContentResult Consume() => NoContent();
    }

    [AllowAnonymous, EndpointClassification(EndpointClass.PublicTransactional), PrivateNoStore,
     EnableRateLimiting("public_transactional")]
    private sealed class GuestRegistrationStatusControllerLookalike : ControllerBase
    {
        [HttpPost("cancellation", Name = RouteNames.CancelConfirmedGuestRegistration), SuppressIdempotencyResponseStorage]
        public NoContentResult CancelConfirmed() => NoContent();
    }
}
