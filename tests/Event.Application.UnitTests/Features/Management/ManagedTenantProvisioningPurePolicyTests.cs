using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents.Payloads;

namespace Event.Application.UnitTests.Features.Management;

public sealed class ManagedTenantProvisioningPurePolicyTests
{
    [Test]
    [Arguments("100", 100L, null)]
    [Arguments("100", 101L, "tenant_plan_quota_ceiling_exceeded")]
    [Arguments("\"100\"", 101L, "tenant_plan_quota_ceiling_exceeded")]
    [Arguments("invalid", 101L, null)]
    [Arguments(null, 101L, null)]
    public async Task StorageCeilingPreservesNumericJsonAndAbsentPolicySemantics(
        string? ceiling, long requested, string? expected)
    {
        var quota = new TenantPlanVersionQuota { QuotaKey = TenantPlanQuotaKeys.StorageBytes, Limit = requested };

        await Assert.That(TenantPlanStorageQuotaCeilingPolicy.Validate([quota], ceiling)).IsEqualTo(expected);
    }

    [Test]
    public async Task AnUnrelatedQuotaDoesNotBecomeAStorageQuota()
    {
        var quota = new TenantPlanVersionQuota { QuotaKey = "other", Limit = 1000 };

        await Assert.That(TenantPlanStorageQuotaCeilingPolicy.Validate([quota], "1")).IsNull();
    }

    [Test]
    public async Task SingleTenantBrandingDoesNotApplyMultiTenantGovernanceLocks()
    {
        var result = TenantBrandingSettingsDocumentLockService.AssessLockState(
            "\"SingleTenant\"", "false", true, true, true, true);

        await Assert.That(result).IsEqualTo(TenantBrandingSettingsDocumentLockState.AllowAll);
    }

    [Test]
    public async Task MultiTenantWhiteLabelingDisabledStillAllowsAnUnlockedDisplayName()
    {
        var result = TenantBrandingSettingsDocumentLockService.AssessLockState(
            "\"MultiTenant\"", "false", false, false, false, false);

        await Assert.That(result).IsEqualTo(new TenantBrandingSettingsDocumentLockState(true, false, false, false));
    }

    [Test]
    public async Task WhiteLabelingDoesNotOverrideIndividualBrandingLocks()
    {
        var result = TenantBrandingSettingsDocumentLockService.AssessLockState(
            "MultiTenant", "true", true, false, true, false);

        await Assert.That(result).IsEqualTo(new TenantBrandingSettingsDocumentLockState(false, true, false, true));
    }

    [Test]
    public async Task AnUnchangedLockedFieldIsAllowedButAChangedLockedFieldIsRefused()
    {
        var current = new BrandingSettings { DisplayName = "Original", LogoUrl = "https://example.test/logo" };
        var same = new BrandingSettings { DisplayName = " Original ", LogoUrl = "https://example.test/logo" };
        var changed = new BrandingSettings { DisplayName = "Changed", LogoUrl = "https://example.test/logo" };
        var locks = new TenantBrandingSettingsDocumentLockState(false, false, false, false);

        await Assert.That(TenantBrandingSettingsDocumentLockService.ValidateChanges(current, same, locks).Count).IsEqualTo(0);
        await Assert.That(TenantBrandingSettingsDocumentLockService.ValidateChanges(current, changed, locks).Single())
            .IsEqualTo("Display name cannot be changed because instance branding governance currently locks this tenant override.");
    }
}
