using System.Text.Json;
using Explore.Application.DTOs.Onboarding;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.Extensions.Configuration;
using Explore.Application.Configuration;

namespace Explore.Application.Features.InstanceOnboarding.Common;

internal static class InstanceOnboardingProfileSettingHelpers
{
    internal static SelfHostOnboardingProfileDto Normalize(
        SelfHostOnboardingProfileDto profile,
        string? fallbackSiteName = null) => new()
        {
            SiteName = string.IsNullOrWhiteSpace(profile.SiteName)
            ? fallbackSiteName?.Trim() ?? string.Empty
            : profile.SiteName.Trim(),
            SupportEmail = string.IsNullOrWhiteSpace(profile.SupportEmail) ? null : profile.SupportEmail.Trim(),
            PublicUrl = string.IsNullOrWhiteSpace(profile.PublicUrl) ? null : profile.PublicUrl.Trim(),
            Locale = string.IsNullOrWhiteSpace(profile.Locale) ? "en" : profile.Locale.Trim(),
            TimeZone = string.IsNullOrWhiteSpace(profile.TimeZone) ? "UTC" : profile.TimeZone.Trim(),
            Purpose = string.IsNullOrWhiteSpace(profile.Purpose) ? null : profile.Purpose.Trim()
        };

    internal static void AddSettings(
        List<SystemSetting> settings,
        SelfHostOnboardingProfileDto profile,
        IConfiguration configuration)
    {
        var configuredUrl = PublicAddressResolver.ReadOverride(configuration);
        var publicUrl = PublicAddressResolver.IsValid(configuredUrl) ? configuredUrl : profile.PublicUrl;
        Add(
            settings,
            GovernanceSettingKeys.Branding.DisplayName,
            JsonSerializer.Serialize(profile.SiteName),
            "Branding",
            1,
            "Instance brand display name");

        Add(
            settings,
            GovernanceSettingKeys.Branding.SupportEmail,
            JsonSerializer.Serialize(profile.SupportEmail),
            "Branding",
            2,
            "Public support contact for the instance site");

        if (PublicAddressResolver.IsValid(publicUrl))
        {
            Add(
                settings,
                GovernanceSettingKeys.Domains.PublicBaseUrl,
                JsonSerializer.Serialize(publicUrl),
                "Domains", 2, "Public address established during authorized setup");
        }

        Add(
            settings,
            GovernanceSettingKeys.Localization.DefaultLanguage,
            JsonSerializer.Serialize(profile.Locale.ToLowerInvariant()),
            "Localization",
            1,
            "Default language code (ISO 639-1) for the instance");
    }

    private static void Add(
        List<SystemSetting> settings,
        string key,
        string value,
        string category,
        int displayOrder,
        string description) => settings.Add(new SystemSetting
        {
            SettingKey = key,
            Value = value,
            ValueType = SettingValueType.String,
            IsLocked = false,
            Category = category,
            DisplayOrder = displayOrder,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
}
