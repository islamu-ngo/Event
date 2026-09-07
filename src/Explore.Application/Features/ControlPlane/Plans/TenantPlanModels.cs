using Explore.Domain.Settings;

namespace Explore.Application.Features.ControlPlane.Plans;

public sealed record TenantPlanDraft(
    string Key,
    string Name,
    TenantPlanPricing Pricing,
    bool IsActiveForProvisioning,
    IReadOnlyList<TenantPlanSettingOverride> SettingOverrides,
    IReadOnlyList<TenantPlanQuotaLimit> QuotaLimits);

public sealed record TenantPlanPricing(decimal Amount, string CurrencyCode, string BillingPeriod);

public sealed record TenantPlanSettingOverride(string Key, string JsonValue, bool IsLocked);

public sealed record TenantPlanQuotaLimit(string Key, long Limit);

public sealed record TenantPlanValidationError(string Code, string Target, string Message);

public sealed record TenantPlanValidationResult(IReadOnlyList<TenantPlanValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class TenantPlanBillingPeriods
{
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
}

public static class TenantPlanQuotaKeys
{
    public const string StorageBytes = "storage.bytes";
    public const string AiDailyTenantMessages = "ai.daily_tenant_messages";
    public const string ExternalApiMonthlyCredits = "external_api.monthly_credits";
    public const string CustomPropertyDefinitionsPerTemplate = "custom_properties.definitions_per_template";

    private static readonly HashSet<string> Supported =
    [
        StorageBytes,
        AiDailyTenantMessages,
        ExternalApiMonthlyCredits,
        CustomPropertyDefinitionsPerTemplate
    ];

    public static bool IsSupported(string key) => Supported.Contains(key);
}

public static class TenantPlanValidationErrorCodes
{
    public const string MissingPricingCurrency = "missing_pricing_currency";
    public const string MissingBillingPeriod = "missing_billing_period";
    public const string NegativePrice = "negative_price";
    public const string UnsupportedSettingKey = "unsupported_setting_key";
    public const string SensitiveSettingKey = "sensitive_setting_key";
    public const string UnsupportedQuotaKey = "unsupported_quota_key";
    public const string NegativeQuotaLimit = "negative_quota_limit";
}

public static class TenantPlanDraftValidator
{
    public static TenantPlanValidationResult Validate(TenantPlanDraft draft)
    {
        var errors = new List<TenantPlanValidationError>();

        ValidatePricing(draft.Pricing, errors);
        ValidateSettings(draft.SettingOverrides, errors);
        ValidateQuotas(draft.QuotaLimits, errors);

        return new TenantPlanValidationResult(errors);
    }

    private static void ValidatePricing(
        TenantPlanPricing pricing,
        ICollection<TenantPlanValidationError> errors)
    {
        if (pricing.Amount < 0)
        {
            errors.Add(new(
                TenantPlanValidationErrorCodes.NegativePrice,
                nameof(TenantPlanDraft.Pricing),
                "Tenant plan price cannot be negative."));
        }

        if (string.IsNullOrWhiteSpace(pricing.CurrencyCode))
        {
            errors.Add(new(
                TenantPlanValidationErrorCodes.MissingPricingCurrency,
                nameof(TenantPlanPricing.CurrencyCode),
                "Tenant plan pricing requires a currency code."));
        }

        if (string.IsNullOrWhiteSpace(pricing.BillingPeriod))
        {
            errors.Add(new(
                TenantPlanValidationErrorCodes.MissingBillingPeriod,
                nameof(TenantPlanPricing.BillingPeriod),
                "Tenant plan pricing requires a billing period."));
        }
    }

    private static void ValidateSettings(
        IReadOnlyList<TenantPlanSettingOverride> overrides,
        ICollection<TenantPlanValidationError> errors)
    {
        foreach (TenantPlanSettingOverride setting in overrides)
        {
            SettingDefinition? definition = SettingRegistry.Get(setting.Key);
            if (definition is null)
            {
                errors.Add(new(
                    TenantPlanValidationErrorCodes.UnsupportedSettingKey,
                    setting.Key,
                    "Tenant plan setting is not registered."));
                continue;
            }

            if (definition.IsSensitive)
            {
                errors.Add(new(
                    TenantPlanValidationErrorCodes.SensitiveSettingKey,
                    setting.Key,
                    "Tenant plans cannot carry secret or credential setting values."));
            }
        }
    }

    private static void ValidateQuotas(
        IReadOnlyList<TenantPlanQuotaLimit> quotas,
        ICollection<TenantPlanValidationError> errors)
    {
        foreach (TenantPlanQuotaLimit quota in quotas)
        {
            if (!TenantPlanQuotaKeys.IsSupported(quota.Key))
            {
                errors.Add(new(
                    TenantPlanValidationErrorCodes.UnsupportedQuotaKey,
                    quota.Key,
                    "Tenant plan quota key is not supported."));
            }

            if (quota.Limit < 0)
            {
                errors.Add(new(
                    TenantPlanValidationErrorCodes.NegativeQuotaLimit,
                    quota.Key,
                    "Tenant plan quota limits cannot be negative."));
            }
        }
    }
}

public sealed record TenantPlanEffectiveConfiguration(IReadOnlyList<TenantPlanEffectiveSetting> Settings);

public sealed record TenantPlanEffectiveSetting(string Key, string JsonValue, bool IsLocked);

public sealed record TenantPlanDiffResult(IReadOnlyList<TenantPlanSettingChange> SettingChanges);

public sealed record TenantPlanSettingChange(
    string Key,
    TenantPlanChangeType ChangeType,
    string? BeforeValue,
    string? AfterValue,
    bool LockChanged);

public enum TenantPlanChangeType
{
    Added,
    Changed
}

public static class TenantPlanDiffService
{
    public static TenantPlanDiffResult Diff(TenantPlanEffectiveConfiguration current, TenantPlanDraft draft)
    {
        Dictionary<string, TenantPlanEffectiveSetting> currentByKey = current.Settings.ToDictionary(setting => setting.Key);
        var changes = new List<TenantPlanSettingChange>();

        foreach (TenantPlanSettingOverride target in draft.SettingOverrides)
        {
            if (!currentByKey.TryGetValue(target.Key, out TenantPlanEffectiveSetting? before))
            {
                changes.Add(new(
                    target.Key,
                    TenantPlanChangeType.Added,
                    BeforeValue: null,
                    target.JsonValue,
                    LockChanged: target.IsLocked));
                continue;
            }

            bool valueChanged = !string.Equals(before.JsonValue, target.JsonValue, StringComparison.Ordinal);
            bool lockChanged = before.IsLocked != target.IsLocked;
            if (!valueChanged && !lockChanged)
            {
                continue;
            }

            changes.Add(new(
                target.Key,
                TenantPlanChangeType.Changed,
                before.JsonValue,
                target.JsonValue,
                lockChanged));
        }

        return new TenantPlanDiffResult(changes);
    }
}
