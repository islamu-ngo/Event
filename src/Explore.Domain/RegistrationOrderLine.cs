using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class RegistrationOrderLine : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private readonly List<RegistrationTicketAssignment> _assignments = [];
    private Guid _tenantId;

    private RegistrationOrderLine()
    {
    }

    private RegistrationOrderLine(
        Guid id,
        Guid registrationOrderId,
        EventTicketCatalogVersion catalog,
        EventTicketType ticketType,
        int quantity,
        long unitPriceAmountSnapshot,
        long? chosenUnitPriceAmountSnapshot,
        int? platformFeePolicyVersionSnapshot)
    {
        Id = id;
        RegistrationOrderId = registrationOrderId;
        TenantId = catalog.TenantId;
        TicketTypeId = ticketType.Id;
        Quantity = quantity;
        UnitPriceAmountSnapshot = unitPriceAmountSnapshot;
        ChosenUnitPriceAmountSnapshot = chosenUnitPriceAmountSnapshot;
        CurrencyCodeSnapshot = ticketType.CurrencyCode;
        LineSubtotalSnapshot = MinorUnitMath.Multiply(unitPriceAmountSnapshot, quantity);
        PreDiscountLineSubtotalMinorSnapshot = LineSubtotalSnapshot;
        PostDiscountLineSubtotalMinorSnapshot = LineSubtotalSnapshot;
        TicketTypeNameSnapshot = ticketType.Name;
        TicketPricingModeSnapshot = ticketType.TicketPricingModeId;
        MinimumPriceAmountSnapshot = ticketType.MinimumPriceMinor;
        SuggestedPriceAmountSnapshot = ticketType.SuggestedPriceMinor;
        TicketCatalogVersionId = catalog.Id;
        PlatformFeePolicyVersionSnapshot = platformFeePolicyVersionSnapshot;
    }

    public Guid Id { get; private set; }

    public Guid RegistrationOrderId { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(RegistrationOrderLine));
    }

    public Guid TicketTypeId { get; private set; }

    public int Quantity { get; private set; }

    public long UnitPriceAmountSnapshot { get; private set; }

    public long? ChosenUnitPriceAmountSnapshot { get; private set; }

    public string CurrencyCodeSnapshot { get; private set; } = string.Empty;

    public long LineSubtotalSnapshot { get; private set; }

    public long PreDiscountLineSubtotalMinorSnapshot { get; private set; }

    public long PromotionDiscountAmountMinorSnapshot { get; private set; }

    public long PostDiscountLineSubtotalMinorSnapshot { get; private set; }

    public string TicketTypeNameSnapshot { get; private set; } = string.Empty;

    public int TicketPricingModeSnapshot { get; private set; }

    public long? MinimumPriceAmountSnapshot { get; private set; }

    public long? SuggestedPriceAmountSnapshot { get; private set; }

    public Guid TicketCatalogVersionId { get; private set; }

    public int? PlatformFeePolicyVersionSnapshot { get; private set; }

    public IReadOnlyCollection<RegistrationTicketAssignment> Assignments => _assignments.AsReadOnly();

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static RegistrationOrderLine Create(
        EventTicketCatalogVersion catalog,
        EventTicketType ticketType,
        Guid registrationOrderId,
        int quantity,
        long? chosenUnitPriceAmount,
        PlatformFeePolicy? platformFeePolicy) => Create(
        Guid.CreateVersion7(),
        catalog,
        ticketType,
        registrationOrderId,
        quantity,
        chosenUnitPriceAmount,
        platformFeePolicy);

    public static RegistrationOrderLine Create(
        Guid id,
        EventTicketCatalogVersion catalog,
        EventTicketType ticketType,
        Guid registrationOrderId,
        int quantity,
        long? chosenUnitPriceAmount,
        PlatformFeePolicy? platformFeePolicy)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ticketType);

        if (id == Guid.Empty || registrationOrderId == Guid.Empty || quantity <= 0)
        {
            throw new ArgumentException("Registration order and positive quantity are required.");
        }

        if (catalog.TicketCatalogStatusId != (int)TicketCatalogStatusEnum.Published ||
            ticketType.CatalogId != catalog.Id ||
            ticketType.TenantId != catalog.TenantId ||
            !catalog.TicketTypes.Contains(ticketType))
        {
            throw new InvalidOperationException("Order lines require a ticket from the pinned published catalog.");
        }

        TicketPricingModeEnum pricingMode = (TicketPricingModeEnum)ticketType.TicketPricingModeId;
        TicketPricingRules.ValidateConfiguration(
            pricingMode,
            ticketType.CurrencyCode,
            ticketType.FixedPriceMinor,
            ticketType.MinimumPriceMinor,
            ticketType.SuggestedPriceMinor);

        (long unitPriceAmount, long? chosenSnapshot) = ResolveUnitPrice(ticketType, pricingMode, chosenUnitPriceAmount);
        long lineSubtotal = MinorUnitMath.Multiply(unitPriceAmount, quantity);
        int? policyVersionSnapshot = platformFeePolicy is { IsEnabled: true } &&
                                     platformFeePolicy.CalculateFeeMinor(ticketType.CurrencyCode, lineSubtotal) > 0
            ? platformFeePolicy.VersionNumber
            : null;

        return new RegistrationOrderLine(
            id,
            registrationOrderId,
            catalog,
            ticketType,
            quantity,
            unitPriceAmount,
            chosenSnapshot,
            policyVersionSnapshot);
    }

    internal void AddAssignment(RegistrationTicketAssignment assignment)
    {
        if (_assignments.Any(existing => existing.Id == assignment.Id || existing.Ordinal == assignment.Ordinal))
        {
            throw new ArgumentException("Ticket assignment identity and ordinal must be unique within the order line.", nameof(assignment));
        }

        _assignments.Add(assignment);
    }

    internal void ApplyPromotionDiscount(PromotionLineDiscountAllocation allocation)
    {
        if (allocation.LineId != Id || allocation.PreDiscountLineSubtotalMinor != LineSubtotalSnapshot ||
            allocation.DiscountMinor < 0 || allocation.PostDiscountLineSubtotalMinor < 0 ||
            allocation.PostDiscountLineSubtotalMinor != LineSubtotalSnapshot - allocation.DiscountMinor)
        {
            throw new ArgumentException("Promotion allocation does not match the line snapshot.", nameof(allocation));
        }

        PreDiscountLineSubtotalMinorSnapshot = allocation.PreDiscountLineSubtotalMinor;
        PromotionDiscountAmountMinorSnapshot = allocation.DiscountMinor;
        PostDiscountLineSubtotalMinorSnapshot = allocation.PostDiscountLineSubtotalMinor;
    }

    internal void ClearPromotionDiscount()
    {
        PreDiscountLineSubtotalMinorSnapshot = LineSubtotalSnapshot;
        PromotionDiscountAmountMinorSnapshot = 0;
        PostDiscountLineSubtotalMinorSnapshot = LineSubtotalSnapshot;
    }

    private static (long UnitPriceAmount, long? ChosenSnapshot) ResolveUnitPrice(
        EventTicketType ticketType,
        TicketPricingModeEnum pricingMode,
        long? chosenUnitPriceAmount)
    {
        return pricingMode switch
        {
            TicketPricingModeEnum.Fixed when chosenUnitPriceAmount is null => (ticketType.FixedPriceMinor!.Value, null),
            TicketPricingModeEnum.Free when chosenUnitPriceAmount is null => (0, null),
            TicketPricingModeEnum.Donation or TicketPricingModeEnum.PayWhatYouCan or TicketPricingModeEnum.SlidingScale when chosenUnitPriceAmount.HasValue =>
                (TicketPricingRules.ValidateChosenUnitPriceMinor(pricingMode, ticketType.CurrencyCode, chosenUnitPriceAmount.Value, ticketType.MinimumPriceMinor), chosenUnitPriceAmount),
            _ => throw new ArgumentException("The ticket pricing mode and buyer-selected amount are incompatible.", nameof(chosenUnitPriceAmount))
        };
    }
}
