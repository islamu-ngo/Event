using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Domain;

namespace Explore.Application.Features.PaidEventPolicies;

internal static class PaidEventPolicyMapper
{
    internal static PaidEventPolicyDto ToDto(PaidEventPolicyVersion policy) => new()
    {
        Id = policy.Id,
        TenantId = policy.TenantId,
        VersionNumber = policy.VersionNumber,
        IsActive = policy.IsActive,
        IsPaymentsEnabled = policy.IsPaymentsEnabled,
        RequiresLocalVerification = policy.RequiresLocalVerification,
        AllowedOrganizerKindIds = policy.AllowedOrganizerKinds.Select(kind => (int)kind).ToArray(),
        AllowedCurrencyCodes = policy.AllowedCurrencyCodes.ToArray(),
        DefaultCurrencyCode = policy.DefaultCurrencyCode,
        RefundProtectionIds = policy.RefundProtections.Select(protection => (int)protection).ToArray(),
        CurrencyRiskLimits = policy.CurrencyRiskLimits.Select(limit => new PaidEventPolicyCurrencyRiskLimitDto
        {
            CurrencyCode = limit.CurrencyCode,
            PerEventSalesCeilingMinor = limit.PerEventSalesCeilingMinor,
            PerEventSalesCountCeiling = limit.PerEventSalesCountCeiling,
            RollingOrganizerSalesCeilingMinor = limit.RollingOrganizerSalesCeilingMinor,
            RollingOrganizerSalesCountCeiling = limit.RollingOrganizerSalesCountCeiling,
            RollingOrganizerWindowDays = limit.RollingOrganizerWindowDays,
            HighValueReviewThresholdMinor = limit.HighValueReviewThresholdMinor
        }).ToArray(),
        RequiresFirstPaidEventReview = policy.RequiresFirstPaidEventReview,
        FarFutureReviewThresholdDays = policy.FarFutureReviewThresholdDays
    };
}
