namespace Event.Application.UnitTests.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;

public sealed class ReportingIntakeSettingGroupTests
{
    [Test]
    public async Task SettingKeys_ContainsOnlyIntakeEnabled()
    {
        await Assert.That(ReportingIntakeSettingGroup.SettingKeys)
            .IsEquivalentTo([GovernanceSettingKeys.EventReporting.IntakeEnabled]);
    }

    [Test]
    public async Task Populate_WithNoSettings_DefaultsIntakeEnabled()
    {
        var group = new ReportingIntakeSettingGroup();

        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.IntakeEnabled).IsTrue();
    }

    [Test]
    public async Task Populate_WithResolvedJsonFalse_DisablesIntake()
    {
        var group = new ReportingIntakeSettingGroup();

        group.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.EventReporting.IntakeEnabled] = new() { Value = "false" }
        });

        await Assert.That(group.IntakeEnabled).IsFalse();
    }

    [Test]
    public async Task Populate_WithResolvedJsonTrue_EnablesIntake()
    {
        var group = new ReportingIntakeSettingGroup();

        group.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.EventReporting.IntakeEnabled] = new() { Value = "true" }
        });

        await Assert.That(group.IntakeEnabled).IsTrue();
    }

    [Test]
    public async Task ReportingSettingGroup_DoesNotContainIntakeEnabled()
    {
        await Assert.That(ReportingSettingGroup.SettingKeys)
            .DoesNotContain(GovernanceSettingKeys.EventReporting.IntakeEnabled);
    }
}
