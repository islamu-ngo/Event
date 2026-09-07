using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Promotions;

public static class PromotionManagementMapper
{
    public static PromotionManagementDto Map(PromotionManagementEntry entry, Event eventTarget) => Map(entry.Definition, entry.ActiveCode, eventTarget);

    public static PromotionManagementDto Map(PromotionDefinition definition, PromotionCode? activeCode, Event eventTarget)
    {
        PromotionDefinitionStatusEnum status = (PromotionDefinitionStatusEnum)definition.PromotionDefinitionStatusId;
        return new PromotionManagementDto
        {
            TenantId = eventTarget.TenantId,
            EventId = definition.ScopeMetadata.EventId,
            ActorId = eventTarget.ActorId,
            ActorUserId = eventTarget.Actor?.UserId,
            ActorOrganizationId = eventTarget.Actor?.OrganizationId,
            ActorGroupId = eventTarget.Actor?.GroupId,
            OrganizerActorId = eventTarget.OrganizerActorId,
            OrganizerUserId = eventTarget.OrganizerActor?.UserId,
            OrganizerOrganizationId = eventTarget.OrganizerActor?.OrganizationId,
            OrganizerGroupId = eventTarget.OrganizerActor?.GroupId,
            TicketCatalogVersionId = definition.ScopeMetadata.TicketCatalogVersionId,
            TicketCatalogVersionNumber = definition.ScopeMetadata.TicketCatalogVersionNumber,
            CurrencyCode = definition.ScopeMetadata.CurrencyCode,
            DefinitionId = definition.Id,
            DefinitionGroupId = definition.DefinitionGroupId,
            VersionNumber = definition.VersionNumber,
            StatusId = definition.PromotionDefinitionStatusId,
            StatusCode = status.ToString().ToLowerInvariant(),
            StatusName = status.ToString(),
            DisplayLabel = definition.DisplayLabel,
            StartsAtUtc = definition.StartsAtUtc,
            EndsAtUtc = definition.EndsAtUtc,
            TotalRedemptionLimit = definition.TotalRedemptionLimit,
            PerVerifiedPurchaserLimit = definition.PerVerifiedPurchaserLimit,
            DiscountKind = definition.DiscountRule.FixedDiscountMinor is not null ? "fixed" : "basis_points",
            FixedDiscountMinor = definition.DiscountRule.FixedDiscountMinor,
            BasisPointDiscount = definition.DiscountRule.BasisPointDiscount,
            MaximumDiscountMinor = definition.DiscountRule.MaximumDiscountMinor,
            IncludesAllTickets = definition.Eligibility.IncludesAllTickets,
            EligibleTicketTypeIds = definition.Eligibility.EligibleTicketTypeIds.ToArray(),
            PromotionCodeDisplayLabel = activeCode?.DisplayLabel
        };
    }
}
