using Explore.Application.Features.ControlPlane.Plans;
using Explore.Domain.Constants;

namespace Event.Application.UnitTests.Features.ControlPlane.Plans;

public sealed class TenantPlanDraftValidatorTests
{
    [Test]
    public async Task Validate_WhenDraftDefinesPricedTierWithSafeSettingsAndQuotas_ReturnsValid()
    {
        var draft = CreateValidDraft();

        TenantPlanValidationResult result = TenantPlanDraftValidator.Validate(draft);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task Validate_WhenPricingCurrencyIsMissing_ReturnsPricingError()
    {
        var draft = CreateValidDraft() with
        {
            Pricing = new TenantPlanPricing(29m, string.Empty, TenantPlanBillingPeriods.Monthly)
        };

        TenantPlanValidationResult result = TenantPlanDraftValidator.Validate(draft);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains(TenantPlanValidationErrorCodes.MissingPricingCurrency);
    }

    [Test]
    public async Task Validate_WhenSettingKeyIsUnknown_ReturnsUnsupportedSettingError()
    {
        var draft = CreateValidDraft() with
        {
            SettingOverrides =
            [
                new TenantPlanSettingOverride("unknown.feature.enabled", "true", IsLocked: true)
            ]
        };

        TenantPlanValidationResult result = TenantPlanDraftValidator.Validate(draft);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains(TenantPlanValidationErrorCodes.UnsupportedSettingKey);
    }

    [Test]
    public async Task Validate_WhenSettingKeyIsSensitive_ReturnsSensitiveSettingError()
    {
        var draft = CreateValidDraft() with
        {
            SettingOverrides =
            [
                new TenantPlanSettingOverride(
                    InfrastructureSecretSettingKeys.Reporting.OspreyApiKey,
                    "\"secret\"",
                    IsLocked: true)
            ]
        };

        TenantPlanValidationResult result = TenantPlanDraftValidator.Validate(draft);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains(TenantPlanValidationErrorCodes.SensitiveSettingKey);
    }

    [Test]
    public async Task Validate_WhenQuotaLimitIsNegative_ReturnsQuotaError()
    {
        var draft = CreateValidDraft() with
        {
            QuotaLimits =
            [
                new TenantPlanQuotaLimit(TenantPlanQuotaKeys.StorageBytes, -1)
            ]
        };

        TenantPlanValidationResult result = TenantPlanDraftValidator.Validate(draft);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Select(error => error.Code)).Contains(TenantPlanValidationErrorCodes.NegativeQuotaLimit);
    }

    [Test]
    public async Task Diff_WhenPlanChangesSettingValueAndLock_ReturnsSettingChange()
    {
        var current = new TenantPlanEffectiveConfiguration(
        [
            new TenantPlanEffectiveSetting(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, "1073741824", IsLocked: false)
        ]);
        var draft = CreateValidDraft() with
        {
            SettingOverrides =
            [
                new TenantPlanSettingOverride(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, "5368709120", IsLocked: true)
            ]
        };

        TenantPlanDiffResult diff = TenantPlanDiffService.Diff(current, draft);

        TenantPlanSettingChange change = diff.SettingChanges.Single();
        await Assert.That(change.Key).IsEqualTo(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes);
        await Assert.That(change.ChangeType).IsEqualTo(TenantPlanChangeType.Changed);
        await Assert.That(change.LockChanged).IsTrue();
        await Assert.That(change.BeforeValue).IsEqualTo("1073741824");
        await Assert.That(change.AfterValue).IsEqualTo("5368709120");
    }

    private static TenantPlanDraft CreateValidDraft() => new(
        Key: "community",
        Name: "Community",
        Pricing: new TenantPlanPricing(29m, "EUR", TenantPlanBillingPeriods.Monthly),
        IsActiveForProvisioning: true,
        SettingOverrides:
        [
            new TenantPlanSettingOverride(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, "5368709120", IsLocked: true),
            new TenantPlanSettingOverride(GovernanceSettingKeys.AiAssistant.Enabled, "true", IsLocked: false)
        ],
        QuotaLimits:
        [
            new TenantPlanQuotaLimit(TenantPlanQuotaKeys.StorageBytes, 5L * 1024 * 1024 * 1024),
            new TenantPlanQuotaLimit(TenantPlanQuotaKeys.AiDailyTenantMessages, 1000)
        ]);
}
