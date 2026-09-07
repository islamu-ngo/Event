namespace Explore.Domain;

public sealed record PromotionDiscountLine(Guid LineId, Guid TicketTypeId, string CurrencyCode, long LineSubtotalMinor);

public sealed record PromotionLineDiscountAllocation(Guid LineId, long PreDiscountLineSubtotalMinor, long DiscountMinor, long PostDiscountLineSubtotalMinor);

public sealed record PromotionDiscountAllocation(long TotalDiscountMinor, long PostDiscountOrganizerTotalMinor, IReadOnlyCollection<PromotionLineDiscountAllocation> LineAllocations);
