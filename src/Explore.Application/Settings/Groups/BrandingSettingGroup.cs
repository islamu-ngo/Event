namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

/// <summary>
/// Strongly-typed group for white-label branding settings.
/// </summary>
public class BrandingSettingGroup : ISettingGroup
{
    public string DisplayName { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public string? FaviconUrl { get; private set; }
    public string? CustomCssUrl { get; private set; }

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Branding.DisplayName,
        GovernanceSettingKeys.Branding.LogoUrl,
        GovernanceSettingKeys.Branding.FaviconUrl,
        GovernanceSettingKeys.Branding.CustomCssUrl
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Branding.DisplayName, out var name))
            DisplayName = SettingValueSerializer.Deserialize(name.Value, string.Empty);
        if (settings.TryGetValue(GovernanceSettingKeys.Branding.LogoUrl, out var logo))
            LogoUrl = SettingValueSerializer.DeserializeString(logo.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Branding.FaviconUrl, out var favicon))
            FaviconUrl = SettingValueSerializer.DeserializeString(favicon.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Branding.CustomCssUrl, out var css))
            CustomCssUrl = SettingValueSerializer.DeserializeString(css.Value);
    }
}
