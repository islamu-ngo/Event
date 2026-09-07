using System.Reflection;
using System.Security.Claims;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class WebhooksControllerTests
{
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IWebhookOwnershipScopeResolver _ownershipScopeResolver =
        Substitute.For<IWebhookOwnershipScopeResolver>();
    private readonly IResourceAssembler<WebhookConsumerDto, WebhookConsumerDto> _consumerAssembler =
        Substitute.For<IResourceAssembler<WebhookConsumerDto, WebhookConsumerDto>>();
    private readonly IResourceAssembler<WebhookEndpointDto, WebhookEndpointDto> _endpointAssembler =
        Substitute.For<IResourceAssembler<WebhookEndpointDto, WebhookEndpointDto>>();
    private readonly IResourceAssembler<WebhookMessageDto, WebhookMessageDto> _messageAssembler =
        Substitute.For<IResourceAssembler<WebhookMessageDto, WebhookMessageDto>>();
    private readonly IResourceAssembler<WebhookDeliveryAttemptDto, WebhookDeliveryAttemptDto> _attemptAssembler =
        Substitute.For<IResourceAssembler<WebhookDeliveryAttemptDto, WebhookDeliveryAttemptDto>>();

    public WebhooksControllerTests()
    {
        _tenantContext.TenantId.Returns(_tenantId);
        _ownershipScopeResolver.ResolveAsync(
                (int)WebhookConsumerKind.Tenant,
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(WebhookOwnershipScopeResolution.Resolved(WebhookOwnershipScope.Create(
                WebhookConsumerKind.Tenant,
                _tenantId,
                null,
                null,
                null,
                null)));
    }

    [Test]
    public async Task GetEventTypesRoute_UsesStablePublicLookupMetadata()
    {
        var controllerType = typeof(WebhooksController);
        var action = controllerType.GetMethod(nameof(WebhooksController.GetEventTypes))!;
        var route = action.GetCustomAttribute<HttpGetAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo("event-types");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetWebhookEventTypes);
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Public);
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("LookupData");
    }

    [Test]
    public async Task GetEventTypes_DispatchesCatalogQuery()
    {
        IReadOnlyList<WebhookEventTypeDto> response =
        [
            new()
            {
                Name = "event.published",
                GroupName = "event",
                Description = "Raised when an event becomes publicly published.",
                SchemaVersion = 1,
                IsPublic = true,
                IsEnabled = true,
                PayloadRetentionDays = 14,
                SchemaJson = """{"type":"object"}""",
                ExamplePayloadJson = """{"type":"event.published"}""",
                DataFields =
                [
                    new WebhookEventDataFieldDto
                    {
                        Name = "eventId",
                        JsonType = "string",
                        Description = "Published event identifier.",
                        ExampleJson = "\"018f0000-0000-7000-8000-000000000001\"",
                        Required = true
                    }
                ]
            }
        ];
        _mediator.Send(Arg.Any<GetWebhookEventTypesQuery>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController<WebhooksController>("keycloak-subject-event-types");

        var result = await controller.GetEventTypes(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(response);
        await _mediator.Received(1).Send(Arg.Any<GetWebhookEventTypesQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OpenSvixAppPortalRoute_UsesStableMetadataAndWebhookAuthorizationAction()
    {
        var controllerType = typeof(WebhooksController);
        var action = controllerType.GetMethod(nameof(WebhooksController.OpenSvixAppPortal))!;
        var route = action.GetCustomAttribute<HttpPostAttribute>();
        var authorization = typeof(OpenSvixAppPortalCommand).GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(controllerType.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controllerType.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(controllerType.GetCustomAttribute<RouteAttribute>()?.Template).IsEqualTo("api/webhooks");
        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo("svix/app-portal");
        await Assert.That(route.Name).IsEqualTo(RouteNames.OpenSvixAppPortal);
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(action.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName).IsEqualTo(RequestTimeoutExtensions.ComplexPolicy);
        await Assert.That(authorization).IsNotNull();
        await Assert.That(authorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Webhooks.OpenProviderPortal);
    }

    [Test]
    public async Task RepairProviderBindingRoute_UsesStableWriteAndManageProviderAuthorization()
    {
        var action = typeof(WebhooksController).GetMethod(nameof(WebhooksController.RepairProviderBinding))!;
        var route = action.GetCustomAttribute<HttpPostAttribute>();
        var authorization = typeof(RepairWebhookProviderBindingCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template)
            .IsEqualTo("consumers/{consumerId:guid}/provider-binding/repair");
        await Assert.That(route.Name).IsEqualTo(RouteNames.RepairWebhookProviderBinding);
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(action.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName)
            .IsEqualTo(RequestTimeoutExtensions.ComplexPolicy);
        await Assert.That(authorization).IsNotNull();
        await Assert.That(authorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Webhooks.ManageProvider);
    }

    [Test]
    public async Task RepairProviderBinding_DispatchesServerTenantAndConsumerIdentity()
    {
        var consumerId = Guid.CreateVersion7();
        _mediator.Send(
                Arg.Any<RepairWebhookProviderBindingCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                Guid.CreateVersion7(),
                "Webhook provider binding verified."));
        var controller = CreateController<WebhooksController>("keycloak-subject-provider-binding-repair");
        var request = new RepairWebhookProviderBindingRequestDto
        {
            ExternalApplicationId = "app_repaired",
            ReasonCode = "provider.application-recreated"
        };

        var result = await controller.RepairProviderBinding(
            consumerId,
            request,
            CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await _mediator.Received(1).Send(
            Arg.Is<RepairWebhookProviderBindingCommand>(command =>
                command.ConsumerId == consumerId &&
                command.ExternalApplicationId == request.ExternalApplicationId &&
                command.ReasonCode == request.ReasonCode),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConsumerRoutes_UseStableMetadataAndWebhookAuthorizationActions()
    {
        var controllerType = typeof(WebhooksController);
        var listAction = controllerType.GetMethod(nameof(WebhooksController.GetConsumers))!;
        var detailAction = controllerType.GetMethod(nameof(WebhooksController.GetConsumer))!;
        var createAction = controllerType.GetMethod(nameof(WebhooksController.CreateConsumer))!;
        var listRoute = listAction.GetCustomAttribute<HttpGetAttribute>();
        var detailRoute = detailAction.GetCustomAttribute<HttpGetAttribute>();
        var createRoute = createAction.GetCustomAttribute<HttpPostAttribute>();
        var listAuthorization = typeof(GetWebhookConsumersQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var detailAuthorization = typeof(GetWebhookConsumerByIdQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var createAuthorization = typeof(CreateWebhookConsumerCommand).GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(listRoute).IsNotNull();
        await Assert.That(listRoute!.Template).IsEqualTo("consumers");
        await Assert.That(listRoute.Name).IsEqualTo(RouteNames.GetWebhookConsumers);
        await Assert.That(detailRoute).IsNotNull();
        await Assert.That(detailRoute!.Template).IsEqualTo("consumers/{consumerId:guid}");
        await Assert.That(detailRoute.Name).IsEqualTo(RouteNames.GetWebhookConsumerById);
        await Assert.That(createRoute).IsNotNull();
        await Assert.That(createRoute!.Template).IsEqualTo("consumers");
        await Assert.That(createRoute.Name).IsEqualTo(RouteNames.CreateWebhookConsumer);
        await Assert.That(listAction.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("ListData");
        await Assert.That(createAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(createAction.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName).IsEqualTo(RequestTimeoutExtensions.ComplexPolicy);
        await Assert.That(listAuthorization).IsNotNull();
        await Assert.That(listAuthorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(listAuthorization.Action).IsEqualTo(AuthorizationActions.Webhooks.View);
        await Assert.That(detailAuthorization).IsNotNull();
        await Assert.That(detailAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.View);
        await Assert.That(createAuthorization).IsNotNull();
        await Assert.That(createAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.Create);
    }

    [Test]
    [Arguments("Svix", "Svix")]
    [Arguments("Composite", "Composite")]
    public async Task ConsumerDetailLinks_BeforePersistedPortalAuthority_HideOpenPortalAffordance(
        string consumerProviderMode,
        string configuredProviderMode)
    {
        var consumer = CreateConsumerDto(consumerProviderMode);

        var links = CreateConsumerDetailLinkPolicy(configuredProviderMode, appPortalEnabled: true)
            .GetLinks(consumer, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.OpenProviderPortal)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
    }

    [Test]
    [Arguments("Svix", "Svix", false)]
    [Arguments("Svix", "Local", true)]
    [Arguments("Local", "Svix", true)]
    public async Task ConsumerDetailLinks_WhenSvixPortalUnavailable_HideOpenPortalAffordance(
        string consumerProviderMode,
        string configuredProviderMode,
        bool appPortalEnabled)
    {
        var consumer = CreateConsumerDto(consumerProviderMode);

        var links = CreateConsumerDetailLinkPolicy(configuredProviderMode, appPortalEnabled)
            .GetLinks(consumer, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.OpenProviderPortal)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
    }

    [Test]
    [Arguments(WebhookConsumerKind.Instance)]
    [Arguments(WebhookConsumerKind.Tenant)]
    [Arguments(WebhookConsumerKind.Organization)]
    [Arguments(WebhookConsumerKind.Group)]
    [Arguments(WebhookConsumerKind.User)]
    public async Task WebhookCreateCollectionLinks_UseCanonicalTypedOwnerAuthorization(
        WebhookConsumerKind ownerKind)
    {
        var ownerId = ownerKind == WebhookConsumerKind.Tenant
            ? _tenantId
            : Guid.CreateVersion7();
        var ownership = CreateOwnershipScope(ownerKind, ownerId);
        var context = Substitute.For<ICollectionAuthorizationContext>();
        context.AuthorizationResourceId.Returns(ownerId.ToString("D"));
        context.AuthorizationFacts.Returns(WebhookOwnershipAuthorizationFacts.From(ownership));

        LinkDefinition[] links =
        [
            new WebhookConsumerCollectionLinkPolicy(new WebhookConsumerDetailLinkPolicy())
                .GetCollectionLinks(null, context)
                .Single(),
            new WebhookEndpointCollectionLinkPolicy()
                .GetCollectionLinks(null, context)
                .Single()
        ];

        foreach (var link in links)
        {
            await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.Create);
            await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.Webhook);
            await Assert.That(link.PermissionResourceId).IsEqualTo(ownerId.ToString("D"));
            await Assert.That(link.PermissionFacts).IsTypeOf<WebhookOwnershipAuthorizationFacts>();
            var facts = (WebhookOwnershipAuthorizationFacts)link.PermissionFacts!;
            await Assert.That(facts.Kind).IsEqualTo(ownerKind);
            await Assert.That(facts.OwnerId).IsEqualTo(ownerId);
            // Instance-owned webhooks carry no tenant: publishing one would invent a scope the owner
            // does not have.
            await Assert.That(facts.TenantId is not null).IsEqualTo(ownerKind != WebhookConsumerKind.Instance);
        }
    }

    [Test]
    [Arguments(WebhookConsumerKind.Instance, false)]
    [Arguments(WebhookConsumerKind.Tenant, true)]
    [Arguments(WebhookConsumerKind.Organization, false)]
    [Arguments(WebhookConsumerKind.Group, false)]
    [Arguments(WebhookConsumerKind.User, false)]
    public async Task WebhookMessageCollectionLinks_ExposeTenantOperationsOnlyAtApprovedScope(
        WebhookConsumerKind ownerKind,
        bool expectsTenantOperations)
    {
        var ownerId = ownerKind == WebhookConsumerKind.Tenant
            ? _tenantId
            : Guid.CreateVersion7();
        var ownership = CreateOwnershipScope(ownerKind, ownerId);
        var context = Substitute.For<ICollectionAuthorizationContext>();
        context.AuthorizationResourceId.Returns(ownerId.ToString("D"));
        context.AuthorizationFacts.Returns(WebhookOwnershipAuthorizationFacts.From(ownership));

        var links = new WebhookMessageCollectionLinkPolicy(TimeProvider.System)
            .GetCollectionLinks(null, context)
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.ProviderPublications))
            .IsEqualTo(expectsTenantOperations);
        await Assert.That(links.Any(link => link.Rel == LinkRelations.BulkReplayPreview))
            .IsEqualTo(expectsTenantOperations);
        await Assert.That(links.Any(link => link.Rel == LinkRelations.BulkReplays))
            .IsEqualTo(expectsTenantOperations);
        await Assert.That(links.All(link => link.PermissionResourceId == ownerId.ToString("D"))).IsTrue();
        await Assert.That(links.All(link =>
                link.PermissionFacts is WebhookOwnershipAuthorizationFacts facts && facts.Kind == ownerKind))
            .IsTrue();
    }

    [Test]
    public async Task EndpointRoutes_UseStableMetadataAndWebhookAuthorizationActions()
    {
        var controllerType = typeof(WebhookEndpointsController);
        var listAction = controllerType.GetMethod(nameof(WebhookEndpointsController.GetEndpoints))!;
        var detailAction = controllerType.GetMethod(nameof(WebhookEndpointsController.GetEndpoint))!;
        var createAction = controllerType.GetMethod(nameof(WebhookEndpointsController.CreateEndpoint))!;
        var updateAction = controllerType.GetMethod(nameof(WebhookEndpointsController.UpdateEndpoint))!;
        var deleteAction = controllerType.GetMethod(nameof(WebhookEndpointsController.DeleteEndpoint))!;
        var rotateAction = controllerType.GetMethod(nameof(WebhookEndpointsController.RotateEndpointSecret))!;
        var testAction = controllerType.GetMethod(nameof(WebhookEndpointsController.TestEndpoint))!;
        var listRoute = listAction.GetCustomAttribute<HttpGetAttribute>();
        var detailRoute = detailAction.GetCustomAttribute<HttpGetAttribute>();
        var createRoute = createAction.GetCustomAttribute<HttpPostAttribute>();
        var updateRoute = updateAction.GetCustomAttribute<HttpPatchAttribute>();
        var deleteRoute = deleteAction.GetCustomAttribute<HttpDeleteAttribute>();
        var rotateRoute = rotateAction.GetCustomAttribute<HttpPostAttribute>();
        var testRoute = testAction.GetCustomAttribute<HttpPostAttribute>();
        var listAuthorization = typeof(GetWebhookEndpointsQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var detailAuthorization = typeof(GetWebhookEndpointByIdQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var createAuthorization = typeof(CreateWebhookEndpointCommand).GetCustomAttribute<AuthorizeResourceAttribute>();
        var updateAuthorization = typeof(UpdateWebhookEndpointCommand).GetCustomAttribute<AuthorizeResourceAttribute>();
        var archiveAuthorization = typeof(ArchiveWebhookEndpointCommand).GetCustomAttribute<AuthorizeResourceAttribute>();
        var rotateAuthorization = typeof(RotateWebhookEndpointSecretCommand).GetCustomAttribute<AuthorizeResourceAttribute>();
        var testAuthorization = typeof(TestWebhookEndpointCommand).GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(listRoute).IsNotNull();
        await Assert.That(listRoute!.Template).IsEqualTo("endpoints");
        await Assert.That(listRoute.Name).IsEqualTo(RouteNames.GetWebhookEndpoints);
        await Assert.That(detailRoute).IsNotNull();
        await Assert.That(detailRoute!.Template).IsEqualTo("endpoints/{endpointId:guid}");
        await Assert.That(detailRoute.Name).IsEqualTo(RouteNames.GetWebhookEndpointById);
        await Assert.That(createRoute).IsNotNull();
        await Assert.That(createRoute!.Template).IsEqualTo("endpoints");
        await Assert.That(createRoute.Name).IsEqualTo(RouteNames.CreateWebhookEndpoint);
        await Assert.That(updateRoute).IsNotNull();
        await Assert.That(updateRoute!.Template).IsEqualTo("endpoints/{endpointId:guid}");
        await Assert.That(updateRoute.Name).IsEqualTo(RouteNames.UpdateWebhookEndpoint);
        await Assert.That(deleteRoute).IsNotNull();
        await Assert.That(deleteRoute!.Template).IsEqualTo("endpoints/{endpointId:guid}");
        await Assert.That(deleteRoute.Name).IsEqualTo(RouteNames.DeleteWebhookEndpoint);
        await Assert.That(rotateRoute).IsNotNull();
        await Assert.That(rotateRoute!.Template).IsEqualTo("endpoints/{endpointId:guid}/rotate-secret");
        await Assert.That(rotateRoute.Name).IsEqualTo(RouteNames.RotateWebhookEndpointSecret);
        await Assert.That(testRoute).IsNotNull();
        await Assert.That(testRoute!.Template).IsEqualTo("endpoints/{endpointId:guid}/test");
        await Assert.That(testRoute.Name).IsEqualTo(RouteNames.TestWebhookEndpoint);
        await Assert.That(listAction.GetCustomAttribute<OutputCacheAttribute>()?.PolicyName).IsEqualTo("ListData");
        await Assert.That(createAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(createAction.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName).IsEqualTo(RequestTimeoutExtensions.ComplexPolicy);
        await Assert.That(updateAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(updateAction.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName).IsEqualTo(RequestTimeoutExtensions.ComplexPolicy);
        await Assert.That(deleteAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(deleteAction.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName).IsEqualTo(RequestTimeoutExtensions.DefaultPolicy);
        await Assert.That(rotateAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(rotateAction.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName).IsEqualTo(RequestTimeoutExtensions.DefaultPolicy);
        await Assert.That(testAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(testAction.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName).IsEqualTo(RequestTimeoutExtensions.DefaultPolicy);
        await Assert.That(listAuthorization).IsNotNull();
        await Assert.That(listAuthorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(listAuthorization.Action).IsEqualTo(AuthorizationActions.Webhooks.View);
        await Assert.That(detailAuthorization).IsNotNull();
        await Assert.That(detailAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.View);
        await Assert.That(createAuthorization).IsNotNull();
        await Assert.That(createAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.Create);
        await Assert.That(updateAuthorization).IsNotNull();
        await Assert.That(updateAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.Update);
        await Assert.That(archiveAuthorization).IsNotNull();
        await Assert.That(archiveAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.Delete);
        await Assert.That(rotateAuthorization).IsNotNull();
        await Assert.That(rotateAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.RotateSecret);
        await Assert.That(testAuthorization).IsNotNull();
        await Assert.That(testAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.Test);
    }

    [Test]
    public async Task ConsumerProviderModeRoute_UsesStableMetadataAndWebhookUpdateAuthorization()
    {
        var action = typeof(WebhooksController)
            .GetMethod(nameof(WebhooksController.UpdateConsumerProviderMode))!;
        var route = action.GetCustomAttribute<HttpPatchAttribute>();
        var authorization = typeof(UpdateWebhookConsumerProviderModeCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo("consumers/{consumerId:guid}/provider-mode");
        await Assert.That(route.Name).IsEqualTo(RouteNames.UpdateWebhookConsumerProviderMode);
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(action.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName)
            .IsEqualTo(RequestTimeoutExtensions.ComplexPolicy);
        await Assert.That(authorization).IsNotNull();
        await Assert.That(authorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Webhooks.Update);
    }

    [Test]
    public async Task GetConsumers_DispatchesTenantScopedQueryAndReturnsHalCollection()
    {
        var consumer = CreateConsumerDto();
        IReadOnlyList<WebhookConsumerDto> consumers = [consumer];
        var halCollection = new HalCollectionResource<WebhookConsumerDto>
        {
            TotalCount = 1,
            Embedded = new HalCollectionEmbedded<WebhookConsumerDto>
            {
                Items = [new HalResource<WebhookConsumerDto>(consumer)]
            }
        };
        _mediator.Send(Arg.Any<GetWebhookConsumersQuery>(), Arg.Any<CancellationToken>())
            .Returns(consumers);
        _consumerAssembler.ToCollectionResource(
                consumers,
                RouteNames.GetWebhookConsumers,
                Arg.Any<object?>(),
                Arg.Any<HttpContext>())
            .Returns(halCollection);
        var controller = CreateController<WebhooksController>("keycloak-subject-consumers");

        var result = await controller.GetConsumers(
            (int)WebhookConsumerKind.Tenant,
            _tenantId,
            25,
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halCollection);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookConsumersQuery>(query =>
                query.OwnerKindId == (int)WebhookConsumerKind.Tenant &&
                query.OwnerId == _tenantId &&
                query.Limit == 25),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetConsumer_WhenFound_ReturnsHalResource()
    {
        var consumer = CreateConsumerDto();
        var halResource = new HalResource<WebhookConsumerDto>(consumer);
        _mediator.Send(Arg.Any<GetWebhookConsumerByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(consumer);
        _consumerAssembler.ToResource(consumer, Arg.Any<HttpContext>())
            .Returns(halResource);
        var controller = CreateController<WebhooksController>("keycloak-subject-consumer-detail");

        var result = await controller.GetConsumer(consumer.Id, CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halResource);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookConsumerByIdQuery>(query =>
                query.ConsumerId == consumer.Id),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetConsumer_WhenMissing_ReturnsNotFoundProblem()
    {
        _mediator.Send(Arg.Any<GetWebhookConsumerByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns((WebhookConsumerDto?)null);
        var controller = CreateController<WebhooksController>("keycloak-subject-consumer-missing");

        var result = await controller.GetConsumer(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_consumer_not_found");
    }

    [Test]
    public async Task CreateConsumer_DispatchesTenantScopedCommandAndReturnsCreatedRoute()
    {
        var consumerId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<CreateWebhookConsumerCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                consumerId,
                "Webhook consumer created."));
        var controller = CreateController<WebhooksController>("keycloak-subject-create-consumer");

        var result = await controller.CreateConsumer(
            new CreateWebhookConsumerRequestDto
            {
                OwnerId = _tenantId,
                ConsumerKindId = 1,
                Name = "Tenant automation",
                ProviderModeId = 2
            },
            CancellationToken.None);

        var created = result.Result as CreatedAtRouteResult;
        await Assert.That(created).IsNotNull();
        await Assert.That(created!.RouteName).IsEqualTo(RouteNames.GetWebhookConsumerById);
        await Assert.That(created.RouteValues!["consumerId"]).IsEqualTo(consumerId);
        await _mediator.Received(1).Send(
            Arg.Is<CreateWebhookConsumerCommand>(command =>
                command.OwnerId == _tenantId &&
                command.ConsumerKindId == 1 &&
                command.Name == "Tenant automation" &&
                command.ProviderModeId == 2),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateConsumer_WhenNameConflicts_ReturnsConflictProblem()
    {
        _mediator.Send(Arg.Any<CreateWebhookConsumerCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "webhook_consumer_name_conflict",
                "Webhook consumer name is already in use.",
                ["A webhook consumer with this name already exists for the current tenant."]));
        var controller = CreateController<WebhooksController>("keycloak-subject-create-conflict");

        var result = await controller.CreateConsumer(
            new CreateWebhookConsumerRequestDto
            {
                ConsumerKindId = 1,
                Name = "Tenant automation",
                ProviderModeId = 2
            },
            CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_consumer_name_conflict");
    }

    [Test]
    public async Task GetEndpoints_DispatchesTenantScopedQueryAndReturnsHalCollection()
    {
        var endpoint = CreateEndpointDto();
        IReadOnlyList<WebhookEndpointDto> endpoints = [endpoint];
        var halCollection = new HalCollectionResource<WebhookEndpointDto>
        {
            TotalCount = 1,
            Embedded = new HalCollectionEmbedded<WebhookEndpointDto>
            {
                Items = [new HalResource<WebhookEndpointDto>(endpoint)]
            }
        };
        _mediator.Send(Arg.Any<GetWebhookEndpointsQuery>(), Arg.Any<CancellationToken>())
            .Returns(endpoints);
        _endpointAssembler.ToCollectionResource(
                endpoints,
                RouteNames.GetWebhookEndpoints,
                Arg.Any<object?>(),
                Arg.Any<HttpContext>())
            .Returns(halCollection);
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-endpoints");

        var result = await controller.GetEndpoints(
            (int)WebhookConsumerKind.Tenant,
            _tenantId,
            endpoint.ConsumerId,
            25,
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halCollection);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookEndpointsQuery>(query =>
                query.OwnerKindId == (int)WebhookConsumerKind.Tenant &&
                query.OwnerId == _tenantId &&
                query.ConsumerId == endpoint.ConsumerId &&
                query.Limit == 25),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetEndpoint_WhenFound_ReturnsHalResource()
    {
        var endpoint = CreateEndpointDto();
        var halResource = new HalResource<WebhookEndpointDto>(endpoint);
        _mediator.Send(Arg.Any<GetWebhookEndpointByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(endpoint);
        _endpointAssembler.ToResource(endpoint, Arg.Any<HttpContext>())
            .Returns(halResource);
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-endpoint-detail");

        var result = await controller.GetEndpoint(endpoint.Id, CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halResource);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookEndpointByIdQuery>(query =>
                query.EndpointId == endpoint.Id),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetEndpoint_WhenMissing_ReturnsNotFoundProblem()
    {
        _mediator.Send(Arg.Any<GetWebhookEndpointByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns((WebhookEndpointDto?)null);
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-endpoint-missing");

        var result = await controller.GetEndpoint(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_endpoint_not_found");
    }

    [Test]
    public async Task CreateEndpoint_DispatchesTenantScopedCommandAndReturnsCreatedRoute()
    {
        var consumerId = Guid.CreateVersion7();
        var endpointId = Guid.CreateVersion7();
        var eventTypeId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<CreateWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                endpointId,
                "Webhook endpoint created."));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-create-endpoint");

        var result = await controller.CreateEndpoint(
            new CreateWebhookEndpointRequestDto
            {
                ConsumerId = consumerId,
                Url = "https://integrator.example/webhooks/islamu",
                Description = "Integrator endpoint",
                SecretRef = "configuration:Webhooks:EndpointSecrets:integrator",
                EventTypeIds = [eventTypeId],
                MaxAttempts = 8,
                TimeoutSeconds = 15,
                RateLimitPerMinute = 60
            },
            CancellationToken.None);

        var created = result.Result as CreatedAtRouteResult;
        await Assert.That(created).IsNotNull();
        await Assert.That(created!.RouteName).IsEqualTo(RouteNames.GetWebhookEndpointById);
        await Assert.That(created.RouteValues!["endpointId"]).IsEqualTo(endpointId);
        await _mediator.Received(1).Send(
            Arg.Is<CreateWebhookEndpointCommand>(command =>
                command.ConsumerId == consumerId &&
                command.Url == "https://integrator.example/webhooks/islamu" &&
                command.Description == "Integrator endpoint" &&
                command.SecretRef == "configuration:Webhooks:EndpointSecrets:integrator" &&
                command.EventTypeIds.Contains(eventTypeId) &&
                command.MaxAttempts == 8 &&
                command.TimeoutSeconds == 15 &&
                command.RateLimitPerMinute == 60),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateEndpoint_WhenUrlConflicts_ReturnsConflictProblem()
    {
        _mediator.Send(Arg.Any<CreateWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "webhook_endpoint_url_conflict",
                "Webhook endpoint URL is already configured for this consumer.",
                ["Webhook endpoint URL is already configured for this consumer."]));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-endpoint-conflict");

        var result = await controller.CreateEndpoint(
            new CreateWebhookEndpointRequestDto
            {
                ConsumerId = Guid.CreateVersion7(),
                Url = "https://integrator.example/webhooks/islamu",
                SecretRef = "configuration:Webhooks:EndpointSecrets:integrator",
                EventTypeIds = [Guid.CreateVersion7()]
            },
            CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_endpoint_url_conflict");
    }

    [Test]
    public async Task UpdateEndpoint_DispatchesTenantScopedCommandAndReturnsOk()
    {
        var endpointId = Guid.CreateVersion7();
        var eventTypeId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<UpdateWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                endpointId,
                "Webhook endpoint updated."));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-update-endpoint");

        var result = await controller.UpdateEndpoint(
            endpointId,
            new UpdateWebhookEndpointRequestDto
            {
                Destination = new UpdateWebhookEndpointDestinationDto
                {
                    Url = "https://integrator.example/hooks/updated",
                    Description = "Updated endpoint"
                },
                Subscriptions = new UpdateWebhookEndpointSubscriptionsDto { EventTypeIds = [eventTypeId] },
                DeliveryPolicy = new UpdateWebhookEndpointDeliveryPolicyDto
                {
                    MaxAttempts = 6,
                    TimeoutSeconds = 12,
                    RateLimitPerMinute = 120
                },
                Governance = new UpdateWebhookEndpointGovernanceDto
                {
                    ExpectedConfigurationVersion = 4,
                    PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                    PendingWorkReason = "Preserve queued work.",
                    AcknowledgeUncertainProviderPublications = true
                }
            },
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsTypeOf<BaseCommandResponse<Guid>>();
        await _mediator.Received(1).Send(
            Arg.Is<UpdateWebhookEndpointCommand>(command =>
                command.EndpointId == endpointId &&
                command.Destination!.Url == "https://integrator.example/hooks/updated" &&
                command.Destination.Description == "Updated endpoint" &&
                command.Subscriptions!.EventTypeIds.Contains(eventTypeId) &&
                command.DeliveryPolicy!.MaxAttempts == 6 &&
                command.DeliveryPolicy.TimeoutSeconds == 12 &&
                command.DeliveryPolicy.RateLimitPerMinute == 120 &&
                command.Governance.ExpectedConfigurationVersion == 4 &&
                command.Governance.PendingWorkDecisionId == (int)WebhookPendingWorkDecision.PreserveExisting &&
                command.Governance.PendingWorkReason == "Preserve queued work." &&
                command.Governance.AcknowledgeUncertainProviderPublications),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateConsumerProviderMode_DispatchesGovernedTenantScopedCommandAndReturnsOk()
    {
        var consumerId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<UpdateWebhookConsumerProviderModeCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                consumerId,
                "Webhook consumer provider mode changed."));
        var controller = CreateController<WebhooksController>("keycloak-subject-update-consumer-provider-mode");

        var result = await controller.UpdateConsumerProviderMode(
            consumerId,
            new UpdateWebhookConsumerProviderModeRequestDto
            {
                ProviderMode = new UpdateWebhookConsumerProviderModeDto
                {
                    ProviderModeId = (int)WebhookProviderMode.Svix,
                    ExpectedConfigurationVersion = 5,
                    PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                    PendingWorkReason = "Move new deliveries to self-hosted Svix.",
                    AcknowledgeUncertainProviderPublications = true
                }
            },
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await _mediator.Received(1).Send(
            Arg.Is<UpdateWebhookConsumerProviderModeCommand>(command =>
                command.ConsumerId == consumerId &&
                command.ProviderModeId == (int)WebhookProviderMode.Svix &&
                command.ExpectedConfigurationVersion == 5 &&
                command.PendingWorkDecisionId == (int)WebhookPendingWorkDecision.PreserveExisting &&
                command.PendingWorkReason == "Move new deliveries to self-hosted Svix." &&
                command.AcknowledgeUncertainProviderPublications),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateConsumerProviderMode_WhenConfigurationConflicts_ReturnsConflictProblem()
    {
        _mediator.Send(Arg.Any<UpdateWebhookConsumerProviderModeCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "webhook_consumer_configuration_conflict",
                "Webhook consumer configuration changed.",
                ["Webhook consumer configuration changed."]));
        var controller = CreateController<WebhooksController>("keycloak-subject-update-consumer-provider-mode-conflict");

        var result = await controller.UpdateConsumerProviderMode(
            Guid.CreateVersion7(),
            new UpdateWebhookConsumerProviderModeRequestDto
            {
                ProviderMode = new UpdateWebhookConsumerProviderModeDto
                {
                    ProviderModeId = (int)WebhookProviderMode.Local,
                    ExpectedConfigurationVersion = 2,
                    PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                    PendingWorkReason = "Preserve pending work."
                }
            },
            CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"])
            .IsEqualTo("webhook_consumer_configuration_conflict");
    }

    [Test]
    public async Task UpdateConsumerProviderMode_WithoutProviderModeGroup_ReturnsValidationProblem()
    {
        var controller = CreateController<WebhooksController>("keycloak-subject-update-consumer-provider-mode-invalid");

        var result = await controller.UpdateConsumerProviderMode(
            Guid.CreateVersion7(),
            new UpdateWebhookConsumerProviderModeRequestDto(),
            CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await _mediator.DidNotReceive().Send(
            Arg.Any<UpdateWebhookConsumerProviderModeCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateEndpoint_WhenMissing_ReturnsNotFoundProblem()
    {
        _mediator.Send(Arg.Any<UpdateWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "webhook_endpoint_not_found",
                "Webhook endpoint was not found.",
                ["Webhook endpoint was not found."]));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-update-missing");

        var result = await controller.UpdateEndpoint(
            Guid.CreateVersion7(),
            new UpdateWebhookEndpointRequestDto
            {
                Destination = new UpdateWebhookEndpointDestinationDto
                {
                    Url = "https://integrator.example/webhooks/islamu"
                },
                Subscriptions = new UpdateWebhookEndpointSubscriptionsDto
                {
                    EventTypeIds = [Guid.CreateVersion7()]
                },
                Governance = new UpdateWebhookEndpointGovernanceDto
                {
                    ExpectedConfigurationVersion = 1,
                    PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                    PendingWorkReason = "Preserve queued work."
                }
            },
            CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_endpoint_not_found");
    }

    [Test]
    public async Task RotateEndpointSecret_DispatchesTenantScopedCommandAndReturnsOk()
    {
        var endpointId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<RotateWebhookEndpointSecretCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                endpointId,
                "Webhook endpoint secret rotated."));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-rotate-endpoint-secret");

        var result = await controller.RotateEndpointSecret(
            endpointId,
            new RotateWebhookEndpointSecretRequestDto
            {
                NewSecretRef = "configuration:Webhooks:EndpointSecrets:integrator:v2",
                PreviousSecretValidForSeconds = 3_600,
                ExpectedConfigurationVersion = 7,
                PendingWorkDecisionId = (int)WebhookPendingWorkDecision.MigrateEligible,
                PendingWorkReason = "Move pending work to the rotated credential.",
                AcknowledgeUncertainProviderPublications = true
            },
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsTypeOf<BaseCommandResponse<Guid>>();
        await _mediator.Received(1).Send(
            Arg.Is<RotateWebhookEndpointSecretCommand>(command =>
                command.EndpointId == endpointId &&
                command.NewSecretRef == "configuration:Webhooks:EndpointSecrets:integrator:v2" &&
                command.PreviousSecretValidForSeconds == 3_600 &&
                command.ExpectedConfigurationVersion == 7 &&
                command.PendingWorkDecisionId == (int)WebhookPendingWorkDecision.MigrateEligible &&
                command.PendingWorkReason == "Move pending work to the rotated credential." &&
                command.AcknowledgeUncertainProviderPublications),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RotateEndpointSecret_WhenEndpointMissing_ReturnsNotFoundProblem()
    {
        _mediator.Send(Arg.Any<RotateWebhookEndpointSecretCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "webhook_endpoint_not_found",
                "Webhook endpoint was not found.",
                ["Webhook endpoint was not found."]));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-rotate-endpoint-secret-missing");

        var result = await controller.RotateEndpointSecret(
            Guid.CreateVersion7(),
            new RotateWebhookEndpointSecretRequestDto
            {
                NewSecretRef = "configuration:Webhooks:EndpointSecrets:integrator:v2",
                ExpectedConfigurationVersion = 1,
                PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                PendingWorkReason = "Preserve queued work."
            },
            CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_endpoint_not_found");
    }

    [Test]
    public async Task TestEndpoint_DispatchesTenantScopedCommandAndReturnsOk()
    {
        var endpointId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<TestWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                messageId,
                "Webhook endpoint test scheduled."));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-test-endpoint");

        var result = await controller.TestEndpoint(endpointId, CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var response = ok!.Value as BaseCommandResponse<Guid>;
        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Id).IsEqualTo(messageId);
        await _mediator.Received(1).Send(
            Arg.Is<TestWebhookEndpointCommand>(command =>
                command.EndpointId == endpointId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TestEndpoint_WhenEndpointMissing_ReturnsNotFoundProblem()
    {
        _mediator.Send(Arg.Any<TestWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "webhook_endpoint_not_found",
                "Webhook endpoint was not found.",
                ["Webhook endpoint was not found."]));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-test-endpoint-missing");

        var result = await controller.TestEndpoint(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_endpoint_not_found");
    }

    [Test]
    public async Task DeleteEndpoint_DispatchesTenantScopedArchiveCommandAndReturnsNoContent()
    {
        var endpointId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<ArchiveWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                endpointId,
                "Webhook endpoint archived."));
        var controller = CreateController<WebhookEndpointsController>("keycloak-subject-delete-endpoint");

        var result = await controller.DeleteEndpoint(endpointId, CancellationToken.None);

        await Assert.That(result).IsTypeOf<NoContentResult>();
        await _mediator.Received(1).Send(
            Arg.Is<ArchiveWebhookEndpointCommand>(command =>
                command.EndpointId == endpointId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EndpointDetailLinks_WhenActive_ExposeUpdateRotateTestAndDeleteAuthorizationMetadata()
    {
        var endpoint = CreateEndpointDto();

        var links = new WebhookEndpointDetailLinkPolicy()
            .GetLinks(endpoint, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var update = links.Single(link => link.Rel == LinkRelations.Update);
        var rotate = links.Single(link => link.Rel == LinkRelations.RotateSecret);
        var test = links.Single(link => link.Rel == LinkRelations.Test);
        var delete = links.Single(link => link.Rel == LinkRelations.Delete);
        await Assert.That(update.RouteName).IsEqualTo(RouteNames.UpdateWebhookEndpoint);
        await Assert.That(update.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.Update);
        await Assert.That(rotate.RouteName).IsEqualTo(RouteNames.RotateWebhookEndpointSecret);
        await Assert.That(rotate.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.RotateSecret);
        await Assert.That(test.RouteName).IsEqualTo(RouteNames.TestWebhookEndpoint);
        await Assert.That(test.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.Test);
        await Assert.That(delete.RouteName).IsEqualTo(RouteNames.DeleteWebhookEndpoint);
        await Assert.That(delete.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.Delete);
    }

    [Test]
    public async Task EndpointDetailLinks_ExposeResumeOnlyForAutoPausedLocalDelivery()
    {
        var localAutoPaused = CreateEndpointDto(3, "AutoPaused", "Local");
        var localDisabled = CreateEndpointDto(2, "Disabled", "Local");
        var localActive = CreateEndpointDto();
        var managedAutoPaused = CreateEndpointDto(3, "AutoPaused", "Svix");
        var policy = new WebhookEndpointDetailLinkPolicy();

        var localLinks = policy.GetLinks(localAutoPaused, null).ToList();
        var disabledLinks = policy.GetLinks(localDisabled, null).ToList();
        var activeLinks = policy.GetLinks(localActive, null).ToList();
        var managedLinks = policy.GetLinks(managedAutoPaused, null).ToList();

        var resume = localLinks.Single(link => link.Rel == LinkRelations.Resume);
        await Assert.That(resume.RouteName).IsEqualTo(RouteNames.ResumeWebhookEndpoint);
        await Assert.That(resume.Method).IsEqualTo("POST");
        await Assert.That(resume.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.Resume);
        await Assert.That(disabledLinks.Any(link => link.Rel == LinkRelations.Resume)).IsTrue();
        await Assert.That(activeLinks.Any(link => link.Rel == LinkRelations.Resume)).IsFalse();
        await Assert.That(managedLinks.Any(link => link.Rel == LinkRelations.Resume)).IsFalse();
    }

    [Test]
    public async Task EndpointDetailLinks_ExposePauseOnlyForActiveLocalDelivery()
    {
        var localActive = CreateEndpointDto();
        var localDisabled = CreateEndpointDto(2, "Disabled", "Local");
        var managedActive = CreateEndpointDto(1, "Active", "Svix");
        var policy = new WebhookEndpointDetailLinkPolicy();

        var activeLinks = policy.GetLinks(localActive, null).ToList();
        var disabledLinks = policy.GetLinks(localDisabled, null).ToList();
        var managedLinks = policy.GetLinks(managedActive, null).ToList();

        var pause = activeLinks.Single(link => link.Rel == LinkRelations.Pause);
        await Assert.That(pause.RouteName).IsEqualTo(RouteNames.PauseWebhookEndpoint);
        await Assert.That(pause.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.Pause);
        await Assert.That(disabledLinks.Any(link => link.Rel == LinkRelations.Pause)).IsFalse();
        await Assert.That(managedLinks.Any(link => link.Rel == LinkRelations.Pause)).IsFalse();
    }

    [Test]
    public async Task PauseEndpointRoute_UsesWriteControlsAndWebhookPauseAuthorization()
    {
        var action = typeof(WebhookEndpointOperationsController)
            .GetMethod(nameof(WebhookEndpointOperationsController.Pause))!;
        var route = action.GetCustomAttribute<HttpPostAttribute>();
        var authorization = typeof(PauseWebhookEndpointCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(route?.Template).IsEqualTo("{endpointId:guid}/pause");
        await Assert.That(route?.Name).IsEqualTo(RouteNames.PauseWebhookEndpoint);
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(authorization?.Action).IsEqualTo(AuthorizationActions.Webhooks.Pause);
    }

    [Test]
    public async Task PauseEndpoint_DispatchesTenantActorAndReasonCode()
    {
        var endpointId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<PauseWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                endpointId,
                "Webhook endpoint paused."));
        var controller = CreateEndpointOperationsController(actorUserId);

        var result = await controller.Pause(
            endpointId,
            new PauseWebhookEndpointRequestDto
            {
                ExpectedDeliveryStateVersion = 17,
                ReasonCode = "operator.maintenance"
            },
            CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await _mediator.Received(1).Send(
            Arg.Is<PauseWebhookEndpointCommand>(command =>
                command.EndpointId == endpointId &&
                command.ActorUserId == actorUserId &&
                command.ExpectedDeliveryStateVersion == 17 &&
                command.ReasonCode == "operator.maintenance"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResumeEndpointRoute_UsesWriteControlsAndWebhookResumeAuthorization()
    {
        var action = typeof(WebhookEndpointOperationsController)
            .GetMethod(nameof(WebhookEndpointOperationsController.Resume))!;
        var route = action.GetCustomAttribute<HttpPostAttribute>();
        var authorization = typeof(ResumeWebhookEndpointCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo("{endpointId:guid}/resume");
        await Assert.That(route.Name).IsEqualTo(RouteNames.ResumeWebhookEndpoint);
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(action.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName)
            .IsEqualTo(RequestTimeoutExtensions.DefaultPolicy);
        await Assert.That(authorization).IsNotNull();
        await Assert.That(authorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Webhooks.Resume);
    }

    [Test]
    public async Task ResumeEndpoint_DispatchesTenantAndActorScopedCommand()
    {
        var endpointId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<ResumeWebhookEndpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                endpointId,
                "Webhook endpoint resumed."));
        var controller = CreateEndpointOperationsController(actorUserId);

        var result = await controller.Resume(
            endpointId,
            new ResumeWebhookEndpointRequestDto
            {
                ExpectedDeliveryStateVersion = 19,
                ReasonCode = "operator.recovered"
            },
            CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await _mediator.Received(1).Send(
            Arg.Is<ResumeWebhookEndpointCommand>(command =>
                command.EndpointId == endpointId
                && command.ActorUserId == actorUserId
                && command.ExpectedDeliveryStateVersion == 19
                && command.ReasonCode == "operator.recovered"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConsumerDetailLinks_ExposeProviderModeChangeOnlyForNonArchivedConsumers()
    {
        var activeConsumer = CreateConsumerDto();
        var archivedConsumer = CreateConsumerDto(status: WebhookConsumerStatus.Archived);
        var policy = new WebhookConsumerDetailLinkPolicy();

        var activeLinks = policy
            .GetLinks(activeConsumer, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();
        var archivedLinks = policy
            .GetLinks(archivedConsumer, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var changeMode = activeLinks.Single(link => link.Rel == LinkRelations.ChangeProviderMode);
        await Assert.That(changeMode.RouteName).IsEqualTo(RouteNames.UpdateWebhookConsumerProviderMode);
        await Assert.That(changeMode.Method).IsEqualTo("PATCH");
        await Assert.That(changeMode.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.Update);
        await Assert.That(archivedLinks.Any(link => link.Rel == LinkRelations.ChangeProviderMode)).IsFalse();
    }

    [Test]
    [Arguments("Svix")]
    [Arguments("DryRun")]
    [Arguments("Disabled")]
    public async Task EndpointDetailLinks_WhenProviderManaged_HideLocalTestAffordance(string providerModeName)
    {
        var endpoint = CreateEndpointDto(providerModeName: providerModeName);

        var links = new WebhookEndpointDetailLinkPolicy()
            .GetLinks(endpoint, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Test)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Update)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.RotateSecret)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Delete)).IsTrue();
    }

    [Test]
    public async Task EndpointDetailLinks_WhenArchived_HideMutationAffordances()
    {
        var endpoint = CreateEndpointDto(statusId: 4, statusName: "Archived");

        var links = new WebhookEndpointDetailLinkPolicy()
            .GetLinks(endpoint, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Update)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.RotateSecret)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Test)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Delete)).IsFalse();
    }

    [Test]
    public async Task EndpointAssembler_WhenMutationAuthorizationDenied_OmitsMutationAffordances()
    {
        var endpoint = CreateEndpointDto();
        var evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(
                Arg.Any<IReadOnlyList<LinkDefinition>>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<HttpContext>())
            .Returns(call =>
            {
                var definitions = call.ArgAt<IReadOnlyList<LinkDefinition>>(0);
                return Task.FromResult<IReadOnlyList<bool>>(
                    definitions.Select(link => link.Rel == LinkRelations.Self).ToArray());
            });
        var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call =>
            {
                var definition = call.ArgAt<LinkDefinition>(0);
                return HalLink.CreateAction($"/{definition.RouteName}", definition.Method);
            });
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(evaluator)
                .BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
        };
        var assembler = new HalResourceAssembler<WebhookEndpointDto, WebhookEndpointDto>(
            linkGenerator,
            new WebhookEndpointDetailLinkPolicy(),
            new WebhookEndpointCollectionLinkPolicy());

        var resource = await assembler.ToResource(endpoint, httpContext);

        await Assert.That(resource.Links.ContainsKey(LinkRelations.Self)).IsTrue();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Update)).IsFalse();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.RotateSecret)).IsFalse();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Test)).IsFalse();
        await Assert.That(resource.Links.ContainsKey(LinkRelations.Delete)).IsFalse();
    }

    [Test]
    public async Task WebhookAuditDtos_ExposeOnlySafeOperationalMetadata()
    {
        var messageProperties = typeof(WebhookMessageDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var attemptProperties = typeof(WebhookDeliveryAttemptDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(messageProperties).Contains(nameof(WebhookMessageDto.PayloadHash));
        await Assert.That(messageProperties).Contains(nameof(WebhookMessageDto.PayloadRetentionUntil));
        await Assert.That(messageProperties).DoesNotContain("PayloadJson");
        await Assert.That(messageProperties).DoesNotContain("RawPayloadJson");
        await Assert.That(messageProperties).DoesNotContain(nameof(WebhookMessagePayloadDto.PayloadBase64));
        await Assert.That(messageProperties).DoesNotContain("SecretRef");
        await Assert.That(messageProperties).DoesNotContain("Signature");
        await Assert.That(attemptProperties).DoesNotContain("ResponseBodyPreview");
        await Assert.That(attemptProperties).DoesNotContain("EndpointUrl");
        await Assert.That(attemptProperties).DoesNotContain("ResponseBody");
        await Assert.That(attemptProperties).DoesNotContain("RequestHeaders");
        await Assert.That(attemptProperties).DoesNotContain("SecretRef");
        await Assert.That(attemptProperties).DoesNotContain("Signature");
    }

    [Test]
    public async Task MessageAndDeliveryAuditRoutes_UseStableMetadataAndWebhookAuthorizationActions()
    {
        var controllerType = typeof(WebhookMessagesController);
        var listMessagesAction = controllerType.GetMethod(nameof(WebhookMessagesController.GetMessages))!;
        var detailMessageAction = controllerType.GetMethod(nameof(WebhookMessagesController.GetMessage))!;
        var payloadAction = controllerType.GetMethod(nameof(WebhookMessagesController.GetMessagePayload))!;
        var listAttemptsAction = controllerType.GetMethod(nameof(WebhookMessagesController.GetDeliveryAttempts))!;
        var detailAttemptAction = controllerType.GetMethod(nameof(WebhookMessagesController.GetDeliveryAttempt))!;
        var retryAttemptAction = controllerType.GetMethod(nameof(WebhookMessagesController.RetryDeliveryAttempt))!;
        var listMessagesRoute = listMessagesAction.GetCustomAttribute<HttpGetAttribute>();
        var detailMessageRoute = detailMessageAction.GetCustomAttribute<HttpGetAttribute>();
        var payloadRoute = payloadAction.GetCustomAttribute<HttpGetAttribute>();
        var listAttemptsRoute = listAttemptsAction.GetCustomAttribute<HttpGetAttribute>();
        var detailAttemptRoute = detailAttemptAction.GetCustomAttribute<HttpGetAttribute>();
        var retryAttemptRoute = retryAttemptAction.GetCustomAttribute<HttpPostAttribute>();
        var listMessagesAuthorization = typeof(GetWebhookMessagesQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var detailMessageAuthorization = typeof(GetWebhookMessageByIdQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var payloadAuthorization = typeof(GetWebhookMessagePayloadQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var listAttemptsAuthorization = typeof(GetWebhookDeliveryAttemptsQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var detailAttemptAuthorization = typeof(GetWebhookDeliveryAttemptByIdQuery).GetCustomAttribute<AuthorizeResourceAttribute>();
        var retryAttemptAuthorization = typeof(RetryWebhookDeliveryAttemptCommand).GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(listMessagesRoute).IsNotNull();
        await Assert.That(listMessagesRoute!.Template).IsEqualTo("messages");
        await Assert.That(listMessagesRoute.Name).IsEqualTo(RouteNames.GetWebhookMessages);
        await Assert.That(detailMessageRoute).IsNotNull();
        await Assert.That(detailMessageRoute!.Template).IsEqualTo("messages/{messageId:guid}");
        await Assert.That(detailMessageRoute.Name).IsEqualTo(RouteNames.GetWebhookMessageById);
        await Assert.That(payloadRoute).IsNotNull();
        await Assert.That(payloadRoute!.Template).IsEqualTo("messages/{messageId:guid}/payload");
        await Assert.That(payloadRoute.Name).IsEqualTo(RouteNames.GetWebhookMessagePayload);
        await Assert.That(listAttemptsRoute).IsNotNull();
        await Assert.That(listAttemptsRoute!.Template).IsEqualTo("delivery-attempts");
        await Assert.That(listAttemptsRoute.Name).IsEqualTo(RouteNames.GetWebhookDeliveryAttempts);
        await Assert.That(detailAttemptRoute).IsNotNull();
        await Assert.That(detailAttemptRoute!.Template).IsEqualTo("delivery-attempts/{attemptId:guid}");
        await Assert.That(detailAttemptRoute.Name).IsEqualTo(RouteNames.GetWebhookDeliveryAttemptById);
        await Assert.That(retryAttemptRoute).IsNotNull();
        await Assert.That(retryAttemptRoute!.Template).IsEqualTo("delivery-attempts/{attemptId:guid}/retry");
        await Assert.That(retryAttemptRoute.Name).IsEqualTo(RouteNames.RetryWebhookDeliveryAttempt);
        await Assert.That(listMessagesAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        await Assert.That(detailMessageAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        await Assert.That(payloadAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        await Assert.That(listAttemptsAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        await Assert.That(detailAttemptAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        await Assert.That(retryAttemptAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(retryAttemptAction.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName)
            .IsEqualTo(RequestTimeoutExtensions.DefaultPolicy);
        await Assert.That(listMessagesAction.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(detailMessageAction.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(payloadAction.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        var payloadResponseCache = payloadAction.GetCustomAttribute<ResponseCacheAttribute>();
        await Assert.That(payloadResponseCache).IsNotNull();
        await Assert.That(payloadResponseCache!.NoStore).IsTrue();
        await Assert.That(payloadResponseCache.Location).IsEqualTo(ResponseCacheLocation.None);
        await Assert.That(payloadResponseCache.Duration).IsEqualTo(0);
        await Assert.That(listAttemptsAction.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(detailAttemptAction.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(listMessagesAuthorization).IsNotNull();
        await Assert.That(listMessagesAuthorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(listMessagesAuthorization.Action).IsEqualTo(AuthorizationActions.Webhooks.ViewDelivery);
        await Assert.That(detailMessageAuthorization).IsNotNull();
        await Assert.That(detailMessageAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.ViewDelivery);
        await Assert.That(payloadAuthorization).IsNotNull();
        await Assert.That(payloadAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.ViewPayload);
        await Assert.That(listAttemptsAuthorization).IsNotNull();
        await Assert.That(listAttemptsAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.ViewDelivery);
        await Assert.That(detailAttemptAuthorization).IsNotNull();
        await Assert.That(detailAttemptAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.ViewDelivery);
        await Assert.That(retryAttemptAuthorization).IsNotNull();
        await Assert.That(retryAttemptAuthorization!.Action).IsEqualTo(AuthorizationActions.Webhooks.Retry);
    }

    [Test]
    public async Task GetMessages_DispatchesTenantScopedQueryAndReturnsHalCollection()
    {
        var message = CreateMessageDto();
        IReadOnlyList<WebhookMessageDto> messages = [message];
        var halCollection = new HalCollectionResource<WebhookMessageDto>
        {
            TotalCount = 1,
            Embedded = new HalCollectionEmbedded<WebhookMessageDto>
            {
                Items = [new HalResource<WebhookMessageDto>(message)]
            }
        };
        _mediator.Send(Arg.Any<GetWebhookMessagesQuery>(), Arg.Any<CancellationToken>())
            .Returns(messages);
        _messageAssembler.ToCollectionResource(
                messages,
                RouteNames.GetWebhookMessages,
                Arg.Any<object?>(),
                Arg.Any<HttpContext>())
            .Returns(halCollection);
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-messages");

        var result = await controller.GetMessages(
            (int)WebhookConsumerKind.Tenant,
            _tenantId,
            25,
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halCollection);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookMessagesQuery>(query =>
                query != null &&
                query.OwnerKindId == (int)WebhookConsumerKind.Tenant &&
                query.OwnerId == _tenantId &&
                query.Limit == 25),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetMessage_WhenFound_ReturnsHalResource()
    {
        var message = CreateMessageDto();
        var halResource = new HalResource<WebhookMessageDto>(message);
        _mediator.Send(Arg.Any<GetWebhookMessageByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(message);
        _messageAssembler.ToResource(message, Arg.Any<HttpContext>())
            .Returns(halResource);
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-message-detail");

        var result = await controller.GetMessage(message.Id, CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halResource);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookMessageByIdQuery>(query =>
                query != null &&
                query.MessageId == message.Id),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetMessage_WhenMissing_ReturnsNotFoundProblem()
    {
        _mediator.Send(Arg.Any<GetWebhookMessageByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns((WebhookMessageDto?)null);
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-message-missing");

        var result = await controller.GetMessage(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_message_not_found");
    }

    [Test]
    public async Task GetMessagePayload_WhenAvailable_ReturnsDedicatedPayloadContract()
    {
        var messageId = Guid.CreateVersion7();
        var payload = new WebhookMessagePayloadDto
        {
            MessageId = messageId,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            PayloadBase64 = Convert.ToBase64String("{}"u8),
            PayloadHash = "sha256:ab3d5f2c4e8a",
            PayloadByteLength = 2,
            PayloadRetentionUntil = DateTime.UtcNow.AddHours(1),
            RetrievedAt = DateTime.UtcNow
        };
        _mediator.Send(Arg.Any<GetWebhookMessagePayloadQuery>(), Arg.Any<CancellationToken>())
            .Returns(WebhookMessagePayloadReadResult.Available(payload));
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-payload");

        var result = await controller.GetMessagePayload(messageId, CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(payload);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookMessagePayloadQuery>(query =>
                query.MessageId == messageId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetMessagePayload_WhenOutsideTenant_ReturnsGenericNotFoundProblem()
    {
        _mediator.Send(Arg.Any<GetWebhookMessagePayloadQuery>(), Arg.Any<CancellationToken>())
            .Returns(WebhookMessagePayloadReadResult.NotFound());
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-payload-missing");

        var result = await controller.GetMessagePayload(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_message_payload_not_found");
    }

    [Test]
    public async Task GetMessagePayload_WhenRetentionEnded_ReturnsGoneProblem()
    {
        _mediator.Send(Arg.Any<GetWebhookMessagePayloadQuery>(), Arg.Any<CancellationToken>())
            .Returns(WebhookMessagePayloadReadResult.Gone());
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-payload-gone");

        var result = await controller.GetMessagePayload(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status410Gone);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_message_payload_gone");
    }

    [Test]
    public async Task GetDeliveryAttempts_NormalizesEmptyFiltersAndReturnsHalCollection()
    {
        var attempt = CreateDeliveryAttemptDto();
        IReadOnlyList<WebhookDeliveryAttemptDto> attempts = [attempt];
        var halCollection = new HalCollectionResource<WebhookDeliveryAttemptDto>
        {
            TotalCount = 1,
            Embedded = new HalCollectionEmbedded<WebhookDeliveryAttemptDto>
            {
                Items = [new HalResource<WebhookDeliveryAttemptDto>(attempt)]
            }
        };
        _mediator.Send(Arg.Any<GetWebhookDeliveryAttemptsQuery>(), Arg.Any<CancellationToken>())
            .Returns(attempts);
        _attemptAssembler.ToCollectionResource(
                attempts,
                RouteNames.GetWebhookDeliveryAttempts,
                Arg.Any<object?>(),
                Arg.Any<HttpContext>())
            .Returns(halCollection);
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-attempts");

        var result = await controller.GetDeliveryAttempts(
            (int)WebhookConsumerKind.Tenant,
            _tenantId,
            null,
            attempt.EndpointId,
            25,
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halCollection);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookDeliveryAttemptsQuery>(query =>
                query != null &&
                query.OwnerKindId == (int)WebhookConsumerKind.Tenant &&
                query.OwnerId == _tenantId &&
                query.MessageId == null &&
                query.EndpointId == attempt.EndpointId &&
                query.Limit == 25),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetDeliveryAttempt_WhenFound_ReturnsHalResource()
    {
        var attempt = CreateDeliveryAttemptDto();
        var halResource = new HalResource<WebhookDeliveryAttemptDto>(attempt);
        _mediator.Send(Arg.Any<GetWebhookDeliveryAttemptByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns(attempt);
        _attemptAssembler.ToResource(attempt, Arg.Any<HttpContext>())
            .Returns(halResource);
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-attempt-detail");

        var result = await controller.GetDeliveryAttempt(attempt.Id, CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halResource);
        await _mediator.Received(1).Send(
            Arg.Is<GetWebhookDeliveryAttemptByIdQuery>(query =>
                query != null &&
                query.AttemptId == attempt.Id),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetDeliveryAttempt_WhenMissing_ReturnsNotFoundProblem()
    {
        _mediator.Send(Arg.Any<GetWebhookDeliveryAttemptByIdQuery>(), Arg.Any<CancellationToken>())
            .Returns((WebhookDeliveryAttemptDto?)null);
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-attempt-missing");

        var result = await controller.GetDeliveryAttempt(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_delivery_attempt_not_found");
    }

    [Test]
    public async Task RetryDeliveryAttempt_DispatchesTenantScopedCommandAndReturnsOk()
    {
        var attemptId = Guid.CreateVersion7();
        var retryAttemptId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<RetryWebhookDeliveryAttemptCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                retryAttemptId,
                "Webhook delivery retry scheduled."));
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-attempt-retry");

        var result = await controller.RetryDeliveryAttempt(attemptId, CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var response = ok!.Value as BaseCommandResponse<Guid>;
        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Id).IsEqualTo(retryAttemptId);
        await _mediator.Received(1).Send(
            Arg.Is<RetryWebhookDeliveryAttemptCommand>(command =>
                command != null &&
                command.AttemptId == attemptId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryDeliveryAttempt_WhenAttemptIsNotRetryable_ReturnsConflictProblem()
    {
        _mediator.Send(Arg.Any<RetryWebhookDeliveryAttemptCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "webhook_delivery_attempt_not_retryable",
                "Webhook delivery retry cannot be scheduled for this attempt.",
                ["Webhook delivery retry cannot be scheduled for this attempt."]));
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-webhook-attempt-retry-conflict");

        var result = await controller.RetryDeliveryAttempt(Guid.CreateVersion7(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_delivery_attempt_not_retryable");
    }

    [Test]
    public async Task RedriveIncomingWebhook_DispatchesServerTenantAndExpectedGeneration()
    {
        var messageId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<RedriveIncomingWebhookCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                messageId,
                "Incoming webhook redrive scheduled."));
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-incoming-webhook-redrive");
        var request = new RedriveIncomingWebhookRequestDto
        {
            ExpectedProcessingGeneration = 4,
            Reason = "operator-confirmed-recovery"
        };

        var result = await controller.RedriveIncomingWebhook(messageId, request, CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await _mediator.Received(1).Send(
            Arg.Is<RedriveIncomingWebhookCommand>(command =>
                command.TenantId == _tenantId &&
                command.IncomingWebhookMessageId == messageId &&
                command.ExpectedProcessingGeneration == request.ExpectedProcessingGeneration &&
                command.Reason == request.Reason),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RedriveIncomingWebhook_WhenGenerationIsStale_ReturnsConflictProblem()
    {
        _mediator.Send(Arg.Any<RedriveIncomingWebhookCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "incoming_webhook_redrive_generation_conflict",
                "Incoming webhook processing generation changed before redrive.",
                ["Incoming webhook processing generation changed before redrive."]));
        var controller = CreateController<WebhookMessagesController>("keycloak-subject-incoming-webhook-redrive-conflict");

        var result = await controller.RedriveIncomingWebhook(
            Guid.CreateVersion7(),
            new RedriveIncomingWebhookRequestDto
            {
                ExpectedProcessingGeneration = 1,
                Reason = "stale-redrive"
            },
            CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"])
            .IsEqualTo("incoming_webhook_redrive_generation_conflict");
    }

    [Test]
    public async Task RedriveIncomingWebhookRoute_UsesStableWriteAndAuthorizationMetadata()
    {
        var action = typeof(WebhookMessagesController).GetMethod(nameof(WebhookMessagesController.RedriveIncomingWebhook))!;
        var route = action.GetCustomAttribute<HttpPostAttribute>();
        var authorization = typeof(RedriveIncomingWebhookCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo("incoming/{incomingWebhookMessageId:guid}/redrive");
        await Assert.That(route.Name).IsEqualTo(RouteNames.RedriveIncomingWebhook);
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(action.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName)
            .IsEqualTo(RequestTimeoutExtensions.DefaultPolicy);
        await Assert.That(authorization).IsNotNull();
        await Assert.That(authorization!.Resource).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Webhooks.RedriveIncoming);
    }

    [Test]
    public async Task WebhookMessageLinks_ExposeViewDeliveryAuditAffordances()
    {
        var now = DateTime.UtcNow;
        var message = CreateMessageDto(payloadRetentionUntil: now.AddHours(1));

        var links = new WebhookMessageDetailLinkPolicy(new FixedTimeProvider(now))
            .GetLinks(message, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        var deliveryAttempts = links.Single(link => link.Rel == LinkRelations.DeliveryAttempts);
        var payload = links.Single(link => link.Rel == LinkRelations.Payload);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetWebhookMessageById);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.ViewDelivery);
        await Assert.That(deliveryAttempts.RouteName).IsEqualTo(RouteNames.GetWebhookDeliveryAttempts);
        await Assert.That(deliveryAttempts.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.ViewDelivery);
        await Assert.That(payload.RouteName).IsEqualTo(RouteNames.GetWebhookMessagePayload);
        await Assert.That(payload.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.ViewPayload);
    }

    [Test]
    public async Task WebhookMessageLinks_OmitPayloadWhenExpiredOrCleared()
    {
        var now = DateTime.UtcNow;
        var policy = new WebhookMessageDetailLinkPolicy(new FixedTimeProvider(now));
        var expired = CreateMessageDto(payloadRetentionUntil: now.AddTicks(-1));
        var cleared = CreateMessageDto(
            payloadRetentionUntil: now.AddHours(1),
            payloadClearedAt: now.AddMinutes(-1));

        var expiredLinks = policy.GetLinks(expired, null).ToList();
        var clearedLinks = policy.GetLinks(cleared, null).ToList();

        await Assert.That(expiredLinks.Any(link => link.Rel == LinkRelations.Payload)).IsFalse();
        await Assert.That(clearedLinks.Any(link => link.Rel == LinkRelations.Payload)).IsFalse();
    }

    [Test]
    public async Task WebhookDeliveryAttemptLinks_ExposeRetryOnlyForFailedOrAbandonedAttempts()
    {
        var failedAttempt = CreateDeliveryAttemptDto(statusName: "Failed");
        var succeededAttempt = CreateDeliveryAttemptDto(statusName: "Succeeded");

        var failedLinks = new WebhookDeliveryAttemptDetailLinkPolicy()
            .GetLinks(failedAttempt, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();
        var succeededLinks = new WebhookDeliveryAttemptDetailLinkPolicy()
            .GetLinks(succeededAttempt, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var retry = failedLinks.Single(link => link.Rel == LinkRelations.Retry);
        await Assert.That(retry.RouteName).IsEqualTo(RouteNames.RetryWebhookDeliveryAttempt);
        await Assert.That(retry.PermissionAction).IsEqualTo(AuthorizationActions.Webhooks.Retry);
        await Assert.That(succeededLinks.Any(link => link.Rel == LinkRelations.Retry)).IsFalse();
    }

    [Test]
    public async Task OpenSvixAppPortal_DispatchesTenantScopedCommandWithProviderSubject()
    {
        var consumerId = Guid.CreateVersion7();
        var responseDto = new WebhookProviderPortalAccessDto
        {
            Url = "https://svix.example/app-portal/session",
            Token = "portal-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _mediator.Send(Arg.Any<OpenSvixAppPortalCommand>(), Arg.Any<CancellationToken>())
            .Returns(WebhookProviderPortalAccessCommandResponse.Success(
                responseDto,
                message: null,
                isRetryable: false));
        var controller = CreateController<WebhooksController>("keycloak-subject-1");

        var result = await controller.OpenSvixAppPortal(
            new OpenSvixAppPortalRequestDto
            {
                ConsumerId = consumerId,
                ExpiresInSeconds = 900
            },
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(responseDto);
        await _mediator.Received(1).Send(
            Arg.Is<OpenSvixAppPortalCommand>(command =>
                command.ConsumerId == consumerId &&
                command.SessionId == "keycloak-subject-1" &&
                command.ExpiresInSeconds == 900),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OpenSvixAppPortal_WhenConsumerMissing_ReturnsNotFoundProblem()
    {
        _mediator.Send(Arg.Any<OpenSvixAppPortalCommand>(), Arg.Any<CancellationToken>())
            .Returns(WebhookProviderPortalAccessCommandResponse.Failure(
                BaseCommandResponse.Failure<WebhookProviderPortalAccessDto>(
                    "webhook_consumer_not_found",
                    "Webhook consumer was not found.",
                    ["webhook_consumer_not_found"]),
                isRetryable: false));
        var controller = CreateController<WebhooksController>("keycloak-subject-2");

        var result = await controller.OpenSvixAppPortal(new OpenSvixAppPortalRequestDto(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("webhook_consumer_not_found");
    }

    [Test]
    public async Task OpenSvixAppPortal_WhenProviderFailureRetryable_ReturnsServiceUnavailableProblem()
    {
        _mediator.Send(Arg.Any<OpenSvixAppPortalCommand>(), Arg.Any<CancellationToken>())
            .Returns(WebhookProviderPortalAccessCommandResponse.Failure(
                BaseCommandResponse.Failure<WebhookProviderPortalAccessDto>(
                    "svix_provider_unavailable",
                    "Svix is temporarily unavailable.",
                    ["SvixApi:503"]),
                isRetryable: true));
        var controller = CreateController<WebhooksController>("keycloak-subject-3");

        var result = await controller.OpenSvixAppPortal(new OpenSvixAppPortalRequestDto(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        var problem = objectResult.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("svix_provider_unavailable");
    }

    private TController CreateController<TController>(string providerSubject)
        where TController : ControllerBase
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(Guid.CreateVersion7());
        var services = new ServiceCollection()
            .AddSingleton(userContext)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", providerSubject)],
                "TestAuth"))
        };

        // Each capability controller declares only the dependencies it actually uses, so arguments are
        // matched to the target constructor by type rather than assuming one shared arity.
        object[] pool =
        [
            _mediator,
            _tenantContext,
            _ownershipScopeResolver,
            _consumerAssembler,
            _endpointAssembler,
            _messageAssembler,
            _attemptAssembler,
        ];
        var constructor = typeof(TController).GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => pool.First(candidate => parameter.ParameterType.IsInstanceOfType(candidate)))
            .ToArray();
        var controller = (TController)constructor.Invoke(arguments);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private WebhookEndpointOperationsController CreateEndpointOperationsController(Guid actorUserId)
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(actorUserId);
        userContext.GetRequiredUserId().Returns(actorUserId);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(userContext)
                .BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", actorUserId.ToString("D"))],
                authenticationType: "TestAuth"))
        };

        return new WebhookEndpointOperationsController(_mediator)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static WebhookConsumerDetailLinkPolicy CreateConsumerDetailLinkPolicy(
        string providerMode,
        bool appPortalEnabled)
    {
        _ = providerMode;
        _ = appPortalEnabled;
        return new WebhookConsumerDetailLinkPolicy();
    }

    private WebhookConsumerDto CreateConsumerDto(
        string providerModeName = "Local",
        WebhookConsumerStatus status = WebhookConsumerStatus.Active) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = _tenantId,
            ConsumerKindId = 1,
            ConsumerKindCode = "TENANT",
            ConsumerKindName = "Tenant",
            StatusId = (int)status,
            StatusCode = status.ToString().ToUpperInvariant(),
            StatusName = status.ToString(),
            ProviderModeId = ProviderModeId(providerModeName),
            ProviderModeCode = providerModeName == "DryRun" ? "DRY_RUN" : providerModeName.ToUpperInvariant(),
            ProviderModeName = providerModeName,
            ProviderCapabilityAuthorityAvailable = true,
            CapabilityResolutionVersion = "controller-test-v1",
            ProviderCapabilities = [],
            Name = "Tenant automation",
            CreatedAt = DateTime.UtcNow
        };

    private WebhookEndpointDto CreateEndpointDto(
        int statusId = 1,
        string statusName = "Active",
        string providerModeName = "Local")
    {
        var eventTypeId = Guid.CreateVersion7();
        return new WebhookEndpointDto
        {
            Id = Guid.CreateVersion7(),
            TenantId = _tenantId,
            OwnerId = _tenantId,
            OwnerKindId = (int)WebhookConsumerKind.Tenant,
            OwnerKindCode = "TENANT",
            OwnerKindName = "Tenant",
            ConsumerId = Guid.CreateVersion7(),
            ConsumerName = "Tenant automation",
            ProviderModeId = ProviderModeId(providerModeName),
            ProviderModeCode = providerModeName == "DryRun" ? "DRY_RUN" : providerModeName.ToUpperInvariant(),
            ProviderModeName = providerModeName,
            DestinationHost = "integrator.example",
            Description = "Integrator endpoint",
            StatusId = statusId,
            StatusCode = statusName switch
            {
                "AutoPaused" => "AUTO_PAUSED",
                _ => statusName.ToUpperInvariant()
            },
            StatusName = statusName,
            SecretVersion = 1,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            RateLimitPerMinute = 60,
            CreatedAt = DateTime.UtcNow,
            Subscriptions =
            [
                new WebhookEndpointSubscriptionDto
                {
                    Id = Guid.CreateVersion7(),
                    EventTypeId = eventTypeId,
                    EventTypeName = "event.published",
                    EventTypeGroupName = "event",
                    IsEnabled = true,
                    CreatedAt = DateTime.UtcNow
                }
            ]
        };
    }

    private static int ProviderModeId(string providerModeName) =>
        providerModeName switch
        {
            "Disabled" => 1,
            "Local" => 2,
            "Svix" => 3,
            "Composite" => 4,
            "DryRun" => 5,
            _ => 2
        };

    private WebhookOwnershipScope CreateOwnershipScope(WebhookConsumerKind ownerKind, Guid ownerId) =>
        WebhookOwnershipScope.Create(
            ownerKind,
            ownerKind is WebhookConsumerKind.Instance ? null : _tenantId,
            ownerKind is WebhookConsumerKind.Instance ? ownerId : null,
            ownerKind is WebhookConsumerKind.Organization ? ownerId : null,
            ownerKind is WebhookConsumerKind.Group ? ownerId : null,
            ownerKind is WebhookConsumerKind.User ? ownerId : null);

    private WebhookMessageDto CreateMessageDto(
        DateTime? payloadRetentionUntil = null,
        DateTime? payloadClearedAt = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = _tenantId,
            OwnerKindId = (int)WebhookConsumerKind.Tenant,
            OwnerId = _tenantId,
            EventType = "event.published",
            EventId = Guid.CreateVersion7().ToString("D"),
            AggregateKind = "Event",
            AggregateId = Guid.CreateVersion7(),
            ConsumerId = Guid.CreateVersion7(),
            ConsumerName = "Tenant automation",
            PayloadHash = "sha256:ab3d5f2c4e8a",
            PayloadRetentionUntil = payloadRetentionUntil ?? DateTime.UtcNow.AddDays(14),
            PayloadClearedAt = payloadClearedAt,
            CreatedAt = DateTime.UtcNow
        };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private WebhookDeliveryAttemptDto CreateDeliveryAttemptDto(string statusName = "Failed") =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = _tenantId,
            OwnerKindId = (int)WebhookConsumerKind.Tenant,
            OwnerId = _tenantId,
            MessageId = Guid.CreateVersion7(),
            MessageEventType = "event.published",
            EndpointId = Guid.CreateVersion7(),
            EndpointStatusName = "Active",
            EndpointStatusCode = "ACTIVE",
            AttemptNumber = 1,
            OutcomeId = statusName == "Succeeded" ? 3 : 4,
            OutcomeCode = statusName == "Succeeded" ? "SUCCEEDED" : "FAILED",
            OutcomeName = statusName,
            ScheduledAt = DateTime.UtcNow.AddMinutes(-5),
            SentAt = DateTime.UtcNow.AddMinutes(-4),
            CompletedAt = DateTime.UtcNow.AddMinutes(-4),
            HttpStatusCode = statusName == "Succeeded" ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError,
            FailureCategory = statusName == "Succeeded" ? null : "server_error",
            DurationMs = 127,
            NextRetryAt = statusName == "Succeeded" ? null : DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        };
}
