using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Studio;

public sealed class PromotionEditModel
{
    public string DisplayLabel { get; set; } = "Promotion";
    public string DiscountKind { get; set; } = "fixed";
    public long? FixedDiscountMinor { get; set; } = 100;
    public int? BasisPointDiscount { get; set; }
    public long? MaximumDiscountMinor { get; set; }
    public DateTime StartsAtUtc { get; set; } = DateTime.UtcNow.Date;
    public DateTime EndsAtUtc { get; set; } = DateTime.UtcNow.Date.AddDays(7);
    public int? TotalRedemptionLimit { get; set; }
    public int? PerVerifiedPurchaserLimit { get; set; }
    public bool IncludesAllTickets { get; set; } = true;
    public HashSet<Guid> EligibleTicketTypeIds { get; } = [];

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(DisplayLabel)
        && EndsAtUtc > StartsAtUtc
        && (IncludesAllTickets || EligibleTicketTypeIds.Count > 0)
        && (DiscountKind == "fixed"
            ? FixedDiscountMinor > 0
            : DiscountKind == "basis_points" && BasisPointDiscount is > 0 and <= 10_000)
        && TotalRedemptionLimit is null or > 0
        && PerVerifiedPurchaserLimit is null or > 0
        && MaximumDiscountMinor is null or > 0;

    public CreatePromotionDraftRequest ToCreateRequest(Guid ticketCatalogVersionId, string code) => new()
    {
        TicketCatalogVersionId = ticketCatalogVersionId,
        DisplayLabel = DisplayLabel.Trim(),
        Code = code,
        DiscountKind = DiscountKind,
        FixedDiscountMinor = DiscountKind == "fixed" ? FixedDiscountMinor : null,
        BasisPointDiscount = DiscountKind == "basis_points" ? BasisPointDiscount : null,
        MaximumDiscountMinor = DiscountKind == "basis_points" ? MaximumDiscountMinor : null,
        StartsAtUtc = AsUtc(StartsAtUtc),
        EndsAtUtc = AsUtc(EndsAtUtc),
        TotalRedemptionLimit = TotalRedemptionLimit,
        PerVerifiedPurchaserLimit = PerVerifiedPurchaserLimit,
        EligibleTicketTypeIds = Eligibility()
    };

    public RevisePromotionRequest ToReviseRequest() => new()
    {
        DisplayLabel = DisplayLabel.Trim(),
        DiscountKind = DiscountKind,
        FixedDiscountMinor = DiscountKind == "fixed" ? FixedDiscountMinor : null,
        BasisPointDiscount = DiscountKind == "basis_points" ? BasisPointDiscount : null,
        MaximumDiscountMinor = DiscountKind == "basis_points" ? MaximumDiscountMinor : null,
        StartsAtUtc = AsUtc(StartsAtUtc),
        EndsAtUtc = AsUtc(EndsAtUtc),
        TotalRedemptionLimit = TotalRedemptionLimit,
        PerVerifiedPurchaserLimit = PerVerifiedPurchaserLimit,
        EligibleTicketTypeIds = Eligibility()
    };

    public static PromotionEditModel From(PromotionManagementItemState promotion)
    {
        var model = new PromotionEditModel
        {
            DisplayLabel = promotion.DisplayLabel,
            DiscountKind = promotion.DiscountKind,
            FixedDiscountMinor = promotion.FixedDiscountMinor,
            BasisPointDiscount = promotion.BasisPointDiscount,
            MaximumDiscountMinor = promotion.MaximumDiscountMinor,
            StartsAtUtc = promotion.StartsAtUtc.UtcDateTime,
            EndsAtUtc = promotion.EndsAtUtc.UtcDateTime,
            TotalRedemptionLimit = promotion.TotalRedemptionLimit,
            PerVerifiedPurchaserLimit = promotion.PerVerifiedPurchaserLimit,
            IncludesAllTickets = promotion.IncludesAllTickets
        };
        model.EligibleTicketTypeIds.UnionWith(promotion.EligibleTicketTypeIds);
        return model;
    }

    private ICollection<Guid> Eligibility() =>
        IncludesAllTickets ? [] : EligibleTicketTypeIds.Order().ToArray();

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
