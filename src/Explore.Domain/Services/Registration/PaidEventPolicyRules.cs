using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Domain.Services.Registration;

public static class PaidEventPolicyRules
{
    public static void ValidateTenantPolicy(PaidEventPolicyVersion instancePolicy, PaidEventPolicyVersion tenantPolicy)
    {
        ArgumentNullException.ThrowIfNull(instancePolicy);
        ArgumentNullException.ThrowIfNull(tenantPolicy);

        if (instancePolicy.TenantId is not null || tenantPolicy.TenantId is null)
        {
            throw new ArgumentException("Paid-event policy narrowing requires an instance policy and a tenant policy.", nameof(tenantPolicy));
        }

        if (!instancePolicy.IsActive || !tenantPolicy.IsActive)
        {
            throw new InvalidOperationException("Only active paid-event policy versions can be resolved.");
        }

        if (tenantPolicy.IsPaymentsEnabled && !instancePolicy.IsPaymentsEnabled)
        {
            throw new InvalidOperationException("Tenant paid events cannot be enabled when the instance policy is disabled.");
        }

        if (instancePolicy.RequiresLocalVerification && !tenantPolicy.RequiresLocalVerification)
        {
            throw new InvalidOperationException("Tenant paid-event policy cannot weaken the instance local-verification floor.");
        }

        ValidateOrganizerKinds(instancePolicy, tenantPolicy);
        ValidateCurrencyNarrowing(instancePolicy, tenantPolicy);
        ValidateRefundNarrowing(instancePolicy, tenantPolicy);
        ValidateCurrencyRiskLimits(instancePolicy, tenantPolicy);
        if (instancePolicy.RequiresFirstPaidEventReview && !tenantPolicy.RequiresFirstPaidEventReview)
        {
            throw new InvalidOperationException("Tenant paid-event policy cannot disable the instance first-paid-event review requirement.");
        }

        ValidateCeiling(instancePolicy.FarFutureReviewThresholdDays, tenantPolicy.FarFutureReviewThresholdDays, "Tenant far-future review threshold cannot exceed or remove the instance threshold.");
    }

    public static bool IsOrganizerKindEligible(ActorTypeEnum actorType) =>
        actorType is ActorTypeEnum.Organization or ActorTypeEnum.Group or ActorTypeEnum.User;

    public static IReadOnlyList<string> GetEffectiveCurrencyCodes(PaidEventPolicyVersion instancePolicy, PaidEventPolicyVersion? tenantPolicy)
    {
        ArgumentNullException.ThrowIfNull(instancePolicy);
        if (!instancePolicy.IsActive || !instancePolicy.IsPaymentsEnabled || instancePolicy.TenantId is not null)
        {
            return [];
        }

        if (tenantPolicy is not null)
        {
            if (!tenantPolicy.IsActive || !tenantPolicy.IsPaymentsEnabled || tenantPolicy.TenantId is null)
            {
                return [];
            }

            try
            {
                ValidateTenantPolicy(instancePolicy, tenantPolicy);
            }
            catch (ArgumentException)
            {
                return [];
            }
            catch (InvalidOperationException)
            {
                return [];
            }
        }

        HashSet<string>? tenantCurrencyCodes = tenantPolicy?.AllowedCurrencyCodes.ToHashSet(StringComparer.Ordinal);
        return instancePolicy.AllowedCurrencyCodes
            .Where(currencyCode => tenantCurrencyCodes is null || tenantCurrencyCodes.Contains(currencyCode))
            .ToArray();
    }

    public static string? ResolveConfirmedCatalogCurrency(
        PaidEventPolicyVersion instancePolicy,
        PaidEventPolicyVersion? tenantPolicy,
        string? suggestedCurrencyCode,
        string? confirmedCurrencyCode)
    {
        if (string.IsNullOrWhiteSpace(confirmedCurrencyCode))
        {
            _ = suggestedCurrencyCode;
            return null;
        }

        string normalizedConfirmedCurrencyCode;
        try
        {
            CurrencyMetadata currency = CurrencyMetadata.Get(confirmedCurrencyCode);
            if (currency.IsNoCurrency)
            {
                return null;
            }

            normalizedConfirmedCurrencyCode = currency.Code;
        }
        catch (ArgumentException)
        {
            return null;
        }

        return GetEffectiveCurrencyCodes(instancePolicy, tenantPolicy).Contains(normalizedConfirmedCurrencyCode, StringComparer.Ordinal)
            ? normalizedConfirmedCurrencyCode
            : null;
    }

    private static void ValidateOrganizerKinds(PaidEventPolicyVersion instancePolicy, PaidEventPolicyVersion tenantPolicy)
    {
        if (instancePolicy.AllowedOrganizerKinds.Any(static kind => !IsOrganizerKindEligible(kind))
            || tenantPolicy.AllowedOrganizerKinds.Any(static kind => !IsOrganizerKindEligible(kind)))
        {
            throw new InvalidOperationException("Paid-event organizer kinds are limited to organization, group, or user actors.");
        }

        if (tenantPolicy.AllowedOrganizerKinds.Any(kind => !instancePolicy.AllowedOrganizerKinds.Contains(kind)))
        {
            throw new InvalidOperationException("Tenant paid-event policy cannot add organizer kinds outside the instance ceiling.");
        }
    }

    private static void ValidateCurrencyNarrowing(PaidEventPolicyVersion instancePolicy, PaidEventPolicyVersion tenantPolicy)
    {
        string[] expectedTenantOrder = instancePolicy.AllowedCurrencyCodes
            .Where(tenantPolicy.AllowedCurrencyCodes.Contains)
            .ToArray();
        if (!tenantPolicy.AllowedCurrencyCodes.SequenceEqual(expectedTenantOrder, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Tenant paid-event policy cannot add currencies or change instance currency order.");
        }
    }

    private static void ValidateRefundNarrowing(PaidEventPolicyVersion instancePolicy, PaidEventPolicyVersion tenantPolicy)
    {
        if (instancePolicy.RefundProtections.Any(protection => !tenantPolicy.RefundProtections.Contains(protection)))
        {
            throw new InvalidOperationException("Tenant paid-event policy cannot weaken the instance refund protection floor.");
        }
    }

    private static void ValidateCurrencyRiskLimits(PaidEventPolicyVersion instancePolicy, PaidEventPolicyVersion tenantPolicy)
    {
        if (tenantPolicy.CurrencyRiskLimits.Any(limit => !tenantPolicy.AllowedCurrencyCodes.Contains(limit.CurrencyCode, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("Tenant paid-event risk limits must stay inside the tenant currency subset.");
        }

        foreach (PaidEventPolicyCurrencyRiskLimit instanceLimit in instancePolicy.CurrencyRiskLimits)
        {
            if (!tenantPolicy.AllowedCurrencyCodes.Contains(instanceLimit.CurrencyCode, StringComparer.Ordinal))
            {
                continue;
            }

            PaidEventPolicyCurrencyRiskLimit? tenantLimit = tenantPolicy.CurrencyRiskLimits.SingleOrDefault(limit => limit.CurrencyCode == instanceLimit.CurrencyCode);
            ValidateCeiling(instanceLimit.PerEventSalesCeilingMinor, tenantLimit?.PerEventSalesCeilingMinor, "Tenant per-event sales ceiling cannot exceed or remove the instance ceiling.");
            ValidateCeiling(instanceLimit.PerEventSalesCountCeiling, tenantLimit?.PerEventSalesCountCeiling, "Tenant per-event sales-count ceiling cannot exceed or remove the instance ceiling.");
            ValidateCeiling(instanceLimit.RollingOrganizerSalesCeilingMinor, tenantLimit?.RollingOrganizerSalesCeilingMinor, "Tenant rolling organizer sales ceiling cannot exceed or remove the instance ceiling.");
            ValidateCeiling(instanceLimit.RollingOrganizerSalesCountCeiling, tenantLimit?.RollingOrganizerSalesCountCeiling, "Tenant rolling organizer sales-count ceiling cannot exceed or remove the instance ceiling.");
            ValidateWindow(instanceLimit.RollingOrganizerWindowDays, tenantLimit?.RollingOrganizerWindowDays);
            ValidateCeiling(instanceLimit.HighValueReviewThresholdMinor, tenantLimit?.HighValueReviewThresholdMinor, "Tenant high-value review threshold cannot exceed or remove the instance threshold.");
        }
    }

    private static void ValidateWindow(int? instanceWindow, int? tenantWindow)
    {
        if (instanceWindow.HasValue && (!tenantWindow.HasValue || tenantWindow.Value < instanceWindow.Value))
        {
            throw new InvalidOperationException("Tenant rolling organizer window cannot shorten or remove the instance window.");
        }
    }

    private static void ValidateCeiling<T>(T? instanceCeiling, T? tenantCeiling, string message)
        where T : struct, IComparable<T>
    {
        if (instanceCeiling.HasValue && (!tenantCeiling.HasValue || tenantCeiling.Value.CompareTo(instanceCeiling.Value) > 0))
        {
            throw new InvalidOperationException(message);
        }
    }
}
