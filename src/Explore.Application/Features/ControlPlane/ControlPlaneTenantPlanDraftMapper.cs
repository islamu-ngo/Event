using Explore.Application.Features.ControlPlane.Plans;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.ControlPlane;

internal static class ControlPlaneTenantPlanDraftMapper
{
    public static TenantPlan ToPlan(TenantPlanDraft draft)
    {
        var plan = new TenantPlan
        {
            Id = Guid.CreateVersion7(),
            Key = draft.Key.Trim(),
            DisplayName = draft.Name.Trim()
        };

        plan.Versions.Add(ToVersion(plan, draft, versionNumber: 1, TenantPlanStatusEnum.Draft));
        return plan;
    }

    public static TenantPlanVersion ToVersion(
        TenantPlan plan,
        TenantPlanDraft draft,
        int versionNumber,
        TenantPlanStatusEnum status)
    {
        var version = new TenantPlanVersion
        {
            Id = Guid.CreateVersion7(),
            TenantPlan = plan,
            TenantPlanId = plan.Id,
            VersionNumber = versionNumber,
            TenantPlanStatusId = (int)status,
            PriceAmount = draft.Pricing.Amount,
            CurrencyCode = draft.Pricing.CurrencyCode.Trim().ToUpperInvariant(),
            BillingPeriod = draft.Pricing.BillingPeriod.Trim(),
            IsActiveForProvisioning = draft.IsActiveForProvisioning
        };

        foreach (TenantPlanSettingOverride setting in draft.SettingOverrides)
        {
            version.Settings.Add(new TenantPlanVersionSetting
            {
                Id = Guid.CreateVersion7(),
                TenantPlanVersion = version,
                TenantPlanVersionId = version.Id,
                SettingKey = setting.Key,
                JsonValue = setting.JsonValue,
                IsLocked = setting.IsLocked
            });
        }

        foreach (TenantPlanQuotaLimit quota in draft.QuotaLimits)
        {
            version.Quotas.Add(new TenantPlanVersionQuota
            {
                Id = Guid.CreateVersion7(),
                TenantPlanVersion = version,
                TenantPlanVersionId = version.Id,
                QuotaKey = quota.Key,
                Limit = quota.Limit
            });
        }

        return version;
    }

    public static void ApplyToVersion(TenantPlanVersion version, TenantPlanDraft draft)
    {
        ApplyPricing(version, draft.Pricing);
        version.IsActiveForProvisioning = draft.IsActiveForProvisioning;
        ReplaceSettings(version, draft.SettingOverrides);
        ReplaceQuotas(version, draft.QuotaLimits);
    }

    public static void ApplyPricing(TenantPlanVersion version, TenantPlanPricing pricing)
    {
        version.PriceAmount = pricing.Amount;
        version.CurrencyCode = pricing.CurrencyCode.Trim().ToUpperInvariant();
        version.BillingPeriod = pricing.BillingPeriod.Trim();
    }

    public static void ReplaceSettings(
        TenantPlanVersion version,
        IReadOnlyList<TenantPlanSettingOverride> settings)
    {
        version.Settings.Clear();

        foreach (TenantPlanSettingOverride setting in settings)
        {
            version.Settings.Add(new TenantPlanVersionSetting
            {
                Id = Guid.CreateVersion7(),
                TenantPlanVersion = version,
                TenantPlanVersionId = version.Id,
                SettingKey = setting.Key,
                JsonValue = setting.JsonValue,
                IsLocked = setting.IsLocked
            });
        }
    }

    public static void ReplaceQuotas(
        TenantPlanVersion version,
        IReadOnlyList<TenantPlanQuotaLimit> quotas)
    {
        version.Quotas.Clear();

        foreach (TenantPlanQuotaLimit quota in quotas)
        {
            version.Quotas.Add(new TenantPlanVersionQuota
            {
                Id = Guid.CreateVersion7(),
                TenantPlanVersion = version,
                TenantPlanVersionId = version.Id,
                QuotaKey = quota.Key,
                Limit = quota.Limit
            });
        }
    }
}
