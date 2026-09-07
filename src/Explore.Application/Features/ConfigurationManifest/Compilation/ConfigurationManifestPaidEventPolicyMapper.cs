namespace Explore.Application.Features.ConfigurationManifest.Compilation;

using Explore.Application.DTOs.PaidEventPolicies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Domain;
using Explore.Domain.Enums;

public static class ConfigurationManifestPaidEventPolicyMapper
{
    public static PaidEventPolicyVersion CreateInstanceCandidate(
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        PaidEventPolicyVersion baseline = PaidEventPolicyVersion.CreateDefaultInstance();
        return baseline.CreateRevision(
            payload.IsPaymentsEnabled,
            payload.AllowedOrganizerKindIds.Select(id => (ActorTypeEnum)id),
            payload.RequiresLocalVerification,
            payload.AllowedCurrencyCodes,
            payload.DefaultCurrencyCode,
            payload.RefundProtectionIds.Select(id => (PaidEventRefundProtection)id),
            payload.CurrencyRiskLimits.Select(limit => PaidEventPolicyCurrencyRiskLimit.Create(
                limit.CurrencyCode,
                limit.PerEventSalesCeilingMinor,
                limit.PerEventSalesCountCeiling,
                limit.RollingOrganizerSalesCeilingMinor,
                limit.RollingOrganizerSalesCountCeiling,
                limit.RollingOrganizerWindowDays,
                limit.HighValueReviewThresholdMinor)),
            payload.RequiresFirstPaidEventReview,
            payload.FarFutureReviewThresholdDays);
    }

    public static PaidEventPolicyVersion CreateTenantCandidate(
        Guid tenantId,
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return PaidEventPolicyVersion.CreateTenant(
            tenantId,
            payload.IsPaymentsEnabled,
            payload.AllowedOrganizerKindIds.Select(id => (ActorTypeEnum)id),
            payload.RequiresLocalVerification,
            payload.AllowedCurrencyCodes,
            payload.DefaultCurrencyCode,
            payload.RefundProtectionIds.Select(id => (PaidEventRefundProtection)id),
            payload.CurrencyRiskLimits.Select(limit => PaidEventPolicyCurrencyRiskLimit.Create(
                limit.CurrencyCode,
                limit.PerEventSalesCeilingMinor,
                limit.PerEventSalesCountCeiling,
                limit.RollingOrganizerSalesCeilingMinor,
                limit.RollingOrganizerSalesCountCeiling,
                limit.RollingOrganizerWindowDays,
                limit.HighValueReviewThresholdMinor)),
            payload.RequiresFirstPaidEventReview,
            payload.FarFutureReviewThresholdDays);
    }

    public static RevisePaidEventPolicyDto ToRevisionDto(
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new RevisePaidEventPolicyDto
        {
            IsPaymentsEnabled = payload.IsPaymentsEnabled,
            AllowedOrganizerKindIds = payload.AllowedOrganizerKindIds.ToArray(),
            RequiresLocalVerification = payload.RequiresLocalVerification,
            AllowedCurrencyCodes = payload.AllowedCurrencyCodes.ToArray(),
            DefaultCurrencyCode = payload.DefaultCurrencyCode,
            RefundProtectionIds = payload.RefundProtectionIds.ToArray(),
            CurrencyRiskLimits = payload.CurrencyRiskLimits
                .Select(limit => new PaidEventPolicyCurrencyRiskLimitDto
                {
                    CurrencyCode = limit.CurrencyCode,
                    PerEventSalesCeilingMinor = limit.PerEventSalesCeilingMinor,
                    PerEventSalesCountCeiling = limit.PerEventSalesCountCeiling,
                    RollingOrganizerSalesCeilingMinor =
                        limit.RollingOrganizerSalesCeilingMinor,
                    RollingOrganizerSalesCountCeiling =
                        limit.RollingOrganizerSalesCountCeiling,
                    RollingOrganizerWindowDays = limit.RollingOrganizerWindowDays,
                    HighValueReviewThresholdMinor = limit.HighValueReviewThresholdMinor
                })
                .ToArray(),
            RequiresFirstPaidEventReview = payload.RequiresFirstPaidEventReview,
            FarFutureReviewThresholdDays = payload.FarFutureReviewThresholdDays
        };
    }

    public static ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 ToManifestPayload(
        PaidEventPolicyVersion policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new ConfigurationManifestPaidEventPolicyPayloadV1Alpha2
        {
            IsPaymentsEnabled = policy.IsPaymentsEnabled,
            AllowedOrganizerKindIds = policy.AllowedOrganizerKinds
                .Select(kind => (int)kind)
                .ToArray(),
            RequiresLocalVerification = policy.RequiresLocalVerification,
            AllowedCurrencyCodes = policy.AllowedCurrencyCodes.ToArray(),
            DefaultCurrencyCode = policy.DefaultCurrencyCode,
            RefundProtectionIds = policy.RefundProtections
                .Select(protection => (int)protection)
                .ToArray(),
            CurrencyRiskLimits = policy.CurrencyRiskLimits
                .Select(limit =>
                    new ConfigurationManifestPaidEventPolicyCurrencyRiskLimitV1Alpha2
                    {
                        CurrencyCode = limit.CurrencyCode,
                        PerEventSalesCeilingMinor = limit.PerEventSalesCeilingMinor,
                        PerEventSalesCountCeiling = limit.PerEventSalesCountCeiling,
                        RollingOrganizerSalesCeilingMinor =
                            limit.RollingOrganizerSalesCeilingMinor,
                        RollingOrganizerSalesCountCeiling =
                            limit.RollingOrganizerSalesCountCeiling,
                        RollingOrganizerWindowDays = limit.RollingOrganizerWindowDays,
                        HighValueReviewThresholdMinor =
                            limit.HighValueReviewThresholdMinor
                    })
                .ToArray(),
            RequiresFirstPaidEventReview = policy.RequiresFirstPaidEventReview,
            FarFutureReviewThresholdDays = policy.FarFutureReviewThresholdDays
        };
    }
}
