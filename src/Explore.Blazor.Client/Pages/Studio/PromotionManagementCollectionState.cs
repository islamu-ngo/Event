using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Studio;

public sealed record PromotionManagementCollectionState(
    Guid EventId,
    Guid TicketCatalogVersionId,
    IReadOnlyList<PromotionManagementItemState> Items,
    IReadOnlyDictionary<string, HalLink> Links)
{
    public bool HasLink(string relation) => Links.ContainsKey(relation);

    public static PromotionManagementCollectionState Create(
        Guid eventId,
        Guid ticketCatalogVersionId,
        IReadOnlyList<PromotionManagementItemState> items,
        IReadOnlyDictionary<string, HalLink>? links = null) => new(
        eventId,
        ticketCatalogVersionId,
        items,
        links ?? EmptyLinks());

    public static bool TryParse(
        HalCollectionResourceOfPromotionManagementDto resource,
        Guid eventId,
        Guid ticketCatalogVersionId,
        out PromotionManagementCollectionState? state)
    {
        state = null;
        if (eventId == Guid.Empty || ticketCatalogVersionId == Guid.Empty)
        {
            return false;
        }

        var items = new List<PromotionManagementItemState>();
        foreach (HalResourceOfPromotionManagementDto item in resource._embedded?.Items ?? [])
        {
            if (!PromotionManagementItemState.TryCreate(item, eventId, ticketCatalogVersionId, out PromotionManagementItemState? parsed))
            {
                return false;
            }

            items.Add(parsed!);
        }

        state = new PromotionManagementCollectionState(
            eventId,
            ticketCatalogVersionId,
            items,
            LinksFrom(resource._links));
        return true;
    }

    internal static IReadOnlyDictionary<string, HalLink> LinksFrom(IDictionary<string, HalLink>? source) =>
        source?.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value.Href))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        ?? EmptyLinks();

    private static IReadOnlyDictionary<string, HalLink> EmptyLinks() =>
        new Dictionary<string, HalLink>(StringComparer.Ordinal);
}

public sealed record PromotionManagementItemState(
    Guid DefinitionId,
    string DisplayLabel,
    string StatusName,
    string DiscountKind,
    string CurrencyCode,
    long? FixedDiscountMinor,
    int? BasisPointDiscount,
    long? MaximumDiscountMinor,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    int? TotalRedemptionLimit,
    int? PerVerifiedPurchaserLimit,
    bool IncludesAllTickets,
    IReadOnlyCollection<Guid> EligibleTicketTypeIds,
    string? PromotionCodeDisplayLabel,
    IReadOnlyDictionary<string, HalLink> Links)
{
    public bool HasLink(string relation) => Links.ContainsKey(relation);

    internal static bool TryCreate(
        HalResourceOfPromotionManagementDto dto,
        Guid eventId,
        Guid ticketCatalogVersionId,
        out PromotionManagementItemState? state)
    {
        state = null;
        if (dto.EventId != eventId
            || dto.TicketCatalogVersionId != ticketCatalogVersionId
            || dto.DefinitionId is not { } definitionId
            || definitionId == Guid.Empty
            || string.IsNullOrWhiteSpace(dto.DisplayLabel)
            || string.IsNullOrWhiteSpace(dto.StatusName ?? dto.StatusCode)
            || dto.DiscountKind is not ("fixed" or "basis_points")
            || string.IsNullOrWhiteSpace(dto.CurrencyCode)
            || dto.StartsAtUtc is not { } startsAtUtc
            || dto.EndsAtUtc is not { } endsAtUtc
            || dto.IncludesAllTickets is not { } includesAllTickets)
        {
            return false;
        }

        Guid[] eligibleTicketTypeIds = dto.EligibleTicketTypeIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        if (includesAllTickets == (eligibleTicketTypeIds.Length > 0))
        {
            return false;
        }

        state = new PromotionManagementItemState(
            definitionId,
            dto.DisplayLabel,
            dto.StatusName ?? dto.StatusCode!,
            dto.DiscountKind,
            dto.CurrencyCode,
            dto.FixedDiscountMinor,
            dto.BasisPointDiscount,
            dto.MaximumDiscountMinor,
            startsAtUtc,
            endsAtUtc,
            dto.TotalRedemptionLimit,
            dto.PerVerifiedPurchaserLimit,
            includesAllTickets,
            eligibleTicketTypeIds,
            dto.PromotionCodeDisplayLabel,
            PromotionManagementCollectionState.LinksFrom(dto._links));
        return true;
    }
}
