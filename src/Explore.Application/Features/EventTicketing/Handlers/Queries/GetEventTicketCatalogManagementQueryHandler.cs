using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Features.EventTicketing.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventTicketing.Handlers.Queries;

public sealed class GetEventTicketCatalogManagementQueryHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    ITenantContext tenant,
    Services.PaidEventPublicationPreflightService paidPreflight) : IRequestHandler<GetEventTicketCatalogManagementQuery, EventTicketCatalogManagementDto?>
{
    public async Task<EventTicketCatalogManagementDto?> Handle(
        GetEventTicketCatalogManagementQuery request,
        CancellationToken cancellationToken)
    {
        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return null;
        }

        EventTicketCatalogVersion? catalog =
            await catalogs.GetManagementCatalogAsync(request.EventId, tenant.TenantId, cancellationToken)
            ?? await catalogs.GetPublishedCatalogAsync(request.EventId, tenant.TenantId, cancellationToken);

        if (catalog is null)
        {
            return CreateBaseDto(eventTarget);
        }

        Event? eventWithDetails = await events.GetEventWithDetails(request.EventId);
        EventCapacityPool[] pools = eventWithDetails?.CapacityPools.Where(pool => !pool.IsDeleted).ToArray() ?? [];
        PaidEventPublicationPreflightDto publicationPreflight = await paidPreflight.AssessAsync(request.EventId, eventWithDetails, catalog, cancellationToken);
        return Map(catalog, pools, eventTarget, publicationPreflight);
    }

    private static bool IsPlatformManaged(Event? eventTarget, Guid tenantId) =>
        eventTarget?.TenantId == tenantId
        && eventTarget.ParticipationConfiguration?.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged;

    private static EventTicketCatalogManagementDto Map(
        EventTicketCatalogVersion catalog,
        IReadOnlyList<EventCapacityPool> pools,
        Event eventTarget,
        PaidEventPublicationPreflightDto publicationPreflight) =>
        new()
        {
            TenantId = eventTarget.TenantId,
            EventId = catalog.EventId,
            ActorId = eventTarget.ActorId,
            ActorUserId = eventTarget.Actor?.UserId,
            ActorOrganizationId = eventTarget.Actor?.OrganizationId,
            ActorGroupId = eventTarget.Actor?.GroupId,
            OrganizerActorId = eventTarget.OrganizerActorId,
            OrganizerUserId = eventTarget.OrganizerActor?.UserId,
            OrganizerOrganizationId = eventTarget.OrganizerActor?.OrganizationId,
            OrganizerGroupId = eventTarget.OrganizerActor?.GroupId,
            CatalogId = catalog.Id,
            VersionNumber = catalog.VersionNumber,
            CurrencyCode = catalog.CurrencyCode,
            StatusId = catalog.TicketCatalogStatusId,
            StatusCode = catalog.TicketCatalogStatus?.MasterCode,
            StatusName = catalog.TicketCatalogStatus?.FullName,
            MerchantDisclosureText = catalog.MerchantDisclosureText,
            RefundPolicyDisclosureText = catalog.RefundPolicyDisclosureText,
            SupportContactDisclosureText = catalog.SupportContactDisclosureText,
            PublicationPreflight = publicationPreflight,
            TicketTypes = catalog.TicketTypes
                .Where(ticketType => !ticketType.IsDeleted)
                .Select(Map)
                .ToArray(),
            CapacityPools = pools.Select(Map).ToArray()
        };

    private static EventTicketCatalogManagementDto CreateBaseDto(Event eventTarget) => new()
    {
        TenantId = eventTarget.TenantId,
        EventId = eventTarget.Id,
        ActorId = eventTarget.ActorId,
        ActorUserId = eventTarget.Actor?.UserId,
        ActorOrganizationId = eventTarget.Actor?.OrganizationId,
        ActorGroupId = eventTarget.Actor?.GroupId,
        OrganizerActorId = eventTarget.OrganizerActorId,
        OrganizerUserId = eventTarget.OrganizerActor?.UserId,
        OrganizerOrganizationId = eventTarget.OrganizerActor?.OrganizationId,
        OrganizerGroupId = eventTarget.OrganizerActor?.GroupId
    };

    private static EventTicketTypeDto Map(EventTicketType ticketType) => new()
    {
        Id = ticketType.Id,
        Name = ticketType.Name,
        TicketPricingModeId = ticketType.TicketPricingModeId,
        TicketPricingModeCode = ticketType.TicketPricingMode?.MasterCode,
        TicketPricingModeName = ticketType.TicketPricingMode?.FullName,
        FixedPriceMinor = ticketType.FixedPriceMinor,
        MinimumPriceMinor = ticketType.MinimumPriceMinor,
        SuggestedPriceMinor = ticketType.SuggestedPriceMinor,
        ParticipantDataCollectionModeId = ticketType.ParticipantDataCollectionModeId,
        ParticipantDataCollectionModeCode = ticketType.ParticipantDataCollectionMode?.MasterCode,
        ParticipantDataCollectionModeName = ticketType.ParticipantDataCollectionMode?.FullName,
        CapacityPoolId = ticketType.CapacityPoolId,
        MinimumAge = ticketType.MinimumAge,
        MaximumAge = ticketType.MaximumAge,
        RequiresGuardian = ticketType.RequiresGuardian,
        RequiresApproval = ticketType.RequiresApproval,
        PerOrderLimit = ticketType.PerOrderLimit,
        PerAccountLimit = ticketType.PerAccountLimit,
        PerVerifiedContactLimit = ticketType.PerVerifiedContactLimit,
        PerBookingPartyLimit = ticketType.PerBookingPartyLimit,
        Entitlements = ticketType.Entitlements.Select(entitlement => new TicketTypeEntitlementDto
        {
            EntitlementScopeTypeId = entitlement.EntitlementScopeTypeId,
            EntitlementScopeTypeCode = entitlement.EntitlementScopeType?.MasterCode,
            EntitlementScopeTypeName = entitlement.EntitlementScopeType?.FullName,
            EventDayId = entitlement.EventDayId,
            EventSessionId = entitlement.EventSessionId,
            IncludedQuantity = entitlement.IncludedQuantity,
            EntitlementSelectionRuleId = entitlement.EntitlementSelectionRuleId,
            EntitlementSelectionRuleCode = entitlement.EntitlementSelectionRule?.MasterCode,
            EntitlementSelectionRuleName = entitlement.EntitlementSelectionRule?.FullName
        }).ToArray()
    };

    private static EventCapacityPoolDto Map(EventCapacityPool pool) => new()
    {
        Id = pool.Id,
        Name = pool.Name,
        MaximumQuantity = pool.MaximumQuantity,
        HoldDurationSeconds = pool.HoldDurationSeconds,
        CapacityHoldPolicyId = pool.CapacityHoldPolicyId,
        CapacityHoldPolicyCode = pool.CapacityHoldPolicy?.MasterCode,
        CapacityHoldPolicyName = pool.CapacityHoldPolicy?.FullName,
        CapacityOversellPolicyId = pool.CapacityOversellPolicyId,
        CapacityOversellPolicyCode = pool.CapacityOversellPolicy?.MasterCode,
        CapacityOversellPolicyName = pool.CapacityOversellPolicy?.FullName,
        IsActive = pool.IsActive
    };
}
