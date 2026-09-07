using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.PaidEventPolicies;

public sealed record PaidEventPolicyDto
{
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public Guid? TenantId { get; init; }
    public int VersionNumber { get; init; }
    public bool IsActive { get; init; }
    public bool IsPaymentsEnabled { get; init; }
    public bool RequiresLocalVerification { get; init; }
    public IReadOnlyList<int> AllowedOrganizerKindIds { get; init; } = [];
    public IReadOnlyList<string> AllowedCurrencyCodes { get; init; } = [];
    public string? DefaultCurrencyCode { get; init; }
    public IReadOnlyList<int> RefundProtectionIds { get; init; } = [];
    public IReadOnlyList<PaidEventPolicyCurrencyRiskLimitDto> CurrencyRiskLimits { get; init; } = [];
    public bool RequiresFirstPaidEventReview { get; init; }
    public int? FarFutureReviewThresholdDays { get; init; }
}

public sealed record TenantPaidEventPolicyConfigurationDto
{
    [JsonIgnore]
    public Guid TenantId { get; init; }
    public PaidEventPolicyDto ActiveInstanceCeiling { get; init; } = default!;
    public PaidEventPolicyDto? ActiveTenantOverride { get; init; }
    public PaidEventPolicyDto EffectivePolicy { get; init; } = default!;
    public PaidEventPolicyAuthorityDto Authority { get; init; } = default!;
}

public sealed record PaidEventPolicyAuthorityDto
{
    public int InstancePolicyVersion { get; init; }
    public bool EffectiveValuesInherited { get; init; }
    public bool HasTenantNarrowing { get; init; }
    public IReadOnlyList<string> ManifestOwnedFields { get; init; } = [];
    public IReadOnlyList<string> SovereignLockedFields { get; init; } = [];
}

public static class PaidEventPolicyAuthorityMetadata
{
    public static IReadOnlyList<string> ManifestOwnedFields { get; } =
        Array.AsReadOnly(
        [
            "allowedCurrencyCodes",
            "allowedOrganizerKindIds",
            "currencyRiskLimits",
            "defaultCurrencyCode",
            "farFutureReviewThresholdDays",
            "isPaymentsEnabled",
            "refundProtectionIds",
            "requiresFirstPaidEventReview",
            "requiresLocalVerification"
        ]);

    public static IReadOnlyList<string> SovereignLockedFields { get; } =
        Array.AsReadOnly(
        [
            "buyerAcceptance",
            "chargeType",
            "connectedAccounts",
            "disputeHandling",
            "liability",
            "negativeBalances",
            "officialOrigin",
            "officialStatus",
            "operatorIdentity",
            "providerCredentials",
            "providerHandoff",
            "providerProfiles",
            "reconciliation",
            "refundExecution",
            "saleControl"
        ]);
}

public sealed record PaidEventPolicyCurrencyRiskLimitDto
{
    public string CurrencyCode { get; init; } = string.Empty;
    public long? PerEventSalesCeilingMinor { get; init; }
    public int? PerEventSalesCountCeiling { get; init; }
    public long? RollingOrganizerSalesCeilingMinor { get; init; }
    public int? RollingOrganizerSalesCountCeiling { get; init; }
    public int? RollingOrganizerWindowDays { get; init; }
    public long? HighValueReviewThresholdMinor { get; init; }
}

public sealed record RevisePaidEventPolicyDto
{
    public bool IsPaymentsEnabled { get; init; }
    public IReadOnlyList<int> AllowedOrganizerKindIds { get; init; } = [];
    public bool RequiresLocalVerification { get; init; }
    public IReadOnlyList<string> AllowedCurrencyCodes { get; init; } = [];
    public string? DefaultCurrencyCode { get; init; }
    public IReadOnlyList<int> RefundProtectionIds { get; init; } = [];
    public IReadOnlyList<PaidEventPolicyCurrencyRiskLimitDto> CurrencyRiskLimits { get; init; } = [];
    public bool RequiresFirstPaidEventReview { get; init; }
    public int? FarFutureReviewThresholdDays { get; init; }
}
