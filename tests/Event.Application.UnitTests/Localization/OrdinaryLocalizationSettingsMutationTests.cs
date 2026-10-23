using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Localization;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Event.Application.UnitTests.Localization;

public sealed class OrdinaryLocalizationSettingsMutationTests
{
    [Test]
    public async Task CaptureLocalization_AllSectionsProduceExactlyNineNormalizedInstanceValues()
    {
        var effects = OrdinarySettingsMutation.CaptureLocalization(FullCommand());
        (string Key, string Value)[] expected =
        [
            (GovernanceSettingKeys.Localization.TmsProvider, "\"weblate\""),
            (GovernanceSettingKeys.Localization.TmsApiUrl, "\"https://translations.example.test/api\""),
            (GovernanceSettingKeys.Localization.TmsProjectId, "\"event-project\""),
            (GovernanceSettingKeys.Localization.TmsComponent, "\"web-client\""),
            (GovernanceSettingKeys.Localization.DefaultLanguage, "\"en\""),
            (GovernanceSettingKeys.Localization.EnabledLanguages, "\"en,fr,ar\""),
            (GovernanceSettingKeys.Localization.FallbackLanguage, "\"ar\""),
            (GovernanceSettingKeys.Localization.ClientPickerEnabled, "true"),
            (GovernanceSettingKeys.Localization.ForceOfflineMode, "false")
        ];

        await Assert.That(effects.Length).IsEqualTo(9);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(effects[index]).IsEqualTo(new HumanSettingsMutation(
                HumanSettingsOperation.InstanceSettings, SettingScope.Instance, null, null,
                expected[index].Key, HumanSettingsMutationKind.SetValue, expected[index].Value));
        }
    }

    [Test]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    public async Task CaptureLocalization_OmittedSectionsProduceNoEffects(
        bool includeTms, bool includeLanguages, bool includeRuntime)
    {
        var full = FullCommand().Dto;
        var command = new UpdateLocalizationGovernanceCommand
        {
            Dto = new()
            {
                Tms = includeTms ? full.Tms : null,
                Languages = includeLanguages ? full.Languages : null,
                Runtime = includeRuntime ? full.Runtime : null
            }
        };
        string[] expectedKeys = includeTms
            ? [GovernanceSettingKeys.Localization.TmsProvider, GovernanceSettingKeys.Localization.TmsApiUrl,
                GovernanceSettingKeys.Localization.TmsProjectId, GovernanceSettingKeys.Localization.TmsComponent]
            : includeLanguages
                ? [GovernanceSettingKeys.Localization.DefaultLanguage, GovernanceSettingKeys.Localization.EnabledLanguages,
                    GovernanceSettingKeys.Localization.FallbackLanguage]
                : [GovernanceSettingKeys.Localization.ClientPickerEnabled, GovernanceSettingKeys.Localization.ForceOfflineMode];

        var effects = OrdinarySettingsMutation.CaptureLocalization(command);

        await Assert.That(effects.Select(effect => effect.Key).SequenceEqual(expectedKeys)).IsTrue();
        await Assert.That(OrdinarySettingsMutation.Capture(command, effects)!.Original).IsSameReferenceAs(command);
    }

    [Test]
    public async Task CaptureLocalization_DisabledTmsClearsOmittedProviderConfiguration()
    {
        var command = new UpdateLocalizationGovernanceCommand
        {
            Dto = new() { Tms = new() { Provider = "none" } }
        };

        var effects = OrdinarySettingsMutation.CaptureLocalization(command);

        await Assert.That(effects.Length).IsEqualTo(4);
        await Assert.That(effects[0].Value).IsEqualTo("\"none\"");
        await Assert.That(effects[1].Value).IsEqualTo("\"\"");
        await Assert.That(effects[2].Value).IsEqualTo("\"\"");
        await Assert.That(effects[3].Value).IsEqualTo("\"\"");
    }

    [Test]
    public async Task Capture_ExactEffectsPreserveOriginalCommandIdentity()
    {
        var command = FullCommand();
        var effects = OrdinarySettingsMutation.CaptureLocalization(command);

        var captured = OrdinarySettingsMutation.Capture(command, effects);

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Original).IsSameReferenceAs(command);
        await Assert.That(captured!.Effects.SequenceEqual(effects)).IsTrue();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("extra")]
    [Arguments("value")]
    [Arguments("scope")]
    [Arguments("tenant")]
    [Arguments("actor")]
    [Arguments("operation")]
    [Arguments("key")]
    [Arguments("lock-kind")]
    [Arguments("lock-value")]
    public async Task Capture_RejectsEffectsThatDifferFromOriginalCommand(string change)
    {
        var command = FullCommand();
        var effects = OrdinarySettingsMutation.CaptureLocalization(command);
        var first = effects[0];
        var altered = change switch
        {
            "missing" => effects.RemoveAt(0),
            "extra" => effects.Add(first),
            "value" => effects.SetItem(0, first with { Value = "\"tolgee\"" }),
            "scope" => effects.SetItem(0, first with { Scope = SettingScope.Tenant }),
            "tenant" => effects.SetItem(0, first with { TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002") }),
            "actor" => effects.SetItem(0, first with { UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000003") }),
            "operation" => effects.SetItem(0, first with { Operation = HumanSettingsOperation.TenantSettings }),
            "key" => effects.SetItem(0, first with { Key = GovernanceSettingKeys.Localization.ForceOfflineMode }),
            "lock-kind" => effects.SetItem(0, first with { Kind = HumanSettingsMutationKind.SetLock }),
            "lock-value" => effects.SetItem(0, first with { IsLocked = false }),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        await Assert.That(() => OrdinarySettingsMutation.Capture(command, altered))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    [Arguments("empty")]
    [Arguments("provider")]
    [Arguments("url")]
    [Arguments("culture")]
    [Arguments("fallback")]
    public async Task Capture_InvalidOriginalDtoIsRejectedEvenWithOtherwiseValidEffects(string invalidInput)
    {
        var full = FullCommand();
        var dto = full.Dto;
        var invalidDto = invalidInput switch
        {
            "empty" => new UpdateLocalizationGovernanceDto(),
            "provider" => dto with { Tms = dto.Tms! with { Provider = "unsupported" } },
            "url" => dto with { Tms = dto.Tms! with { ApiUrl = "relative/path" } },
            "culture" => dto with { Languages = dto.Languages! with { EnabledLanguages = [" EN ", "FR", " AR ", "zz"] } },
            "fallback" => dto with { Languages = dto.Languages! with { EnabledLanguages = [" EN ", "FR"] } },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidInput))
        };
        var command = full with { Dto = invalidDto };
        var effects = OrdinarySettingsMutation.CaptureLocalization(full);

        await Assert.That(() => OrdinarySettingsMutation.CaptureLocalization(command))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(() => OrdinarySettingsMutation.Capture(command, effects))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(() => OrdinarySettingsMutation.Capture(command, ImmutableArray<HumanSettingsMutation>.Empty))
            .Throws<UnauthorizedAccessException>();
    }

    private static UpdateLocalizationGovernanceCommand FullCommand() => new()
    {
        Dto = new()
        {
            Tms = new()
            {
                Provider = " WeBlAtE ",
                ApiUrl = "https://translations.example.test/api",
                ProjectId = "event-project",
                Component = "web-client"
            },
            Languages = new()
            {
                DefaultLanguage = " EN ",
                EnabledLanguages = [" EN ", "FR", " AR "],
                FallbackLanguage = " AR "
            },
            Runtime = new() { ClientPickerEnabled = true, ForceOfflineMode = false }
        }
    };
}
