namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Footer;
using Explore.Domain.Constants;

/// <summary>
/// Strongly-typed group for footer configuration settings.
/// </summary>
public class FooterSettingGroup : ISettingGroup
{
    public bool Enabled { get; private set; } = true;
    public string Template { get; private set; } = "standard-3-col";
    public bool ShowDescription { get; private set; } = true;
    public string DescriptionText { get; private set; } = string.Empty;
    public bool ShowSocialLinks { get; private set; } = true;
    public List<FooterSocialLinkDto> SocialLinks { get; private set; } = [];
    public string CopyrightText { get; private set; } = string.Empty;
    public bool ShowCookieSettingsLink { get; private set; } = true;
    public bool LockTenantTemplate { get; private set; }
    public bool LockTenantLinkGroups { get; private set; }
    public bool LockTenantSocialLinks { get; private set; }
    public bool LockTenantDescription { get; private set; }
    public bool LockTenantCopyright { get; private set; }

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Footer.Enabled,
        GovernanceSettingKeys.Footer.Template,
        GovernanceSettingKeys.Footer.ShowDescription,
        GovernanceSettingKeys.Footer.DescriptionText,
        GovernanceSettingKeys.Footer.ShowSocialLinks,
        GovernanceSettingKeys.Footer.SocialLinks,
        GovernanceSettingKeys.Footer.CopyrightText,
        GovernanceSettingKeys.Footer.ShowCookieSettingsLink,
        GovernanceSettingKeys.Footer.LockTenantTemplate,
        GovernanceSettingKeys.Footer.LockTenantLinkGroups,
        GovernanceSettingKeys.Footer.LockTenantSocialLinks,
        GovernanceSettingKeys.Footer.LockTenantDescription,
        GovernanceSettingKeys.Footer.LockTenantCopyright,
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Footer.Enabled, out var enabled))
            Enabled = SettingValueSerializer.DeserializeBool(enabled.Value, true);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.Template, out var template))
            Template = SettingValueSerializer.DeserializeString(template.Value, "standard-3-col");

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.ShowDescription, out var showDesc))
            ShowDescription = SettingValueSerializer.DeserializeBool(showDesc.Value, true);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.DescriptionText, out var descText))
            DescriptionText = SettingValueSerializer.DeserializeString(descText.Value);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.ShowSocialLinks, out var showSocial))
            ShowSocialLinks = SettingValueSerializer.DeserializeBool(showSocial.Value, true);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.SocialLinks, out var socialLinks))
            SocialLinks = SettingValueSerializer.Deserialize(socialLinks.Value, new List<FooterSocialLinkDto>());

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.CopyrightText, out var copyright))
            CopyrightText = SettingValueSerializer.DeserializeString(copyright.Value);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.ShowCookieSettingsLink, out var showCookie))
            ShowCookieSettingsLink = SettingValueSerializer.DeserializeBool(showCookie.Value, true);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.LockTenantTemplate, out var lockTemplate))
            LockTenantTemplate = SettingValueSerializer.DeserializeBool(lockTemplate.Value);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.LockTenantLinkGroups, out var lockGroups))
            LockTenantLinkGroups = SettingValueSerializer.DeserializeBool(lockGroups.Value);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.LockTenantSocialLinks, out var lockSocial))
            LockTenantSocialLinks = SettingValueSerializer.DeserializeBool(lockSocial.Value);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.LockTenantDescription, out var lockDesc))
            LockTenantDescription = SettingValueSerializer.DeserializeBool(lockDesc.Value);

        if (settings.TryGetValue(GovernanceSettingKeys.Footer.LockTenantCopyright, out var lockCopy))
            LockTenantCopyright = SettingValueSerializer.DeserializeBool(lockCopy.Value);
    }
}
