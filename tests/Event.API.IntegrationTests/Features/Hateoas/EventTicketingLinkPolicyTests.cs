using System.Text.Json;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.DTOs.OrganizerPaymentConnections;
using Explore.Application.Hateoas;
using Explore.Application.Serialization;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class EventTicketingLinkPolicyTests
{
    [Test]
    public async Task EmptyCatalog_AdvertisesDraftCreationAgainstParentEvent()
    {
        Guid eventId = Guid.CreateVersion7();
        var dto = new EventTicketCatalogManagementDto { EventId = eventId };

        var link = new EventTicketCatalogManagementLinkPolicy()
            .GetLinks(dto, null)
            .Single(candidate => candidate.Rel == LinkRelations.CreateDraft);

        await Assert.That(link.RouteName).IsEqualTo(RouteNames.CreateEventTicketCatalogDraft);
        await Assert.That(link.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageTickets);
        await Assert.That(link.PermissionResourceId).IsEqualTo(eventId.ToString("D"));
    }

    [Test]
    public async Task DraftCatalog_AdvertisesPublishAndPerItemManagementActions()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid ticketTypeId = Guid.CreateVersion7();
        Guid poolId = Guid.CreateVersion7();
        var dto = new EventTicketCatalogManagementDto
        {
            EventId = eventId,
            CatalogId = Guid.CreateVersion7(),
            StatusId = (int)TicketCatalogStatusEnum.Draft,
            PublicationPreflight = new PaidEventPublicationPreflightDto { EventId = eventId, IsPaidCatalog = false, IsReady = true },
            TicketTypes = [new EventTicketTypeDto { Id = ticketTypeId }],
            CapacityPools = [new EventCapacityPoolDto { Id = poolId }]
        };
        var policy = new EventTicketCatalogManagementLinkPolicy();

        var rootLinks = policy.GetLinks(dto, null).ToList();
        var ticketLinks = policy.GetTicketTypeLinks(dto, dto.TicketTypes[0]).ToList();
        var poolLinks = policy.GetCapacityPoolLinks(dto, dto.CapacityPools[0]).ToList();

        await Assert.That(rootLinks.Any(link => link.Rel == LinkRelations.Publish)).IsTrue();
        await Assert.That(rootLinks.Any(link => link.Rel == LinkRelations.CreateTicketType)).IsTrue();
        await Assert.That(rootLinks.Any(link => link.Rel == LinkRelations.CreateCapacityPool)).IsTrue();
        await Assert.That(ticketLinks.Select(link => link.RouteName)).Contains(RouteNames.UpdateEventTicketType);
        await Assert.That(ticketLinks.Select(link => link.RouteName)).Contains(RouteNames.DeleteEventTicketType);
        await Assert.That(poolLinks.Select(link => link.RouteName)).Contains(RouteNames.UpdateEventCapacityPool);
        await Assert.That(poolLinks.Select(link => link.RouteName)).Contains(RouteNames.DeleteEventCapacityPool);
    }

    [Test]
    public async Task DraftCatalog_AlwaysAdvertisesPreflightAgainstManageTickets()
    {
        var dto = DraftCatalog(new PaidEventPublicationPreflightDto { EventId = EventId(), IsPaidCatalog = true, IsReady = false });

        LinkDefinition link = new EventTicketCatalogManagementLinkPolicy()
            .GetLinks(dto, null)
            .Single(candidate => candidate.Rel == LinkRelations.Preflight);

        await Assert.That(link.RouteName).IsEqualTo(RouteNames.GetPaidEventPublicationPreflight);
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageTickets);
    }

    [Test]
    public async Task DraftCatalog_PaidPublishIsOmittedUntilPreflightReady()
    {
        EventTicketCatalogManagementDto notReady = DraftCatalog(new PaidEventPublicationPreflightDto { EventId = EventId(), IsPaidCatalog = true, IsReady = false });
        EventTicketCatalogManagementDto ready = DraftCatalog(new PaidEventPublicationPreflightDto { EventId = EventId(), IsPaidCatalog = true, IsReady = true });
        var policy = new EventTicketCatalogManagementLinkPolicy();

        await Assert.That(policy.GetLinks(notReady, null).Any(link => link.Rel == LinkRelations.Publish)).IsFalse();
        LinkDefinition publish = policy.GetLinks(ready, null).Single(link => link.Rel == LinkRelations.Publish);
        await Assert.That(publish.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
    }

    [Test]
    public async Task DraftCatalog_FreePublishRemainsManageTickets()
    {
        EventTicketCatalogManagementDto dto = DraftCatalog(new PaidEventPublicationPreflightDto { EventId = EventId(), IsPaidCatalog = false, IsReady = true });

        LinkDefinition publish = new EventTicketCatalogManagementLinkPolicy()
            .GetLinks(dto, null)
            .Single(link => link.Rel == LinkRelations.Publish);

        await Assert.That(publish.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageTickets);
    }

    [Test]
    public async Task DraftCatalog_PaidCommerceRelsUsePaidCommerceAction()
    {
        EventTicketCatalogManagementDto dto = DraftCatalog(new PaidEventPublicationPreflightDto { EventId = EventId(), IsPaidCatalog = true, IsReady = true });

        var links = new EventTicketCatalogManagementLinkPolicy().GetLinks(dto, null).ToDictionary(link => link.Rel);

        await Assert.That(links[LinkRelations.CommercialDisclosures].PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
        await Assert.That(links[LinkRelations.PaymentConnection].PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
        await Assert.That(links[LinkRelations.StartOnboarding].PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
    }

    [Test]
    public async Task EmptyCatalog_AdvertisesPaymentConnectionButNotDirectOnboarding()
    {
        Guid eventId = EventId();
        var dto = new EventTicketCatalogManagementDto { EventId = eventId, TenantId = EventId(), ActorId = EventId(), OrganizerActorId = EventId(), OrganizerUserId = EventId() };

        var links = new EventTicketCatalogManagementLinkPolicy().GetLinks(dto, null).ToList();

        LinkDefinition payment = links.Single(link => link.Rel == LinkRelations.PaymentConnection);
        await Assert.That(payment.RouteName).IsEqualTo(RouteNames.GetEventOrganizerPaymentConnection);
        await Assert.That(payment.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
        await Assert.That(links.Any(link => link.Rel == LinkRelations.StartOnboarding)).IsFalse();
    }

    [Test]
    public async Task PaidPreflight_PublishOnlyWhenReadyAndUsesPaidCommerceForPaidCatalog()
    {
        Guid eventId = EventId();
        var notReady = new PaidEventPublicationPreflightDto { EventId = eventId, IsPaidCatalog = true, IsReady = false };
        var ready = new PaidEventPublicationPreflightDto { EventId = eventId, IsPaidCatalog = true, IsReady = true };
        var policy = new PaidEventPublicationPreflightLinkPolicy();

        await Assert.That(policy.GetLinks(notReady, null).Any(link => link.Rel == LinkRelations.Publish)).IsFalse();
        LinkDefinition publish = policy.GetLinks(ready, null).Single(link => link.Rel == LinkRelations.Publish);
        await Assert.That(publish.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
    }

    [Test]
    public async Task DraftCatalog_FreePublishUsesTrustedAttrsAndTenantScope()
    {
        Guid eventId = EventId();
        Guid tenantId = EventId();
        Guid actorId = EventId();
        Guid actorUserId = EventId();
        Guid organizerActorId = EventId();
        Guid organizerOrganizationId = EventId();

        var dto = new PaidEventPublicationPreflightDto
        {
            EventId = eventId,
            TenantId = tenantId,
            ActorId = actorId,
            ActorUserId = actorUserId,
            OrganizerActorId = organizerActorId,
            OrganizerOrganizationId = organizerOrganizationId,
            IsPaidCatalog = false,
            IsReady = true
        };

        LinkDefinition publish = new PaidEventPublicationPreflightLinkPolicy().GetLinks(dto, null).Single(link => link.Rel == LinkRelations.Publish);

        await Assert.That(publish.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageTickets);
        await Assert.That(publish.PermissionScope!.TenantId).IsEqualTo(tenantId.ToString("D"));
        await Assert.That(publish.PermissionFacts).IsEqualTo(new EventAuthorizationFacts(
            tenantId,
            eventId,
            actorId,
            UserId: actorUserId,
            OrganizationId: null,
            GroupId: null,
            OrganizerActorId: organizerActorId,
            OrganizerUserId: null,
            OrganizerOrganizationId: organizerOrganizationId,
            OrganizerGroupId: null,
            ProvenanceType: null,
            SubmittedByUserId: null));
    }

    [Test]
    public async Task PaidPreflight_PublishUsesTrustedAttrsAndTenantScope()
    {
        Guid eventId = EventId();
        Guid tenantId = EventId();
        Guid actorId = EventId();
        Guid actorOrganizationId = EventId();
        Guid organizerActorId = EventId();
        Guid organizerGroupId = EventId();

        var dto = new PaidEventPublicationPreflightDto
        {
            EventId = eventId,
            TenantId = tenantId,
            ActorId = actorId,
            ActorOrganizationId = actorOrganizationId,
            OrganizerActorId = organizerActorId,
            OrganizerGroupId = organizerGroupId,
            IsPaidCatalog = true,
            IsReady = true
        };

        LinkDefinition publish = new PaidEventPublicationPreflightLinkPolicy().GetLinks(dto, null).Single(link => link.Rel == LinkRelations.Publish);

        await Assert.That(publish.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
        await Assert.That(publish.PermissionScope!.TenantId).IsEqualTo(tenantId.ToString("D"));
        await Assert.That(publish.PermissionFacts).IsEqualTo(new EventAuthorizationFacts(
            tenantId,
            eventId,
            actorId,
            UserId: null,
            OrganizationId: actorOrganizationId,
            GroupId: null,
            OrganizerActorId: organizerActorId,
            OrganizerUserId: null,
            OrganizerOrganizationId: null,
            OrganizerGroupId: organizerGroupId,
            ProvenanceType: null,
            SubmittedByUserId: null));
    }

    [Test]
    public async Task PaidPreflight_HiddenTrustedFieldsAreNotSerialized()
    {
        var dto = new PaidEventPublicationPreflightDto
        {
            EventId = EventId(),
            CatalogId = EventId(),
            TenantId = EventId(),
            ActorId = EventId(),
            ActorUserId = EventId(),
            ActorOrganizationId = EventId(),
            ActorGroupId = EventId(),
            OrganizerActorId = EventId(),
            OrganizerUserId = EventId(),
            OrganizerOrganizationId = EventId(),
            OrganizerGroupId = EventId(),
            IsPaidCatalog = true,
            IsReady = true
        };

        string json = JsonSerializer.Serialize(dto, ExploreJsonContext.Default.PaidEventPublicationPreflightDto);

        await Assert.That(json.Contains("\"eventId\"")).IsTrue();
        await Assert.That(json.Contains("\"tenantId\"")).IsFalse();
        await Assert.That(json.Contains("\"actorId\"")).IsFalse();
        await Assert.That(json.Contains("\"organizerActorId\"")).IsFalse();
    }

    [Test]
    public async Task OrganizerPaymentConnectionManagement_ExposesSelfAndOnboarding()
    {
        Guid eventId = EventId();
        var dto = new EventOrganizerPaymentConnectionManagementDto
        {
            EventId = eventId,
            TenantId = EventId(),
            ActorId = EventId(),
            OrganizerActorId = EventId(),
            OrganizerUserId = EventId()
        };

        var links = new OrganizerPaymentConnectionLinkPolicy().GetLinks(dto, null).ToDictionary(link => link.Rel);

        await Assert.That(links[LinkRelations.Self].RouteName).IsEqualTo(RouteNames.GetEventOrganizerPaymentConnection);
        await Assert.That(links[LinkRelations.StartOnboarding].PermissionAction).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
    }

    [Test]
    public async Task PublishedCatalog_OnlyAdvertisesDraftClone()
    {
        var dto = new EventTicketCatalogManagementDto
        {
            EventId = Guid.CreateVersion7(),
            CatalogId = Guid.CreateVersion7(),
            StatusId = (int)TicketCatalogStatusEnum.Published,
            TicketTypes = [new EventTicketTypeDto { Id = Guid.CreateVersion7() }]
        };
        var policy = new EventTicketCatalogManagementLinkPolicy();

        var rootLinks = policy.GetLinks(dto, null).ToList();
        var ticketLinks = policy.GetTicketTypeLinks(dto, dto.TicketTypes[0]).ToList();

        await Assert.That(rootLinks.Any(link => link.RouteName == RouteNames.CloneEventTicketCatalogDraft)).IsTrue();
        await Assert.That(rootLinks.Any(link => link.Rel == LinkRelations.Publish)).IsFalse();
        await Assert.That(ticketLinks).IsEmpty();
    }

    private static EventTicketCatalogManagementDto DraftCatalog(PaidEventPublicationPreflightDto preflight)
    {
        Guid eventId = preflight.EventId;
        return new EventTicketCatalogManagementDto
        {
            EventId = eventId,
            TenantId = EventId(),
            ActorId = EventId(),
            OrganizerActorId = EventId(),
            OrganizerUserId = EventId(),
            CatalogId = Guid.CreateVersion7(),
            StatusId = (int)TicketCatalogStatusEnum.Draft,
            PublicationPreflight = preflight
        };
    }

    private static Guid EventId() => Guid.CreateVersion7();
}
