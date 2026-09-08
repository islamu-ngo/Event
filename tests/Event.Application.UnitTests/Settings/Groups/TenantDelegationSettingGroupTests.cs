namespace Event.Application.UnitTests.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;

public sealed class TenantDelegationSettingGroupTests
{
    [Test]
    public async Task Populate_WithNoReportingSetting_DefaultsReportingProvidersLocked()
    {
        var group = new TenantDelegationSettingGroup();

        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.LockReportingProviders).IsTrue();
        await Assert.That(group.LockTenantOspreyProvider).IsTrue();
        await Assert.That(group.LockTenantCoopProvider).IsTrue();
    }

    [Test]
    public async Task Populate_WithReportingLockFalse_AllowsTenantReportingProviders()
    {
        var group = new TenantDelegationSettingGroup();

        group.Populate(CreateSettings(
            (GovernanceSettingKeys.TenantDelegation.LockReportingProviders, "false"),
            (GovernanceSettingKeys.TenantDelegation.LockTenantOspreyProvider, "false"),
            (GovernanceSettingKeys.TenantDelegation.LockTenantCoopProvider, "false")));

        await Assert.That(group.LockReportingProviders).IsFalse();
        await Assert.That(group.LockTenantOspreyProvider).IsFalse();
        await Assert.That(group.LockTenantCoopProvider).IsFalse();
    }

    private static Dictionary<string, ResolvedSetting> CreateSettings(params (string key, string value)[] entries) =>
        entries.ToDictionary(e => e.key, e => new ResolvedSetting { Value = e.value });
}
