using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class EventTicketCatalogVersion : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public const int MaxCommercialDisclosureTextLength = 2000;

    private readonly List<EventTicketType> _ticketTypes = [];
    private Guid _tenantId;

    private EventTicketCatalogVersion()
    {
    }

    private EventTicketCatalogVersion(Guid tenantId, Guid eventId, string currencyCode, int versionNumber)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        EventId = eventId;
        CurrencyCode = currencyCode;
        VersionNumber = versionNumber;
        TicketCatalogStatusId = (int)TicketCatalogStatusEnum.Draft;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventTicketCatalogVersion));
    }

    public Guid EventId { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public int VersionNumber { get; private set; }

    public int TicketCatalogStatusId { get; private set; }

    public TicketCatalogStatus? TicketCatalogStatus { get; private set; }

    public string? MerchantDisclosureText { get; private set; }

    public string? RefundPolicyDisclosureText { get; private set; }

    public string? SupportContactDisclosureText { get; private set; }

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public IReadOnlyCollection<EventTicketType> TicketTypes => _ticketTypes.AsReadOnly();

    public static EventTicketCatalogVersion Create(Guid tenantId, Guid eventId, string currencyCode, int versionNumber)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant is required.", nameof(tenantId));
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event is required.", nameof(eventId));
        }

        if (versionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber));
        }

        return new EventTicketCatalogVersion(tenantId, eventId, CurrencyMetadata.Get(currencyCode).Code, versionNumber);
    }

    public void AddTicketType(EventTicketType ticketType, EventCapacityPool? capacityPool)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(ticketType);

        if (ticketType.CatalogId != Id || ticketType.TenantId != TenantId)
        {
            throw new ArgumentException("Ticket type must belong to this draft catalog and tenant.", nameof(ticketType));
        }

        if (!string.Equals(ticketType.CurrencyCode, CurrencyCode, StringComparison.Ordinal))
        {
            throw new ArgumentException("All ticket types in a catalog must use the catalog currency.", nameof(ticketType));
        }

        TicketCatalogRules.ValidateCapacityPool(this, capacityPool);
        ticketType.SetCapacityPool(capacityPool);
        _ticketTypes.Add(ticketType);
    }

    public void UpdateTicketPricing(
        EventTicketType ticketType,
        TicketPricingModeEnum pricingMode,
        Money? fixedPrice,
        Money? minimumPrice,
        Money? suggestedPrice)
    {
        EnsureDraft();
        EnsureContains(ticketType);
        ticketType.UpdatePricing(pricingMode, fixedPrice, minimumPrice, suggestedPrice);
    }

    public void AddEntitlement(EventTicketType ticketType, TicketTypeEntitlement entitlement)
    {
        EnsureDraft();
        EnsureContains(ticketType);
        ArgumentNullException.ThrowIfNull(entitlement);

        TicketCatalogRules.ValidateEntitlement(this, ticketType, entitlement);
        ticketType.AddEntitlement(entitlement);
    }

    public void UpdateCommercialDisclosures(
        string? merchantDisclosureText,
        string? refundPolicyDisclosureText,
        string? supportContactDisclosureText)
    {
        EnsureDraft();
        MerchantDisclosureText = NormalizeCommercialDisclosure(merchantDisclosureText, nameof(merchantDisclosureText));
        RefundPolicyDisclosureText = NormalizeCommercialDisclosure(refundPolicyDisclosureText, nameof(refundPolicyDisclosureText));
        SupportContactDisclosureText = NormalizeCommercialDisclosure(supportContactDisclosureText, nameof(supportContactDisclosureText));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateTicketType(EventTicketType ticketType, string name, TicketPricingModeEnum pricingMode, Money? fixedPrice, Money? minimumPrice, Money? suggestedPrice, ParticipantDataCollectionModeEnum participantDataCollectionMode, EventCapacityPool? capacityPool, int? minimumAge, int? maximumAge, bool requiresGuardian, bool requiresApproval, int? perOrderLimit, int? perAccountLimit, int? perVerifiedContactLimit, int? perBookingPartyLimit, IEnumerable<TicketTypeEntitlement> entitlements)
    {
        EnsureDraft(); EnsureContains(ticketType); TicketCatalogRules.ValidateCapacityPool(this, capacityPool);
        ticketType.Update(name, pricingMode, fixedPrice, minimumPrice, suggestedPrice, participantDataCollectionMode, minimumAge, maximumAge, requiresGuardian, requiresApproval, perOrderLimit, perAccountLimit, perVerifiedContactLimit, perBookingPartyLimit);
        ticketType.SetCapacityPool(capacityPool);
        foreach (var entitlement in entitlements) TicketCatalogRules.ValidateEntitlement(this, ticketType, entitlement);
        ticketType.ReplaceEntitlements(entitlements);
    }

    public void DeleteTicketType(EventTicketType ticketType, DateTime deletedAtUtc, Guid deletedBy)
    {
        EnsureDraft();
        EnsureContains(ticketType);
        ticketType.Delete(deletedAtUtc, deletedBy);
    }

    public void ValidateForPublication()
    {
        TicketCatalogRules.ValidateForPublication(this);
    }

    public void Publish()
    {
        EnsureDraft();
        ValidateForPublication();
        TicketCatalogStatusId = (int)TicketCatalogStatusEnum.Published;
    }

    public void Retire()
    {
        if (TicketCatalogStatusId != (int)TicketCatalogStatusEnum.Published)
        {
            throw new InvalidOperationException("Only a published ticket catalog can be retired.");
        }

        TicketCatalogStatusId = (int)TicketCatalogStatusEnum.Retired;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public EventTicketCatalogVersion CloneToDraft()
    {
        if (TicketCatalogStatusId != (int)TicketCatalogStatusEnum.Published)
        {
            throw new InvalidOperationException("Only a published ticket catalog can be cloned into a draft.");
        }

        EventTicketCatalogVersion clone = Create(TenantId, EventId, CurrencyCode, checked(VersionNumber + 1));
        clone.UpdateCommercialDisclosures(MerchantDisclosureText, RefundPolicyDisclosureText, SupportContactDisclosureText);
        foreach (EventTicketType ticketType in _ticketTypes.Where(ticketType => !ticketType.IsDeleted))
        {
            clone._ticketTypes.Add(ticketType.CloneTo(clone.Id));
        }

        return clone;
    }

    private void EnsureDraft()
    {
        if (TicketCatalogStatusId != (int)TicketCatalogStatusEnum.Draft)
        {
            throw new InvalidOperationException("Published or retired ticket catalogs are immutable.");
        }
    }

    private void EnsureContains(EventTicketType ticketType)
    {
        ArgumentNullException.ThrowIfNull(ticketType);
        if (!_ticketTypes.Contains(ticketType))
        {
            throw new ArgumentException("Ticket type does not belong to this catalog.", nameof(ticketType));
        }
    }

    private static string? NormalizeCommercialDisclosure(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length <= MaxCommercialDisclosureTextLength
            ? normalized
            : throw new ArgumentException($"Commercial disclosure text must be at most {MaxCommercialDisclosureTextLength} characters.", parameterName);
    }
}
