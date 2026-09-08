using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Event.Api.IntegrationTests.Features;

public sealed class IncomingWebhookEffectsAdminControllerTests
{
    [Test]
    public async Task StatusAndRedriveRoutes_AreAuthenticatedAndNamedForHal()
    {
        var controllerType = typeof(IncomingWebhookEffectsAdminController);
        var status = controllerType.GetMethod(nameof(IncomingWebhookEffectsAdminController.GetStatus))!;
        var redrive = controllerType.GetMethod(nameof(IncomingWebhookEffectsAdminController.Redrive))!;

        await Assert.That(controllerType.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(status.GetCustomAttribute<HttpGetAttribute>()!.Name)
            .IsEqualTo(RouteNames.GetIncomingWebhookEffectStatus);
        await Assert.That(redrive.GetCustomAttribute<HttpPostAttribute>()!.Name)
            .IsEqualTo(RouteNames.RedriveIncomingWebhookEffect);
        var statusAuthorization = typeof(GetIncomingWebhookEffectStatusQuery)
            .GetCustomAttribute<AuthorizeResourceAttribute>();
        await Assert.That(statusAuthorization).IsNotNull();
        await Assert.That(statusAuthorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(statusAuthorization.Action).IsEqualTo(AuthorizationActions.Webhooks.ViewDelivery);
        var authorization = typeof(RedriveIncomingWebhookEffectCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>();
        await Assert.That(authorization).IsNotNull();
        await Assert.That(authorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Webhooks.RedriveIncoming);
    }

    [Test]
    public async Task StatusLinkPolicy_EmitsRedriveOnlyForDeadLetteredPointers()
    {
        var policy = new IncomingWebhookEffectStatusCollectionLinkPolicy();
        var deadLettered = CreateStatus("DeadLettered");
        var processing = CreateStatus("Processing");

        var eligibleLinks = policy.GetItemLinks(deadLettered, null).ToArray();
        var activeLinks = policy.GetItemLinks(processing, null).ToArray();

        await Assert.That(eligibleLinks).HasSingleItem();
        await Assert.That(eligibleLinks[0].Rel).IsEqualTo("redrive");
        await Assert.That(eligibleLinks[0].RouteName).IsEqualTo(RouteNames.RedriveIncomingWebhookEffect);
        await Assert.That(eligibleLinks[0].PermissionAction)
            .IsEqualTo(AuthorizationActions.Webhooks.RedriveIncoming);
        await Assert.That(activeLinks).IsEmpty();
    }

    private static IncomingWebhookEffectStatusDto CreateStatus(string status) => new()
    {
        EffectOutboxId = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        IncomingWebhookMessageId = Guid.CreateVersion7(),
        EffectKind = "coop.review.decision",
        Status = status,
        ProcessingGeneration = 1
    };
}
