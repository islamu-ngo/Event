namespace Explore.API.Models;

public sealed record CreatePromotionDraftRequest(
    Guid TicketCatalogVersionId,
    string DisplayLabel,
    string Code,
    string DiscountKind,
    long? FixedDiscountMinor,
    int? BasisPointDiscount,
    long? MaximumDiscountMinor,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int? TotalRedemptionLimit,
    int? PerVerifiedPurchaserLimit,
    IReadOnlyCollection<Guid> EligibleTicketTypeIds);

public sealed record RevisePromotionRequest(
    string DisplayLabel,
    string DiscountKind,
    long? FixedDiscountMinor,
    int? BasisPointDiscount,
    long? MaximumDiscountMinor,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int? TotalRedemptionLimit,
    int? PerVerifiedPurchaserLimit,
    IReadOnlyCollection<Guid> EligibleTicketTypeIds);

public sealed record PromotionCodeRequest(string Code);

public sealed record RevokePromotionRequest;
