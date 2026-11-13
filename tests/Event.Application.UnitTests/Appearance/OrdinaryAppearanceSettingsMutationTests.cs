using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Event.Application.UnitTests.Appearance;

public sealed class OrdinaryAppearanceSettingsMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CapturePreservesTheOriginalAppearanceIntent(bool removeCandidate)
    {
        var command = new UpdateCurrentUserAppearancePreferencesCommand
        {
            Preferences = new() { Localization = new() { Language = "ar" } }
        };
        ImmutableArray<HumanSettingsMutation> effects =
        [
            new(HumanSettingsOperation.UserSettings, SettingScope.User, Guid.CreateVersion7(), Guid.CreateVersion7(),
                GovernanceSettingKeys.Appearance.Language,
                removeCandidate ? HumanSettingsMutationKind.Remove : HumanSettingsMutationKind.SetValue,
                removeCandidate ? null : "\"ar\"")
        ];

        var captured = OrdinarySettingsMutation.Capture(command, effects);

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Original).IsSameReferenceAs(command);
        await Assert.That(captured.Effects.SequenceEqual(effects)).IsTrue();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("extra")]
    [Arguments("duplicate")]
    [Arguments("key")]
    [Arguments("value")]
    [Arguments("scope")]
    [Arguments("operation")]
    [Arguments("tenant")]
    [Arguments("user")]
    [Arguments("lock")]
    [Arguments("remove-value")]
    public async Task CaptureRejectsChangesOutsideTheAppearanceIntent(string change)
    {
        var command = new UpdateCurrentUserAppearancePreferencesCommand
        {
            Preferences = new() { Localization = new() { Language = "ar", Direction = "rtl" } }
        };
        Guid tenant = Guid.CreateVersion7();
        Guid user = Guid.CreateVersion7();
        ImmutableArray<HumanSettingsMutation> effects =
        [
            new(HumanSettingsOperation.UserSettings, SettingScope.User, tenant, user,
                GovernanceSettingKeys.Appearance.Language, HumanSettingsMutationKind.SetValue, "\"ar\""),
            new(HumanSettingsOperation.UserSettings, SettingScope.User, tenant, user,
                GovernanceSettingKeys.Appearance.Direction, HumanSettingsMutationKind.SetValue, "\"rtl\"")
        ];
        var first = effects[0];
        var altered = change switch
        {
            "missing" => effects.RemoveAt(0),
            "extra" => effects.Add(first),
            "duplicate" => effects.SetItem(1, first),
            "key" => effects.SetItem(0, first with { Key = GovernanceSettingKeys.Appearance.ThemeMode }),
            "value" => effects.SetItem(0, first with { Value = "\"fr\"" }),
            "scope" => effects.SetItem(0, first with { Scope = SettingScope.Tenant }),
            "operation" => effects.SetItem(0, first with { Operation = HumanSettingsOperation.TenantSettings }),
            "tenant" => effects.SetItem(0, first with { TenantId = null }),
            "user" => effects.SetItem(0, first with { UserId = null }),
            "lock" => effects.SetItem(0, first with { Kind = HumanSettingsMutationKind.SetLock, Value = null, IsLocked = true }),
            "remove-value" => effects.SetItem(0, first with { Kind = HumanSettingsMutationKind.Remove }),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        await Assert.That(() => OrdinarySettingsMutation.Capture(command, altered))
            .Throws<UnauthorizedAccessException>();
    }
}
