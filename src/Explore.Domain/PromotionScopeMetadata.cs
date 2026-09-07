using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed record PromotionScopeMetadata
{
    private PromotionScopeMetadata(Guid tenantId, Guid eventId, Guid ticketCatalogVersionId, int ticketCatalogVersionNumber, string currencyCode)
    {
        TenantId = tenantId;
        EventId = eventId;
        TicketCatalogVersionId = ticketCatalogVersionId;
        TicketCatalogVersionNumber = ticketCatalogVersionNumber;
        CurrencyCode = currencyCode;
    }

    public Guid TenantId { get; }

    public Guid EventId { get; }

    public Guid TicketCatalogVersionId { get; }

    public int TicketCatalogVersionNumber { get; }

    public string CurrencyCode { get; }

    public static PromotionScopeMetadata Create(Guid tenantId, Guid eventId, Guid ticketCatalogVersionId, int ticketCatalogVersionNumber, string currencyCode)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty || ticketCatalogVersionId == Guid.Empty || ticketCatalogVersionNumber <= 0)
        {
            throw new ArgumentException("Promotion scope requires tenant, event, catalog version, and positive version number.");
        }

        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency)
        {
            throw new ArgumentException("Promotions require a monetary currency.", nameof(currencyCode));
        }

        return new PromotionScopeMetadata(tenantId, eventId, ticketCatalogVersionId, ticketCatalogVersionNumber, currency.Code);
    }
}
