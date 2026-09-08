using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class EventTicketType : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<TicketTypeEntitlement> _entitlements = [];
    private Guid _tenantId;

    private EventTicketType()
    {
    }

    private EventTicketType(
        Guid id,
        Guid tenantId,
        Guid catalogId,
        string name,
        string currencyCode,
        TicketPricingModeEnum pricingMode,
        long? fixedPriceMinor,
        long? minimumPriceMinor,
        long? suggestedPriceMinor,
        ParticipantDataCollectionModeEnum participantDataCollectionMode,
        Guid? capacityPoolId,
        int? minimumAge,
        int? maximumAge,
        bool requiresGuardian,
        bool requiresApproval,
        int? perOrderLimit,
        int? perAccountLimit,
        int? perVerifiedContactLimit,
        int? perBookingPartyLimit)
    {
        Id = id;
        TenantId = tenantId;
        CatalogId = catalogId;
        Name = name;
        CurrencyCode = currencyCode;
        TicketPricingModeId = (int)pricingMode;
        FixedPriceMinor = fixedPriceMinor;
        MinimumPriceMinor = minimumPriceMinor;
        SuggestedPriceMinor = suggestedPriceMinor;
        ParticipantDataCollectionModeId = (int)participantDataCollectionMode;
        CapacityPoolId = capacityPoolId;
        MinimumAge = minimumAge;
        MaximumAge = maximumAge;
        RequiresGuardian = requiresGuardian;
        RequiresApproval = requiresApproval;
        PerOrderLimit = perOrderLimit;
        PerAccountLimit = perAccountLimit;
        PerVerifiedContactLimit = perVerifiedContactLimit;
        PerBookingPartyLimit = perBookingPartyLimit;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventTicketType));
    }

    public Guid CatalogId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string CurrencyCode { get; private set; } = string.Empty;

    public int TicketPricingModeId { get; private set; }

    public TicketPricingMode? TicketPricingMode { get; private set; }

    public long? FixedPriceMinor { get; private set; }

    public long? MinimumPriceMinor { get; private set; }

    public long? SuggestedPriceMinor { get; private set; }

    public int ParticipantDataCollectionModeId { get; private set; }

    public ParticipantDataCollectionMode? ParticipantDataCollectionMode { get; private set; }

    public Guid? CapacityPoolId { get; private set; }

    public int? MinimumAge { get; private set; }

    public int? MaximumAge { get; private set; }

    public bool RequiresGuardian { get; private set; }

    public bool RequiresApproval { get; private set; }

    public int? PerOrderLimit { get; private set; }

    public int? PerAccountLimit { get; private set; }

    public int? PerVerifiedContactLimit { get; private set; }

    public int? PerBookingPartyLimit { get; private set; }

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public IReadOnlyCollection<TicketTypeEntitlement> Entitlements => _entitlements.AsReadOnly();

    public static EventTicketType Create(
        Guid id,
        Guid tenantId,
        Guid catalogId,
        string name,
        string currencyCode,
        TicketPricingModeEnum pricingMode,
        Money? fixedPrice,
        Money? minimumPrice,
        Money? suggestedPrice,
        ParticipantDataCollectionModeEnum participantDataCollectionMode,
        Guid? capacityPoolId,
        int? minimumAge,
        int? maximumAge,
        bool requiresGuardian,
        bool requiresApproval,
        int? perOrderLimit,
        int? perAccountLimit,
        int? perVerifiedContactLimit,
        int? perBookingPartyLimit)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ticket type id is required.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant is required.", nameof(tenantId));
        }

        if (catalogId == Guid.Empty)
        {
            throw new ArgumentException("Catalog is required.", nameof(catalogId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Ticket type name is required.", nameof(name));
        }

        ValidateEligibility(minimumAge, maximumAge);
        ValidateLimits(perOrderLimit, perAccountLimit, perVerifiedContactLimit, perBookingPartyLimit);

        string normalizedCurrencyCode = CurrencyMetadata.Get(currencyCode).Code;
        EnsurePricingCurrency(normalizedCurrencyCode, fixedPrice, minimumPrice, suggestedPrice);
        long? fixedPriceMinor = fixedPrice?.MinorUnits;
        long? minimumPriceMinor = minimumPrice?.MinorUnits;
        long? suggestedPriceMinor = suggestedPrice?.MinorUnits;
        TicketPricingRules.ValidateConfiguration(pricingMode, normalizedCurrencyCode, fixedPriceMinor, minimumPriceMinor, suggestedPriceMinor);

        return new EventTicketType(
            id,
            tenantId,
            catalogId,
            name.Trim(),
            normalizedCurrencyCode,
            pricingMode,
            fixedPriceMinor,
            minimumPriceMinor,
            suggestedPriceMinor,
            ValidateParticipantDataCollectionMode(participantDataCollectionMode),
            capacityPoolId,
            minimumAge,
            maximumAge,
            requiresGuardian,
            requiresApproval,
            perOrderLimit,
            perAccountLimit,
            perVerifiedContactLimit,
            perBookingPartyLimit);
    }

    internal void UpdatePricing(
        TicketPricingModeEnum pricingMode,
        Money? fixedPrice,
        Money? minimumPrice,
        Money? suggestedPrice)
    {
        EnsurePricingCurrency(CurrencyCode, fixedPrice, minimumPrice, suggestedPrice);
        long? fixedPriceMinor = fixedPrice?.MinorUnits;
        long? minimumPriceMinor = minimumPrice?.MinorUnits;
        long? suggestedPriceMinor = suggestedPrice?.MinorUnits;
        TicketPricingRules.ValidateConfiguration(pricingMode, CurrencyCode, fixedPriceMinor, minimumPriceMinor, suggestedPriceMinor);
        TicketPricingModeId = (int)pricingMode;
        FixedPriceMinor = fixedPriceMinor;
        MinimumPriceMinor = minimumPriceMinor;
        SuggestedPriceMinor = suggestedPriceMinor;
    }

    internal void Update(string name, TicketPricingModeEnum pricingMode, Money? fixedPrice, Money? minimumPrice, Money? suggestedPrice, ParticipantDataCollectionModeEnum participantDataCollectionMode, int? minimumAge, int? maximumAge, bool requiresGuardian, bool requiresApproval, int? perOrderLimit, int? perAccountLimit, int? perVerifiedContactLimit, int? perBookingPartyLimit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Ticket type name is required.", nameof(name));
        ValidateEligibility(minimumAge, maximumAge);
        ValidateLimits(perOrderLimit, perAccountLimit, perVerifiedContactLimit, perBookingPartyLimit);
        UpdatePricing(pricingMode, fixedPrice, minimumPrice, suggestedPrice);
        Name = name.Trim(); ParticipantDataCollectionModeId = (int)ValidateParticipantDataCollectionMode(participantDataCollectionMode);
        MinimumAge = minimumAge; MaximumAge = maximumAge; RequiresGuardian = requiresGuardian; RequiresApproval = requiresApproval;
        PerOrderLimit = perOrderLimit; PerAccountLimit = perAccountLimit; PerVerifiedContactLimit = perVerifiedContactLimit; PerBookingPartyLimit = perBookingPartyLimit;
    }

    internal void Delete(DateTime deletedAtUtc, Guid deletedBy)
    {
        if (deletedAtUtc == default)
        {
            throw new ArgumentException("Deletion timestamp is required.", nameof(deletedAtUtc));
        }

        if (deletedBy == Guid.Empty)
        {
            throw new ArgumentException("Deleting actor is required.", nameof(deletedBy));
        }

        if (IsDeleted)
        {
            return;
        }

        DateTime normalizedDeletedAt = deletedAtUtc.ToUniversalTime();
        IsDeleted = true;
        DeletedAt = normalizedDeletedAt;
        DeletedBy = deletedBy;
        UpdatedAt = normalizedDeletedAt;
        UpdatedBy = deletedBy;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    internal void SetCapacityPool(EventCapacityPool? capacityPool)
    {
        CapacityPoolId = capacityPool?.Id;
    }

    internal void AddEntitlement(TicketTypeEntitlement entitlement)
    {
        if (_entitlements.Any(existing => HasSameCanonicalScope(existing, entitlement)))
        {
            throw new ArgumentException(
                "A ticket type can grant only one entitlement for each canonical target scope.",
                nameof(entitlement));
        }

        _entitlements.Add(entitlement);
    }

    internal void ReplaceEntitlements(IEnumerable<TicketTypeEntitlement> entitlements)
    {
        TicketTypeEntitlement[] replacements = entitlements.ToArray();
        if (replacements
            .GroupBy(entitlement => new
            {
                entitlement.TenantId,
                entitlement.TicketTypeId,
                entitlement.TargetEventId,
                entitlement.EntitlementScopeTypeId,
                entitlement.ScopeId
            })
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A ticket type can grant only one entitlement for each canonical target scope.",
                nameof(entitlements));
        }

        _entitlements.Clear();
        _entitlements.AddRange(replacements);
    }

    private static bool HasSameCanonicalScope(
        TicketTypeEntitlement left,
        TicketTypeEntitlement right) =>
        left.TenantId == right.TenantId &&
        left.TicketTypeId == right.TicketTypeId &&
        left.TargetEventId == right.TargetEventId &&
        left.EntitlementScopeTypeId == right.EntitlementScopeTypeId &&
        left.ScopeId == right.ScopeId;

    internal EventTicketType CloneTo(Guid catalogId)
    {
        var clone = new EventTicketType(
            Guid.CreateVersion7(),
            TenantId,
            catalogId,
            Name,
            CurrencyCode,
            (TicketPricingModeEnum)TicketPricingModeId,
            FixedPriceMinor,
            MinimumPriceMinor,
            SuggestedPriceMinor,
            (ParticipantDataCollectionModeEnum)ParticipantDataCollectionModeId,
            CapacityPoolId,
            MinimumAge,
            MaximumAge,
            RequiresGuardian,
            RequiresApproval,
            PerOrderLimit,
            PerAccountLimit,
            PerVerifiedContactLimit,
            PerBookingPartyLimit);

        foreach (TicketTypeEntitlement entitlement in _entitlements)
        {
            clone._entitlements.Add(entitlement.CloneTo(clone.Id));
        }

        return clone;
    }

    private static void EnsurePricingCurrency(string currencyCode, params Money?[] amounts)
    {
        if (amounts.Any(amount => amount is not null &&
            !string.Equals(amount.CurrencyCode, currencyCode, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Ticket price amounts must use the ticket currency.", nameof(amounts));
        }
    }

    private static ParticipantDataCollectionModeEnum ValidateParticipantDataCollectionMode(ParticipantDataCollectionModeEnum mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return mode;
    }

    private static void ValidateEligibility(int? minimumAge, int? maximumAge)
    {
        if (minimumAge is < 0 || maximumAge is < 0 || (minimumAge is not null && maximumAge is not null && minimumAge > maximumAge))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAge), "Age bounds must be non-negative and ordered.");
        }
    }

    private static void ValidateLimits(params int?[] limits)
    {
        if (limits.Any(static limit => limit is <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Ticket quantity limits must be positive when provided.");
        }
    }
}
