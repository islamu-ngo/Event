using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.API.Models;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Promotions;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.Promotions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventPromotionsControllerTests
{
    [Test]
    public async Task PromotionManagementRoutes_ArePrivateAndTransactionalWhereMutating()
    {
        await AssertRoute<HttpGetAttribute>(nameof(EventPromotionsController.List), string.Empty, RouteNames.GetEventPromotions, false);
        await AssertRoute<HttpGetAttribute>(nameof(EventPromotionsController.Get), "{promotionDefinitionId:guid}", RouteNames.GetEventPromotion, false);
        await AssertRoute<HttpPostAttribute>(nameof(EventPromotionsController.CreateDraft), string.Empty, RouteNames.CreateEventPromotionDraft, true);
        await AssertRoute<HttpPutAttribute>(nameof(EventPromotionsController.Revise), "{promotionDefinitionId:guid}", RouteNames.ReviseEventPromotion, true);
        await AssertRoute<HttpPostAttribute>(nameof(EventPromotionsController.Publish), "{promotionDefinitionId:guid}/publish", RouteNames.PublishEventPromotion, true);
        await AssertRoute<HttpPostAttribute>(nameof(EventPromotionsController.Revoke), "{promotionDefinitionId:guid}/revoke", RouteNames.RevokeEventPromotion, true);
        await AssertRoute<HttpPostAttribute>(nameof(EventPromotionsController.RotateCode), "{promotionDefinitionId:guid}/code:rotate", RouteNames.RotateEventPromotionCode, true);
    }

    [Test]
    public async Task PromotionManagementActions_DispatchRouteOwnedCqrsRequests()
    {
        var mediator = Substitute.For<IMediator>();
        var issuedCodeMediator = Substitute.For<IMediator>();
        var eventId = Guid.CreateVersion7();
        var promotionId = Guid.CreateVersion7();
        var catalogId = Guid.CreateVersion7();
        mediator.Send(Arg.Any<ListPromotionManagementQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        mediator.Send(Arg.Any<GetPromotionManagementQuery>(), Arg.Any<CancellationToken>()).Returns(CreatePromotion(eventId, promotionId, catalogId));
        mediator.Send<PromotionManagementCommandResponseDto>(Arg.Any<RevisePromotionCommand>(), Arg.Any<CancellationToken>()).Returns(Success(promotionId));
        mediator.Send<PromotionManagementCommandResponseDto>(Arg.Any<PublishPromotionCommand>(), Arg.Any<CancellationToken>()).Returns(Success(promotionId));
        mediator.Send<PromotionManagementCommandResponseDto>(Arg.Any<RevokePromotionCommand>(), Arg.Any<CancellationToken>()).Returns(Success(promotionId));
        issuedCodeMediator.Send<PromotionCodeIssuedCommandResponseDto>(Arg.Any<CreatePromotionDraftCommand>(), Arg.Any<CancellationToken>()).Returns(IssuedSuccess(promotionId));
        issuedCodeMediator.Send<PromotionCodeIssuedCommandResponseDto>(Arg.Any<RotatePromotionCodeCommand>(), Arg.Any<CancellationToken>()).Returns(IssuedSuccess(promotionId));
        var controller = CreateController(mediator);
        var issuedCodeController = CreateController(issuedCodeMediator);
        var create = new CreatePromotionDraftRequest(catalogId, "Launch", "SAVE10", "fixed", 100, null, null, Utc(0), Utc(7), 10, 1, []);
        var revise = new RevisePromotionRequest("Launch", "fixed", 100, null, null, Utc(0), Utc(7), 10, 1, []);

        await controller.List(eventId, catalogId);
        await controller.Get(eventId, promotionId);
        await issuedCodeController.CreateDraft(eventId, create, idempotencyKey: Guid.CreateVersion7().ToString("N"));
        await controller.Revise(eventId, promotionId, revise, idempotencyKey: Guid.CreateVersion7().ToString("N"));
        await controller.Publish(eventId, promotionId, new PromotionCodeRequest("SAVE10"), idempotencyKey: Guid.CreateVersion7().ToString("N"));
        await controller.Revoke(eventId, promotionId, new RevokePromotionRequest(), idempotencyKey: Guid.CreateVersion7().ToString("N"));
        await issuedCodeController.RotateCode(eventId, promotionId, new PromotionCodeRequest("SAVE20"), idempotencyKey: Guid.CreateVersion7().ToString("N"));

        _ = mediator.Received(1).Send(Arg.Is<ListPromotionManagementQuery>(query => query.EventId == eventId && query.TicketCatalogVersionId == catalogId), Arg.Any<CancellationToken>());
        _ = mediator.Received(1).Send(Arg.Is<GetPromotionManagementQuery>(query => query.EventId == eventId && query.PromotionDefinitionId == promotionId), Arg.Any<CancellationToken>());
        _ = issuedCodeMediator.Received(1).Send(Arg.Is<CreatePromotionDraftCommand>(command => command.EventId == eventId && command.TicketCatalogVersionId == catalogId && command.Code == "SAVE10"), Arg.Any<CancellationToken>());
        _ = mediator.Received(1).Send(Arg.Is<RevisePromotionCommand>(command => command.EventId == eventId && command.PromotionDefinitionId == promotionId), Arg.Any<CancellationToken>());
        _ = mediator.Received(1).Send(Arg.Is<PublishPromotionCommand>(command => command.Code == "SAVE10"), Arg.Any<CancellationToken>());
        _ = mediator.Received(1).Send(Arg.Is<RevokePromotionCommand>(command => command.EventId == eventId && command.PromotionDefinitionId == promotionId), Arg.Any<CancellationToken>());
        _ = issuedCodeMediator.Received(1).Send(Arg.Is<RotatePromotionCodeCommand>(command => command.Code == "SAVE20"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PromotionManagementActions_AdvertiseIssuedCodeOnlyForCreateAndRotate()
    {
        await AssertCommandResponseShape(
            nameof(EventPromotionsController.CreateDraft),
            typeof(PromotionCodeIssuedCommandResponseDto));
        await AssertCommandResponseShape(
            nameof(EventPromotionsController.RotateCode),
            typeof(PromotionCodeIssuedCommandResponseDto));

        await AssertCommandResponseShape(
            nameof(EventPromotionsController.Revise),
            typeof(PromotionManagementCommandResponseDto));
        await AssertCommandResponseShape(
            nameof(EventPromotionsController.Publish),
            typeof(PromotionManagementCommandResponseDto));
        await AssertCommandResponseShape(
            nameof(EventPromotionsController.Revoke),
            typeof(PromotionManagementCommandResponseDto));

        await Assert.That(typeof(PromotionManagementCommandResponseDto).GetProperty("IssuedCode")).IsNull();
        await Assert.That(typeof(PromotionCodeIssuedCommandResponseDto).GetProperty("IssuedCode")).IsNotNull();
    }

    [Test]
    public async Task PromotionHalLinks_UsePaidCommerceAuthorityStateAndNoSecretFields()
    {
        var draft = CreatePromotion(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()) with { StatusId = (int)PromotionDefinitionStatusEnum.Draft };
        var published = CreatePromotion(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()) with { StatusId = (int)PromotionDefinitionStatusEnum.Published };

        LinkDefinition[] draftLinks = new PromotionManagementLinkPolicy().GetLinks(draft, null).ToArray();
        LinkDefinition[] publishedLinks = new PromotionManagementLinkPolicy().GetLinks(published, null).ToArray();
        string serializedDraft = JsonSerializer.Serialize(draft);

        await Assert.That(draftLinks.Select(link => link.Rel)).Contains(LinkRelations.Publish);
        await Assert.That(draftLinks.Select(link => link.Rel)).DoesNotContain(LinkRelations.RevisePromotion);
        await Assert.That(draftLinks.Select(link => link.Rel)).DoesNotContain(LinkRelations.Revoke);
        await Assert.That(publishedLinks.Select(link => link.Rel)).Contains(LinkRelations.RevisePromotion);
        await Assert.That(publishedLinks.Select(link => link.Rel)).Contains(LinkRelations.Revoke);
        await Assert.That(publishedLinks.Select(link => link.Rel)).Contains(LinkRelations.RotatePromotionCode);
        await Assert.That(serializedDraft).DoesNotContain(draft.TenantId.ToString("D"));
        await Assert.That(serializedDraft).DoesNotContain(draft.OrganizerUserId!.Value.ToString("D"));
        foreach (LinkDefinition link in draftLinks.Concat(publishedLinks))
        {
            await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
            await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
            // Promotion secrets are never policy inputs. The closed fact record makes that structural: the
            // link may only publish event authority, so a code or digest has nowhere to leak into.
            await Assert.That(link.PermissionFacts).IsTypeOf<EventAuthorizationFacts>();
        }
    }

    [Test]
    public async Task PromotionCollectionHal_AdvertisesCreateOnlyWithPaidCommerceAuthorityContext()
    {
        var eventId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var catalogId = Guid.CreateVersion7();
        var context = new PromotionCollectionAuthorizationContext(eventId, catalogId, tenantId);

        LinkDefinition[] links = new PromotionManagementCollectionLinkPolicy().GetCollectionLinks(null, context).ToArray();

        await Assert.That(links.Select(link => link.Rel)).IsEquivalentTo([LinkRelations.CreatePromotion]);
        LinkDefinition create = links.Single();
        await Assert.That(create.RouteName).IsEqualTo(RouteNames.CreateEventPromotionDraft);
        await Assert.That(create.RouteValues).IsEqualTo(context);
        await Assert.That(create.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(create.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
        await Assert.That(create.PermissionResourceId).IsEqualTo(eventId.ToString("D"));
        // The catalog version scopes which promotions are listed; authority stays on the parent event.
        await Assert.That(create.PermissionFacts)
            .IsEqualTo(new EventScopedAuthorizationFacts(tenantId, eventId));
        await Assert.That(create.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString("D"));
    }

    /// <summary>
    /// Fail-closed omission: with no trusted collection owner resolved server-side, the create affordance
    /// must not be advertised. Exercised through the context-aware overload the assembler actually calls,
    /// passing a null context — the parameterless overload is a contract default that carries no owner and
    /// so could never authorize a create in the first place.
    /// </summary>
    [Test]
    public async Task PromotionCollectionHal_WithoutAuthorityContextDoesNotAdvertiseCreate()
    {
        ICollectionLinkPolicy<PromotionManagementDto> policy = new PromotionManagementCollectionLinkPolicy();

        LinkDefinition[] links = policy.GetCollectionLinks(null, authorizationContext: null).ToArray();

        await Assert.That(links).IsEmpty();
    }

    [Test]
    public async Task PromotionAuthorization_ExactOrganizerAllowedContributorAdminAndMachineDenied()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var organizerUserId = Guid.CreateVersion7();
        var contributorUserId = Guid.CreateVersion7();
        var unrelatedAdminUserId = Guid.CreateVersion7();
        var organizerOrganizationId = Guid.CreateVersion7();
        var admin = Substitute.For<IAdminContext>();
        var machine = Substitute.For<IMachinePrincipalAccessor>();
        var organizationMembers = Substitute.For<IOrganizationMemberRepository>();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        machine.IsMachineCaller.Returns(false);
        machine.Current.Returns((ApiKeyPrincipalContext?)null);
        admin.UserId.Returns(organizerUserId);
        admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        admin.IsTenantAdminAsync(tenantId, Arg.Any<CancellationToken>()).Returns(false);
        var authorization = new FallbackAuthorizationService(
            admin,
            machine,
            Substitute.For<IEventAuthoritySnapshotService>(),
            organizationMembers,
            Substitute.For<IGroupMemberRepository>(),
            Substitute.For<IHierarchicalSettingsResolver>(),
            tenant,
            Substitute.For<ILogger<FallbackAuthorizationService>>());
        EventAuthorizationFacts organizer = PromotionAuthorizationFacts(tenantId, eventId, organizerUserId);
        var contributor = organizer with
        {
            OrganizerUserId = null,
            OrganizerOrganizationId = organizerOrganizationId
        };

        bool organizerAllowed = (await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            eventId.ToString("D"),
            AuthorizationActions.Events.ManagePaidEventCommerce,
            Facts: organizer))).IsAllowed;

        admin.UserId.Returns(contributorUserId);
        bool contributorAllowed = (await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            eventId.ToString("D"),
            AuthorizationActions.Events.ManagePaidEventCommerce,
            Facts: contributor))).IsAllowed;

        admin.UserId.Returns(unrelatedAdminUserId);
        admin.IsTenantAdminAsync(tenantId, Arg.Any<CancellationToken>()).Returns(true);
        bool adminAllowed = (await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            eventId.ToString("D"),
            AuthorizationActions.Events.ManagePaidEventCommerce,
            Facts: organizer))).IsAllowed;

        machine.IsMachineCaller.Returns(true);
        machine.Current.Returns(new ApiKeyPrincipalContext(
            $"promotion-{Guid.CreateVersion7():N}",
            tenantId,
            ExternalApiKeyOwnerType.User,
            organizerUserId,
            [ExternalApiKeyScopes.EventsWrite]));
        bool machineAllowed = (await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            eventId.ToString("D"),
            AuthorizationActions.Events.ManagePaidEventCommerce,
            Facts: organizer))).IsAllowed;

        await Assert.That(new[] { organizerAllowed, contributorAllowed, adminAllowed, machineAllowed })
            .IsEquivalentTo([true, false, false, false]);
        await organizationMembers.Received(1).HasPermissionInOrganization(
            organizerOrganizationId,
            contributorUserId,
            PermissionCodes.EventManageFinance);
    }

    private static EventPromotionsController CreateController(IMediator mediator)
    {
        var assembler = Substitute.For<IResourceAssembler<PromotionManagementDto, PromotionManagementDto>>();
        assembler.ToResource(Arg.Any<PromotionManagementDto>(), Arg.Any<HttpContext>())
            .Returns(call => new HalResource<PromotionManagementDto>((PromotionManagementDto)call[0]!));
        assembler.ToCollectionResource(Arg.Any<IEnumerable<PromotionManagementDto>>(), Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<HttpContext>())
            .Returns(new HalCollectionResource<PromotionManagementDto>());
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.CreateVersion7());
        var controller = new EventPromotionsController(mediator, tenantContext, assembler)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var url = Substitute.For<IUrlHelper>();
        url.Link(Arg.Any<string>(), Arg.Any<object>()).Returns(call => $"/api/routes/{call.ArgAt<string>(0)}");
        controller.Url = url;
        return controller;
    }

    private static PromotionManagementDto CreatePromotion(Guid eventId, Guid promotionId, Guid catalogId) => new()
    {
        EventId = eventId,
        DefinitionId = promotionId,
        DefinitionGroupId = Guid.CreateVersion7(),
        TicketCatalogVersionId = catalogId,
        TenantId = Guid.CreateVersion7(),
        ActorId = Guid.CreateVersion7(),
        OrganizerActorId = Guid.CreateVersion7(),
        OrganizerUserId = Guid.CreateVersion7(),
        StatusId = (int)PromotionDefinitionStatusEnum.Draft,
        StatusCode = "DRAFT",
        StatusName = "Draft",
        DisplayLabel = "Launch",
        CurrencyCode = "USD",
        DiscountKind = "fixed"
    };

    private static EventAuthorizationFacts PromotionAuthorizationFacts(
        Guid tenantId,
        Guid eventId,
        Guid organizerUserId) => new(
        tenantId,
        eventId,
        ActorId: Guid.CreateVersion7(),
        UserId: null,
        OrganizationId: null,
        GroupId: null,
        OrganizerActorId: Guid.CreateVersion7(),
        OrganizerUserId: organizerUserId,
        OrganizerOrganizationId: null,
        OrganizerGroupId: null,
        ProvenanceType: null,
        SubmittedByUserId: null);

    private static PromotionManagementCommandResponseDto Success(Guid id) =>
        PromotionManagementCommandResponseDto.Success(id, null, null);

    private static PromotionCodeIssuedCommandResponseDto IssuedSuccess(Guid id) =>
        PromotionCodeIssuedCommandResponseDto.Success(id, null, null, null);

    private static async Task AssertCommandResponseShape(string actionName, Type expectedResponseType)
    {
        MethodInfo action = typeof(EventPromotionsController).GetMethod(actionName)!;
        Type actionResultType = action.ReturnType.GenericTypeArguments.Single().GenericTypeArguments.Single();
        Type producesType = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode is StatusCodes.Status200OK or StatusCodes.Status201Created)
            .Type!;

        await Assert.That(actionResultType).IsEqualTo(expectedResponseType);
        await Assert.That(producesType).IsEqualTo(expectedResponseType);
    }

    private static DateTime Utc(int days) => new(2026, 8, 15 + days, 0, 0, 0, DateTimeKind.Utc);

    private static async Task AssertRoute<TAttribute>(string actionName, string template, string routeName, bool mutating)
        where TAttribute : HttpMethodAttribute
    {
        MethodInfo action = typeof(EventPromotionsController).GetMethod(actionName)!;
        var route = action.GetCustomAttribute<TAttribute>();
        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(action.GetCustomAttribute<PrivateNoStoreAttribute>() is not null).IsEqualTo(!mutating);
        await Assert.That(action.GetCustomAttribute<RequireIdempotencyKeyAttribute>() is not null).IsEqualTo(mutating);
        if (mutating)
        {
            await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        }
        await Assert.That(typeof(EventPromotionsController).GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(typeof(EventPromotionsController).GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Authenticated);
    }
}
