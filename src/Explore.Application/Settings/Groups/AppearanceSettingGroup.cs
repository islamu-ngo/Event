namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Common.Localization;
using Explore.Domain.Constants;

public class AppearanceSettingGroup : ISettingGroup
{
    public Guid? ActiveProfileId { get; private set; }
    public string ThemeMode { get; private set; } = "system";
    public string Direction { get; private set; } = "auto";
    public string Language { get; private set; } = "en";
    public Guid? DefaultPresetId { get; private set; }
    public string DefaultThemeMode { get; private set; } = "system";

#pragma warning disable CS0618
    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Appearance.ActiveProfileId,
        GovernanceSettingKeys.Appearance.ThemeMode,
        GovernanceSettingKeys.Appearance.Direction,
        GovernanceSettingKeys.Appearance.Language,
        GovernanceSettingKeys.Appearance.DefaultPresetId,
        GovernanceSettingKeys.Appearance.DefaultThemeMode,
        GovernanceSettingKeys.Appearance.LegacyDefaultThemeId
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Appearance.ActiveProfileId, out var profileIdSetting))
        {
            var rawProfileId = SettingValueSerializer.DeserializeString(profileIdSetting.Value);
            ActiveProfileId = Guid.TryParse(rawProfileId, out var parsed) ? parsed : null;
        }

        // Fallback: if ActiveProfileId is not set, check legacy DefaultThemeId.
        if (ActiveProfileId is null
            && settings.TryGetValue(GovernanceSettingKeys.Appearance.LegacyDefaultThemeId, out var legacySetting))
        {
            var rawLegacy = SettingValueSerializer.DeserializeString(legacySetting.Value);
            ActiveProfileId = Guid.TryParse(rawLegacy, out var parsedLegacy) ? parsedLegacy : null;
        }
#pragma warning restore CS0618

        if (settings.TryGetValue(GovernanceSettingKeys.Appearance.ThemeMode, out var themeModeSetting))
        {
            var mode = SettingValueSerializer.DeserializeString(themeModeSetting.Value, "system");
            ThemeMode = mode is "light" or "dark" or "system" ? mode : "system";
        }

        if (settings.TryGetValue(GovernanceSettingKeys.Appearance.Direction, out var directionSetting))
        {
            var dir = SettingValueSerializer.DeserializeString(directionSetting.Value, "auto");
            Direction = dir is "ltr" or "rtl" ? dir : "auto";
        }

        if (settings.TryGetValue(GovernanceSettingKeys.Appearance.Language, out var languageSetting))
        {
            var raw = SettingValueSerializer.DeserializeString(languageSetting.Value, "en");
            Language = CultureRegistry.TryGetEntry(raw, out var entry) ? entry.Code : "en";
        }

        if (settings.TryGetValue(GovernanceSettingKeys.Appearance.DefaultPresetId, out var presetIdSetting))
        {
            var rawPresetId = SettingValueSerializer.DeserializeString(presetIdSetting.Value);
            DefaultPresetId = Guid.TryParse(rawPresetId, out var parsedPreset) ? parsedPreset : null;
        }

        if (settings.TryGetValue(GovernanceSettingKeys.Appearance.DefaultThemeMode, out var defaultModeSetting))
        {
            var defaultMode = SettingValueSerializer.DeserializeString(defaultModeSetting.Value, "system");
            DefaultThemeMode = defaultMode is "light" or "dark" or "system" ? defaultMode : "system";
        }
    }
}
