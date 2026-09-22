using System.Text.Json;
using Explore.Application.Contracts.Persistence;
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
            CanonicalUrl = string.IsNullOrWhiteSpace(profile.CanonicalUrl) ? null : profile.CanonicalUrl.Trim(),
            Locale = string.IsNullOrWhiteSpace(profile.Locale) ? "en" : profile.Locale.Trim(),
            TimeZone = string.IsNullOrWhiteSpace(profile.TimeZone) ? "UTC" : profile.TimeZone.Trim(),
            Purpose = string.IsNullOrWhiteSpace(profile.Purpose) ? null : profile.Purpose.Trim()
        };

    internal static async Task PersistAsync(
        ISystemSettingRepository systemSettingRepository,
        SelfHostOnboardingProfileDto profile,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var configuredUrl = PublicAddressResolver.ReadOverride(configuration);
        var canonicalUrl = PublicAddressResolver.IsValid(configuredUrl) ? configuredUrl : profile.CanonicalUrl;
        await UpsertAsync(
            systemSettingRepository,
            GovernanceSettingKeys.Branding.DisplayName,
            JsonSerializer.Serialize(profile.SiteName),
            "Branding",
            1,
            "Instance brand display name",
            cancellationToken);

        await UpsertAsync(
            systemSettingRepository,
            GovernanceSettingKeys.Branding.SupportEmail,
            JsonSerializer.Serialize(profile.SupportEmail),
            "Branding",
            2,
            "Public support contact for the instance site",
            cancellationToken);

        if (PublicAddressResolver.IsValid(canonicalUrl))
        {
            await UpsertAsync(
                systemSettingRepository,
                GovernanceSettingKeys.Domains.PublicBaseUrl,
                JsonSerializer.Serialize(canonicalUrl),
                "Domains", 2, "Public address established during authorized setup", cancellationToken);
        }

        await UpsertAsync(
            systemSettingRepository,
            GovernanceSettingKeys.Localization.DefaultLanguage,
            JsonSerializer.Serialize(profile.Locale.ToLowerInvariant()),
            "Localization",
            1,
            "Default language code (ISO 639-1) for the instance",
            cancellationToken);
    }

    private static Task UpsertAsync(
        ISystemSettingRepository systemSettingRepository,
        string key,
        string value,
        string category,
        int displayOrder,
        string description,
        CancellationToken cancellationToken) => systemSettingRepository.UpsertAsync(new SystemSetting
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
        }, cancellationToken);
}
