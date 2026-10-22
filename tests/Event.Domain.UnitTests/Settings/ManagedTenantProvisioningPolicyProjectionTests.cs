using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

namespace Event.Domain.UnitTests.Settings;

public sealed class ManagedTenantProvisioningPolicyProjectionTests
{
    [Test]
    public async Task InventoryIncludesEveryAcceptedPlanLockButNoSensitiveOrUnrelatedInstanceKey()
    {
        foreach (var definition in SettingRegistry.All.Where(definition => !definition.IsSensitive
                     && definition.MinScope <= SettingScope.Tenant && definition.MaxScope >= SettingScope.Tenant))
            await Assert.That(ManagedTenantProvisioningPolicyInventory.Keys.Contains(definition.Key)).IsTrue();
        foreach (var definition in SettingRegistry.All.Where(definition => definition.IsSensitive))
            await Assert.That(ManagedTenantProvisioningPolicyInventory.Keys.Contains(definition.Key)).IsFalse();
        await Assert.That(ManagedTenantProvisioningPolicyInventory.Keys.Contains(BrandingSettingDefinitions.DisplayName.Key)).IsTrue();
        await Assert.That(ManagedTenantProvisioningPolicyInventory.Keys.Contains(BrandingSettingDefinitions.LogoUrl.Key)).IsTrue();
        await Assert.That(ManagedTenantProvisioningPolicyInventory.Keys.Contains(BrandingSettingDefinitions.FaviconUrl.Key)).IsTrue();
        await Assert.That(ManagedTenantProvisioningPolicyInventory.Keys.Contains(BrandingSettingDefinitions.CustomCssUrl.Key)).IsTrue();
        await Assert.That(ManagedTenantProvisioningPolicyInventory.Keys.Contains(BrandingSettingDefinitions.SupportEmail.Key)).IsFalse();
        await Assert.That(ManagedTenantProvisioningPolicyInventory.ValueKeys.Length).IsEqualTo(4);
    }

    [Test]
    [Arguments(GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"")]
    [Arguments(GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, "true")]
    [Arguments(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, "1048576")]
    [Arguments(GovernanceSettingKeys.Domains.AllowTenantCustomDomain, "false")]
    public async Task OnlyTheFourProvisioningValueKeysRetainTheirSourceValue(string key, string value)
    {
        var row = ManagedTenantProvisioningPolicySetting.CreateAbsent(key);
        row.Project(Source(key, value, true));

        await Assert.That(row.Id.Version).IsEqualTo(7);
        await Assert.That(row.SettingKey).IsEqualTo(key);
        await Assert.That(row.IsPresent).IsTrue();
        await Assert.That(row.IsLocked).IsTrue();
        await Assert.That(row.Value).IsEqualTo(value);
        await Assert.That(row.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task LockOnlyProjectionNeverCarriesTheNonApprovedSourceValue()
    {
        string key = BrandingSettingDefinitions.LogoUrl.Key;
        var row = ManagedTenantProvisioningPolicySetting.CreateAbsent(key);
        row.Project(Source(key, "\"https://branding.example.test/asset.svg\"", true));

        await Assert.That(row.IsPresent).IsTrue();
        await Assert.That(row.IsLocked).IsTrue();
        await Assert.That(row.Value).IsNull();
    }

    [Test]
    public async Task DeletionKeepsAnExplicitTombstoneAndRecreationCannotHideAnAbaTransition()
    {
        string key = GovernanceSettingKeys.Tenants.WhiteLabelingEnabled;
        var row = ManagedTenantProvisioningPolicySetting.CreateAbsent(key);
        Guid projectionId = row.Id;
        var source = Source(key, "true", true);
        row.Project(source);
        long observedRevision = row.Revision;
        row.Project(null);

        await Assert.That(row.Id).IsEqualTo(projectionId);
        await Assert.That(row.IsPresent).IsFalse();
        await Assert.That(row.IsLocked).IsFalse();
        await Assert.That(row.Value).IsNull();
        await Assert.That(row.Revision).IsEqualTo(observedRevision + 1);
        row.Project(source);
        await Assert.That(row.Revision).IsEqualTo(observedRevision + 2);
        await Assert.That(row.Value).IsEqualTo("true");
    }

    [Test]
    public async Task ASourceForAnotherKeyCannotRetargetOrPartiallyMutateAProjection()
    {
        var row = ManagedTenantProvisioningPolicySetting.CreateAbsent(GovernanceSettingKeys.Deployment.Mode);
        await Assert.That(() => row.Project(Source(GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, "true", false)))
            .Throws<ArgumentException>();
        await Assert.That(row.SettingKey).IsEqualTo(GovernanceSettingKeys.Deployment.Mode);
        await Assert.That(row.IsPresent).IsFalse();
        await Assert.That(row.Revision).IsEqualTo(0L);
    }

    [Test]
    public async Task MissingInventoryCoverageCannotBeMistakenForAnUnlockedAbsentSource()
    {
        var rows = ManagedTenantProvisioningPolicyInventory.Keys
            .Select(ManagedTenantProvisioningPolicySetting.CreateAbsent).ToArray();
        ManagedTenantProvisioningPolicyInventory.RequireComplete(rows);
        await Assert.That(() => ManagedTenantProvisioningPolicyInventory.RequireComplete(rows.Skip(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ManagedTenantProvisioningPolicyInventory.RequireComplete(rows.Append(rows[0])))
            .Throws<InvalidOperationException>();
    }

    private static SystemSetting Source(string key, string value, bool locked) => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = key,
        Value = value,
        IsLocked = locked
    };
}
