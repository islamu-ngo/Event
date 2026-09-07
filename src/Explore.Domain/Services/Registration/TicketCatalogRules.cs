using Explore.Domain.Enums;

namespace Explore.Domain.Services.Registration;

public static class TicketCatalogRules
{
    public static void ValidateForPublication(EventTicketCatalogVersion catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        EventTicketType[] liveTicketTypes = catalog.TicketTypes
            .Where(ticketType => !ticketType.IsDeleted)
            .ToArray();
        if (liveTicketTypes.Length == 0)
        {
            throw new InvalidOperationException("A published ticket catalog requires at least one ticket type.");
        }

        if (liveTicketTypes.Any(static ticketType => ticketType.TicketPricingModeId != (int)TicketPricingModeEnum.Free)
            && (string.IsNullOrWhiteSpace(catalog.MerchantDisclosureText)
                || string.IsNullOrWhiteSpace(catalog.RefundPolicyDisclosureText)
                || string.IsNullOrWhiteSpace(catalog.SupportContactDisclosureText)))
        {
            throw new InvalidOperationException("Paid ticket catalogs require merchant, refund, and support disclosures before publication.");
        }

        foreach (EventTicketType ticketType in liveTicketTypes)
        {
            if (!string.Equals(ticketType.CurrencyCode, catalog.CurrencyCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("All ticket types in a catalog must use one currency.");
            }

            TicketPricingRules.ValidateConfiguration(
                (TicketPricingModeEnum)ticketType.TicketPricingModeId,
                ticketType.CurrencyCode,
                ticketType.FixedPriceMinor,
                ticketType.MinimumPriceMinor,
                ticketType.SuggestedPriceMinor);

            if (ticketType.Entitlements.Count == 0)
            {
                throw new InvalidOperationException("Each ticket type requires at least one entitlement.");
            }

            foreach (TicketTypeEntitlement entitlement in ticketType.Entitlements)
            {
                ValidateEntitlement(catalog, ticketType, entitlement);
            }
        }
    }

    public static void ValidateCapacityPool(EventTicketCatalogVersion catalog, EventCapacityPool? capacityPool)
    {
        if (capacityPool is not null && (capacityPool.EventId != catalog.EventId || capacityPool.TenantId != catalog.TenantId))
        {
            throw new ArgumentException("A ticket type can use only a capacity pool owned by the same event and tenant.", nameof(capacityPool));
        }
    }

    public static void ValidateEntitlement(
        EventTicketCatalogVersion catalog,
        EventTicketType ticketType,
        TicketTypeEntitlement entitlement)
    {
        if (ticketType.CatalogId != catalog.Id || entitlement.TicketTypeId != ticketType.Id || entitlement.TenantId != catalog.TenantId || entitlement.TargetEventId != catalog.EventId)
        {
            throw new ArgumentException("Ticket entitlements must belong to their ticket type, catalog tenant, and catalog event.", nameof(entitlement));
        }

        ValidateEntitlementShape(
            (EntitlementScopeTypeEnum)entitlement.EntitlementScopeTypeId,
            entitlement.EventDayId,
            entitlement.EventSessionId,
            entitlement.IncludedQuantity,
            (EntitlementSelectionRuleEnum)entitlement.EntitlementSelectionRuleId);
    }

    public static void ValidateEntitlementShape(
        EntitlementScopeTypeEnum scopeType,
        Guid? eventDayId,
        Guid? eventSessionId,
        int includedQuantity,
        EntitlementSelectionRuleEnum selectionRule)
    {
        if (includedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(includedQuantity), "Included entitlement quantity must be positive.");
        }

        if (!Enum.IsDefined(scopeType) || !Enum.IsDefined(selectionRule))
        {
            throw new ArgumentOutOfRangeException(nameof(scopeType));
        }

        switch (scopeType)
        {
            case EntitlementScopeTypeEnum.Event when eventDayId is not null || eventSessionId is not null || selectionRule != EntitlementSelectionRuleEnum.AllIncluded:
                throw new ArgumentException("Event entitlements require no child target and use all-included selection.", nameof(scopeType));

            case EntitlementScopeTypeEnum.EventDay when eventDayId is null || eventSessionId is not null || selectionRule is EntitlementSelectionRuleEnum.ChooseOne or EntitlementSelectionRuleEnum.ChooseUpToN:
                throw new ArgumentException("Event-day entitlements require a day target and fixed or all-included selection.", nameof(scopeType));

            case EntitlementScopeTypeEnum.EventSession when eventDayId is not null || eventSessionId is null:
                throw new ArgumentException("Event-session entitlements require exactly one session target.", nameof(scopeType));
        }

        if ((selectionRule == EntitlementSelectionRuleEnum.FixedSelection || selectionRule == EntitlementSelectionRuleEnum.ChooseOne) && includedQuantity != 1)
        {
            throw new ArgumentException("Fixed and choose-one entitlements include exactly one selection.", nameof(includedQuantity));
        }

        if (selectionRule == EntitlementSelectionRuleEnum.ChooseUpToN && includedQuantity < 2)
        {
            throw new ArgumentException("Choose-up-to-N entitlements require a quantity greater than one.", nameof(includedQuantity));
        }
    }
}
