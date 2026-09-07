using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class PaidEventPolicyVersion : IAuditableEntity
{
    private const int ActivePolicyUniquenessSlot = 0;

    private readonly List<PaidEventPolicyAllowedOrganizerKind> _allowedOrganizerKinds = [];
    private readonly List<PaidEventPolicyAllowedCurrency> _allowedCurrencyCodes = [];
    private readonly List<PaidEventPolicyRefundProtection> _refundProtections = [];
    private readonly List<PaidEventPolicyCurrencyRiskLimitRow> _currencyRiskLimits = [];

    private PaidEventPolicyVersion()
    {
    }

    private PaidEventPolicyVersion(
        Guid? tenantId,
        int versionNumber,
        bool isPaymentsEnabled,
        IEnumerable<ActorTypeEnum> allowedOrganizerKinds,
        bool requiresLocalVerification,
        IEnumerable<string> allowedCurrencyCodes,
        string? defaultCurrencyCode,
        IEnumerable<PaidEventRefundProtection> refundProtections,
        IEnumerable<PaidEventPolicyCurrencyRiskLimit> currencyRiskLimits,
        bool requiresFirstPaidEventReview,
        int? farFutureReviewThresholdDays)
    {
        ActorTypeEnum[] normalizedOrganizerKinds = NormalizeOrganizerKinds(allowedOrganizerKinds);
        string[] normalizedCurrencyCodes = NormalizeCurrencyCodes(allowedCurrencyCodes);
        PaidEventRefundProtection[] normalizedRefundProtections = NormalizeRefundProtections(refundProtections);
        PaidEventPolicyCurrencyRiskLimit[] normalizedRiskLimits = NormalizeRiskLimits(currencyRiskLimits, normalizedCurrencyCodes);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        PolicyScopeKey = CreatePolicyScopeKey(tenantId);
        VersionNumber = versionNumber;
        IsActive = true;
        ActiveUniquenessSlot = ActivePolicyUniquenessSlot;
        IsPaymentsEnabled = isPaymentsEnabled;
        RequiresLocalVerification = requiresLocalVerification;
        DefaultCurrencyCode = NormalizeDefaultCurrency(defaultCurrencyCode, normalizedCurrencyCodes);
        RequiresFirstPaidEventReview = requiresFirstPaidEventReview;
        FarFutureReviewThresholdDays = farFutureReviewThresholdDays;
        _allowedOrganizerKinds.AddRange(normalizedOrganizerKinds.Select((kind, index) =>
            PaidEventPolicyAllowedOrganizerKind.Create(TenantId, PolicyScopeKey, Id, index, kind)));
        _allowedCurrencyCodes.AddRange(normalizedCurrencyCodes.Select((currencyCode, index) =>
            PaidEventPolicyAllowedCurrency.Create(TenantId, PolicyScopeKey, Id, index, currencyCode)));
        _refundProtections.AddRange(normalizedRefundProtections.Select((protection, index) =>
            PaidEventPolicyRefundProtection.Create(TenantId, PolicyScopeKey, Id, index, protection)));
        _currencyRiskLimits.AddRange(normalizedRiskLimits.Select((limit, index) =>
            PaidEventPolicyCurrencyRiskLimitRow.Create(TenantId, PolicyScopeKey, Id, index, limit)));
        ValidateFarFutureThreshold(farFutureReviewThresholdDays);
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public string PolicyScopeKey { get; private set; } = string.Empty;

    public int VersionNumber { get; private set; }

    public bool IsActive { get; private set; }

    public int ActiveUniquenessSlot { get; private set; }

    public bool IsPaymentsEnabled { get; private set; }

    public bool RequiresLocalVerification { get; private set; }

    public IReadOnlyCollection<ActorTypeEnum> AllowedOrganizerKinds => _allowedOrganizerKinds
        .OrderBy(row => row.Ordinal)
        .Select(row => row.ActorType)
        .ToArray();

    public IReadOnlyCollection<string> AllowedCurrencyCodes => _allowedCurrencyCodes
        .OrderBy(row => row.Ordinal)
        .Select(row => row.CurrencyCode)
        .ToArray();

    public IReadOnlyCollection<PaidEventRefundProtection> RefundProtections => _refundProtections
        .OrderBy(row => row.Ordinal)
        .Select(row => row.Protection)
        .ToArray();

    public IReadOnlyCollection<PaidEventPolicyCurrencyRiskLimit> CurrencyRiskLimits => _currencyRiskLimits
        .OrderBy(row => row.Ordinal)
        .Select(row => row.ToValueObject())
        .ToArray();

    private IReadOnlyCollection<PaidEventPolicyAllowedOrganizerKind> AllowedOrganizerKindRows => _allowedOrganizerKinds;

    private IReadOnlyCollection<PaidEventPolicyAllowedCurrency> AllowedCurrencyRows => _allowedCurrencyCodes;

    private IReadOnlyCollection<PaidEventPolicyRefundProtection> RefundProtectionRows => _refundProtections;

    private IReadOnlyCollection<PaidEventPolicyCurrencyRiskLimitRow> CurrencyRiskLimitRows => _currencyRiskLimits;

    public string? DefaultCurrencyCode { get; private set; }

    public bool RequiresFirstPaidEventReview { get; private set; }

    public int? FarFutureReviewThresholdDays { get; private set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static PaidEventPolicyVersion CreateDefaultInstance() => new(
        tenantId: null,
        versionNumber: 1,
        isPaymentsEnabled: false,
        allowedOrganizerKinds: [ActorTypeEnum.Organization],
        requiresLocalVerification: false,
        allowedCurrencyCodes: ["EUR", "USD", "MAD", "SAR", "AED"],
        defaultCurrencyCode: null,
        refundProtections: RequiredRefundFloor(),
        currencyRiskLimits: [],
        requiresFirstPaidEventReview: false,
        farFutureReviewThresholdDays: null);

    public static PaidEventPolicyVersion CreateTenant(
        Guid tenantId,
        bool isPaymentsEnabled,
        IEnumerable<ActorTypeEnum> allowedOrganizerKinds,
        bool requiresLocalVerification,
        IEnumerable<string> allowedCurrencyCodes,
        string? defaultCurrencyCode,
        IEnumerable<PaidEventRefundProtection> refundProtections,
        IEnumerable<PaidEventPolicyCurrencyRiskLimit> currencyRiskLimits,
        bool requiresFirstPaidEventReview,
        int? farFutureReviewThresholdDays)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant is required.", nameof(tenantId));
        }

        return new PaidEventPolicyVersion(
            tenantId,
            1,
            isPaymentsEnabled,
            allowedOrganizerKinds,
            requiresLocalVerification,
            allowedCurrencyCodes,
            defaultCurrencyCode,
            refundProtections,
            currencyRiskLimits,
            requiresFirstPaidEventReview,
            farFutureReviewThresholdDays);
    }

    public PaidEventPolicyVersion CreateRevision(
        bool isPaymentsEnabled,
        IEnumerable<ActorTypeEnum> allowedOrganizerKinds,
        bool requiresLocalVerification,
        IEnumerable<string> allowedCurrencyCodes,
        string? defaultCurrencyCode,
        IEnumerable<PaidEventRefundProtection> refundProtections,
        IEnumerable<PaidEventPolicyCurrencyRiskLimit> currencyRiskLimits,
        bool requiresFirstPaidEventReview,
        int? farFutureReviewThresholdDays)
    {
        EnsureActive();
        PaidEventPolicyVersion revision = new(
            TenantId,
            checked(VersionNumber + 1),
            isPaymentsEnabled,
            allowedOrganizerKinds,
            requiresLocalVerification,
            allowedCurrencyCodes,
            defaultCurrencyCode,
            refundProtections,
            currencyRiskLimits,
            requiresFirstPaidEventReview,
            farFutureReviewThresholdDays);
        RetireActiveSlot();
        return revision;
    }

    public void Retire()
    {
        EnsureActive();
        RetireActiveSlot();
    }

    private void RetireActiveSlot()
    {
        IsActive = false;
        ActiveUniquenessSlot = VersionNumber;
    }

    private static string CreatePolicyScopeKey(Guid? tenantId) => tenantId is { } value
        ? $"tenant:{value:N}"
        : "instance";

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Only the active paid-event policy version can be revised or retired.");
        }
    }

    private static ActorTypeEnum[] NormalizeOrganizerKinds(IEnumerable<ActorTypeEnum> allowedOrganizerKinds)
    {
        ArgumentNullException.ThrowIfNull(allowedOrganizerKinds);
        ActorTypeEnum[] kinds = allowedOrganizerKinds.Distinct().ToArray();
        if (kinds.Length == 0)
        {
            throw new ArgumentException("At least one organizer kind is required.", nameof(allowedOrganizerKinds));
        }

        if (kinds.Any(static kind => kind is not ActorTypeEnum.Organization and not ActorTypeEnum.Group and not ActorTypeEnum.User))
        {
            throw new ArgumentException("Paid-event organizer kinds are limited to organization, group, or user actors.", nameof(allowedOrganizerKinds));
        }

        return kinds;
    }

    private static string[] NormalizeCurrencyCodes(IEnumerable<string> allowedCurrencyCodes)
    {
        ArgumentNullException.ThrowIfNull(allowedCurrencyCodes);
        string[] currencyCodes = allowedCurrencyCodes.Select(NormalizeMoneyCurrencyCode).Distinct(StringComparer.Ordinal).ToArray();
        if (currencyCodes.Length == 0)
        {
            throw new ArgumentException("At least one currency is required.", nameof(allowedCurrencyCodes));
        }

        return currencyCodes;
    }

    private static PaidEventRefundProtection[] RequiredRefundFloor() =>
    [
        PaidEventRefundProtection.OrganizerCancellationFullRefund,
        PaidEventRefundProtection.MaterialChangeBuyerChoiceOrFullRefund,
        PaidEventRefundProtection.DuplicateOrIncorrectChargeFullRefund,
        PaidEventRefundProtection.SubstantialNonDeliveryRemedy,
        PaidEventRefundProtection.AttendeeBuyerChangeTermsDisclosedSubjectToLaw,
        PaidEventRefundProtection.CardDisputeRightsNotWaived,
        PaidEventRefundProtection.CancelledEventPlatformAmountsRefundedByDefault
    ];

    private static PaidEventRefundProtection[] NormalizeRefundProtections(IEnumerable<PaidEventRefundProtection> refundProtections)
    {
        ArgumentNullException.ThrowIfNull(refundProtections);
        PaidEventRefundProtection[] protections = refundProtections.Distinct().ToArray();
        if (protections.Length == 0 || protections.Any(static protection => !Enum.IsDefined(protection)))
        {
            throw new ArgumentException("Paid-event refund protections must be known and non-empty.", nameof(refundProtections));
        }

        if (RequiredRefundFloor().Any(protection => !protections.Contains(protection)))
        {
            throw new ArgumentException("Paid-event refund protections must include every required refund floor protection.", nameof(refundProtections));
        }

        return protections;
    }

    private static PaidEventPolicyCurrencyRiskLimit[] NormalizeRiskLimits(IEnumerable<PaidEventPolicyCurrencyRiskLimit> currencyRiskLimits, IReadOnlyCollection<string> allowedCurrencyCodes)
    {
        ArgumentNullException.ThrowIfNull(currencyRiskLimits);
        PaidEventPolicyCurrencyRiskLimit[] limits = currencyRiskLimits.ToArray();
        if (limits.Select(static limit => limit.CurrencyCode).Distinct(StringComparer.Ordinal).Count() != limits.Length)
        {
            throw new ArgumentException("Currency risk limit currencies must be unique.", nameof(currencyRiskLimits));
        }

        if (limits.Any(limit => !allowedCurrencyCodes.Contains(limit.CurrencyCode, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Currency risk limits must belong to the allowed currency set.", nameof(currencyRiskLimits));
        }

        return limits;
    }

    private static string? NormalizeDefaultCurrency(string? defaultCurrencyCode, IReadOnlyCollection<string> allowedCurrencyCodes)
    {
        if (string.IsNullOrWhiteSpace(defaultCurrencyCode))
        {
            return null;
        }

        string normalizedDefaultCurrency = NormalizeMoneyCurrencyCode(defaultCurrencyCode);
        if (!allowedCurrencyCodes.Contains(normalizedDefaultCurrency, StringComparer.Ordinal))
        {
            throw new ArgumentException("Default currency must be in the allowed currency set.", nameof(defaultCurrencyCode));
        }

        return normalizedDefaultCurrency;
    }

    private static string NormalizeMoneyCurrencyCode(string currencyCode)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency)
        {
            throw new ArgumentException("Paid-event policies require a monetary currency.", nameof(currencyCode));
        }

        return currency.Code;
    }

    private static void ValidateFarFutureThreshold(int? farFutureReviewThresholdDays)
    {
        if (farFutureReviewThresholdDays is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(farFutureReviewThresholdDays));
        }
    }
}

public sealed class PaidEventPolicyAllowedOrganizerKind
{
    private PaidEventPolicyAllowedOrganizerKind()
    {
    }

    private PaidEventPolicyAllowedOrganizerKind(Guid? tenantId, string policyScopeKey, Guid policyVersionId, int ordinal, ActorTypeEnum actorType)
    {
        TenantId = tenantId;
        PolicyScopeKey = policyScopeKey;
        PaidEventPolicyVersionId = policyVersionId;
        Ordinal = ordinal;
        ActorTypeId = (int)actorType;
    }

    public Guid? TenantId { get; private set; }

    public string PolicyScopeKey { get; private set; } = string.Empty;

    public Guid PaidEventPolicyVersionId { get; private set; }

    public int Ordinal { get; private set; }

    public int ActorTypeId { get; private set; }

    public ActorTypeEnum ActorType => (ActorTypeEnum)ActorTypeId;

    internal static PaidEventPolicyAllowedOrganizerKind Create(Guid? tenantId, string policyScopeKey, Guid policyVersionId, int ordinal, ActorTypeEnum actorType) =>
        new(tenantId, policyScopeKey, policyVersionId, ordinal, actorType);
}

public sealed class PaidEventPolicyAllowedCurrency
{
    private PaidEventPolicyAllowedCurrency()
    {
    }

    private PaidEventPolicyAllowedCurrency(Guid? tenantId, string policyScopeKey, Guid policyVersionId, int ordinal, string currencyCode)
    {
        TenantId = tenantId;
        PolicyScopeKey = policyScopeKey;
        PaidEventPolicyVersionId = policyVersionId;
        Ordinal = ordinal;
        CurrencyCode = currencyCode;
    }

    public Guid? TenantId { get; private set; }

    public string PolicyScopeKey { get; private set; } = string.Empty;

    public Guid PaidEventPolicyVersionId { get; private set; }

    public int Ordinal { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    internal static PaidEventPolicyAllowedCurrency Create(Guid? tenantId, string policyScopeKey, Guid policyVersionId, int ordinal, string currencyCode) =>
        new(tenantId, policyScopeKey, policyVersionId, ordinal, currencyCode);
}

public sealed class PaidEventPolicyRefundProtection
{
    private PaidEventPolicyRefundProtection()
    {
    }

    private PaidEventPolicyRefundProtection(Guid? tenantId, string policyScopeKey, Guid policyVersionId, int ordinal, PaidEventRefundProtection protection)
    {
        TenantId = tenantId;
        PolicyScopeKey = policyScopeKey;
        PaidEventPolicyVersionId = policyVersionId;
        Ordinal = ordinal;
        RefundProtectionId = (int)protection;
    }

    public Guid? TenantId { get; private set; }

    public string PolicyScopeKey { get; private set; } = string.Empty;

    public Guid PaidEventPolicyVersionId { get; private set; }

    public int Ordinal { get; private set; }

    public int RefundProtectionId { get; private set; }

    public PaidEventRefundProtection Protection => (PaidEventRefundProtection)RefundProtectionId;

    internal static PaidEventPolicyRefundProtection Create(Guid? tenantId, string policyScopeKey, Guid policyVersionId, int ordinal, PaidEventRefundProtection protection) =>
        new(tenantId, policyScopeKey, policyVersionId, ordinal, protection);
}

public sealed class PaidEventPolicyCurrencyRiskLimitRow
{
    private PaidEventPolicyCurrencyRiskLimitRow()
    {
    }

    private PaidEventPolicyCurrencyRiskLimitRow(
        string policyScopeKey,
        Guid? tenantId,
        Guid policyVersionId,
        int ordinal,
        PaidEventPolicyCurrencyRiskLimit limit)
    {
        TenantId = tenantId;
        PolicyScopeKey = policyScopeKey;
        PaidEventPolicyVersionId = policyVersionId;
        Ordinal = ordinal;
        CurrencyCode = limit.CurrencyCode;
        PerEventSalesCeilingMinor = limit.PerEventSalesCeilingMinor;
        PerEventSalesCountCeiling = limit.PerEventSalesCountCeiling;
        RollingOrganizerSalesCeilingMinor = limit.RollingOrganizerSalesCeilingMinor;
        RollingOrganizerSalesCountCeiling = limit.RollingOrganizerSalesCountCeiling;
        RollingOrganizerWindowDays = limit.RollingOrganizerWindowDays;
        HighValueReviewThresholdMinor = limit.HighValueReviewThresholdMinor;
    }

    public Guid? TenantId { get; private set; }

    public string PolicyScopeKey { get; private set; } = string.Empty;

    public Guid PaidEventPolicyVersionId { get; private set; }

    public int Ordinal { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public long? PerEventSalesCeilingMinor { get; private set; }

    public int? PerEventSalesCountCeiling { get; private set; }

    public long? RollingOrganizerSalesCeilingMinor { get; private set; }

    public int? RollingOrganizerSalesCountCeiling { get; private set; }

    public int? RollingOrganizerWindowDays { get; private set; }

    public long? HighValueReviewThresholdMinor { get; private set; }

    internal static PaidEventPolicyCurrencyRiskLimitRow Create(Guid? tenantId, string policyScopeKey, Guid policyVersionId, int ordinal, PaidEventPolicyCurrencyRiskLimit limit) =>
        new(policyScopeKey, tenantId, policyVersionId, ordinal, limit);

    internal PaidEventPolicyCurrencyRiskLimit ToValueObject() => PaidEventPolicyCurrencyRiskLimit.Create(
        CurrencyCode,
        PerEventSalesCeilingMinor,
        PerEventSalesCountCeiling,
        RollingOrganizerSalesCeilingMinor,
        RollingOrganizerSalesCountCeiling,
        RollingOrganizerWindowDays,
        HighValueReviewThresholdMinor);
}
