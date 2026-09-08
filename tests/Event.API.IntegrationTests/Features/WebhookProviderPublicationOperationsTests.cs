using System.Reflection;
using System.Security.Claims;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class WebhookProviderPublicationOperationsTests
{
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly Guid _actorUserId = Guid.CreateVersion7();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IResourceAssembler<WebhookProviderPublicationDto, WebhookProviderPublicationDto> _assembler =
        Substitute.For<IResourceAssembler<WebhookProviderPublicationDto, WebhookProviderPublicationDto>>();

    public WebhookProviderPublicationOperationsTests()
    {
        _tenantContext.TenantId.Returns(_tenantId);
    }

    [Test]
    public async Task Routes_UseAnonymousReadsAndAuthorizedCqrsWrites()
    {
        var controller = typeof(WebhookProviderPublicationsController);
        var list = controller.GetMethod(nameof(WebhookProviderPublicationsController.GetPublications))!;
        var detail = controller.GetMethod(nameof(WebhookProviderPublicationsController.GetPublication))!;
        var reconcile = controller.GetMethod(nameof(WebhookProviderPublicationsController.Reconcile))!;
        var abandon = controller.GetMethod(nameof(WebhookProviderPublicationsController.Abandon))!;

        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(list.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(detail.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(list.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Public);
        await Assert.That(detail.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Public);
        await Assert.That(reconcile.GetCustomAttribute<HttpPostAttribute>()?.Name)
            .IsEqualTo(RouteNames.ReconcileWebhookProviderPublication);
        await Assert.That(abandon.GetCustomAttribute<HttpPostAttribute>()?.Name)
            .IsEqualTo(RouteNames.AbandonWebhookProviderPublication);
        await Assert.That(typeof(GetWebhookProviderPublicationsQuery)
            .GetCustomAttribute<AuthorizeResourceAttribute>()?.Action)
            .IsEqualTo(AuthorizationActions.Webhooks.ViewDelivery);
        await Assert.That(typeof(ReconcileWebhookProviderPublicationCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>()?.Action)
            .IsEqualTo(AuthorizationActions.Webhooks.ReconcilePublication);
        await Assert.That(typeof(AbandonWebhookProviderPublicationCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>()?.Action)
            .IsEqualTo(AuthorizationActions.Webhooks.AbandonPublication);
    }

    [Test]
    public async Task PublicationLinks_ExposeOnlyStateAndProviderEligibleActions()
    {
        var policy = new WebhookProviderPublicationDetailLinkPolicy();
        var manual = CreatePublication("MANUAL_RECONCILIATION", "SVIX");
        var deadLettered = CreatePublication("DEAD_LETTERED", "SVIX");
        var publishing = CreatePublication("PUBLISHING", "SVIX");
        var localManual = CreatePublication("MANUAL_RECONCILIATION", "LOCAL");

        var manualLinks = policy.GetLinks(manual, null).ToArray();
        var deadLetteredLinks = policy.GetLinks(deadLettered, null).ToArray();
        var publishingLinks = policy.GetLinks(publishing, null).ToArray();
        var localLinks = policy.GetLinks(localManual, null).ToArray();

        await Assert.That(manualLinks.Any(link => link.Rel == LinkRelations.Reconcile)).IsTrue();
        await Assert.That(manualLinks.Any(link => link.Rel == LinkRelations.Abandon)).IsTrue();
        await Assert.That(deadLetteredLinks.Any(link => link.Rel == LinkRelations.Reconcile)).IsFalse();
        await Assert.That(deadLetteredLinks.Any(link => link.Rel == LinkRelations.Abandon)).IsTrue();
        await Assert.That(publishingLinks.Any(link => link.Rel is LinkRelations.Reconcile or LinkRelations.Abandon)).IsFalse();
        await Assert.That(localLinks.Any(link => link.Rel is LinkRelations.Reconcile or LinkRelations.Abandon)).IsFalse();
    }

    [Test]
    public async Task Reconcile_DispatchesServerOwnedTenantAndActorEvidence()
    {
        var publicationId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<ReconcileWebhookProviderPublicationCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                publicationId,
                "Provider publication reconciled."));
        var controller = CreateController();

        var result = await controller.Reconcile(
            publicationId,
            new ReconcileWebhookProviderPublicationRequestDto
            {
                ExpectedConcurrencyVersion = 7,
                ExternalProviderMessageId = "provider-message-123",
                ReasonCode = "operator.provider-evidence"
            },
            CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await _mediator.Received(1).Send(
            Arg.Is<ReconcileWebhookProviderPublicationCommand>(command =>
                command.TenantId == _tenantId &&
                command.PublicationId == publicationId &&
                command.ActorUserId == _actorUserId &&
                command.ExpectedConcurrencyVersion == 7 &&
                command.ExternalProviderMessageId == "provider-message-123" &&
                command.ReasonCode == "operator.provider-evidence"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Abandon_WhenStateConflicts_ReturnsConflict()
    {
        _mediator.Send(Arg.Any<AbandonWebhookProviderPublicationCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "webhook_provider_publication_not_abandonable",
                "Provider publication cannot be abandoned.",
                ["Provider publication cannot be abandoned."]));
        var controller = CreateController();

        var result = await controller.Abandon(
            Guid.CreateVersion7(),
            new AbandonWebhookProviderPublicationRequestDto
            {
                ExpectedConcurrencyVersion = 4,
                ReasonCode = "operator.abort"
            },
            CancellationToken.None);

        await Assert.That((result.Result as ObjectResult)?.StatusCode)
            .IsEqualTo(StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task ExistingWebhookManagementGets_AreControllerAnonymousAndHandlerAuthorized()
    {
        string[] actionNames =
        [
            nameof(WebhooksController.GetConsumers),
            nameof(WebhooksController.GetConsumer),
            nameof(WebhookEndpointsController.GetEndpoints),
            nameof(WebhookEndpointsController.GetEndpoint),
            nameof(WebhookMessagesController.GetMessages),
            nameof(WebhookMessagesController.GetMessage),
            nameof(WebhookMessagesController.GetDeliveryAttempts),
            nameof(WebhookMessagesController.GetDeliveryAttempt)
        ];

        foreach (var actionName in actionNames)
        {
            var action = WebhookFamilyAction(actionName)!;
            await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
                .IsEqualTo(EndpointClass.Public);
        }
    }

    private WebhookProviderPublicationsController CreateController()
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(_actorUserId);
        userContext.GetRequiredUserId().Returns(_actorUserId);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        services.AddSingleton(userContext);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", _actorUserId.ToString("D"))],
                authenticationType: "TestAuth"))
        };

        return new WebhookProviderPublicationsController(_mediator, _tenantContext, _assembler)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private WebhookProviderPublicationDto CreatePublication(string statusCode, string providerKindCode) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = _tenantId,
            WebhookMessageId = Guid.CreateVersion7(),
            WebhookConsumerId = Guid.CreateVersion7(),
            WebhookDeliveryPlanSnapshotId = Guid.CreateVersion7(),
            ProviderKindId = providerKindCode == "SVIX" ? 2 : 1,
            ProviderKindCode = providerKindCode,
            ProviderKindName = providerKindCode == "SVIX" ? "Svix" : "Local",
            ModeSnapshotId = 3,
            ModeSnapshotCode = "SVIX",
            ModeSnapshotName = "Svix",
            StatusId = 7,
            StatusCode = statusCode,
            StatusName = statusCode,
            ProviderVersion = "svix-1.96.1",
            ProviderEventId = "event-123",
            RequestHash = $"sha256:{new string('a', 64)}",
            ProviderEnvironment = "self-hosted",
            ProviderConfigurationVersion = "provider-v1",
            RetentionPolicyVersion = "retention-v1",
            PreparedAt = DateTime.UtcNow,
            PayloadRetentionUntil = DateTime.UtcNow.AddDays(7),
            PublicationRetentionUntil = DateTime.UtcNow.AddDays(30),
            IdempotencyValidUntil = DateTime.UtcNow.AddHours(12),
            EventContractVersion = 1,
            ConcurrencyVersion = 4
        };

    /// <summary>Finds an action across the controllers the original WebhooksController was partitioned into.</summary>
    private static MethodInfo? WebhookFamilyAction(string actionName) => new[]
    {
        typeof(WebhooksController),
        typeof(WebhookEndpointsController),
        typeof(WebhookMessagesController),
    }.Select(type => type.GetMethod(actionName)).FirstOrDefault(method => method is not null);
}
