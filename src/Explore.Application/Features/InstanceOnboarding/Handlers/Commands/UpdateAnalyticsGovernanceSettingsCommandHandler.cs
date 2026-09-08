using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Analytics;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Enums.Analytics;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class UpdateAnalyticsGovernanceSettingsCommandHandler(
    IHierarchicalSettingsResolver settingsResolver,
    IAdminContext adminContext)
    : IRequestHandler<UpdateAnalyticsGovernanceSettingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateAnalyticsGovernanceSettingsCommand request, CancellationToken cancellationToken)
    {
        var userId = request.UserId;

        if (!await adminContext.IsInstanceAdminAsync(userId, cancellationToken))
        {
            return BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can update instance governance settings.");
        }

        if (!request.Patch.HasChanges())
        {
            return BaseCommandResponse.Failure<Guid>(
                "ValidationFailed",
                errors: ["Analytics governance patch must include at least one setting."]);
        }

        // Resolve current provider context for capability-aware validation
        var group = await settingsResolver.ResolveGroupAsync<AnalyticsSettingGroup>(
            new SettingContext(), cancellationToken);
        var s = new DTOs.Analytics.AnalyticsGovernanceSettingsDto
        {
            CookieConsentEnabled = request.Patch.CookieConsentEnabled.HasValue ? request.Patch.CookieConsentEnabled.Value : group.CookieConsentEnabled,
            DeclineBehavior = request.Patch.DeclineBehavior.HasValue ? request.Patch.DeclineBehavior.Value : group.DeclineBehavior,
            ConsentCookieLifetimeDays = request.Patch.ConsentCookieLifetimeDays.HasValue ? request.Patch.ConsentCookieLifetimeDays.Value : group.ConsentCookieLifetimeDays,
            GlobalDisableClientTracking = request.Patch.GlobalDisableClientTracking.HasValue ? request.Patch.GlobalDisableClientTracking.Value : group.GlobalDisableClientTracking,
            PosthogCookielessMode = request.Patch.PosthogCookielessMode.HasValue ? request.Patch.PosthogCookielessMode.Value : group.PosthogCookielessMode,
            PosthogPersonProfiles = request.Patch.PosthogPersonProfiles.HasValue ? request.Patch.PosthogPersonProfiles.Value : group.PosthogPersonProfiles,
            PosthogSessionReplay = request.Patch.PosthogSessionReplay.HasValue ? request.Patch.PosthogSessionReplay.Value : group.PosthogSessionReplay,
            PosthogAutocapture = request.Patch.PosthogAutocapture.HasValue ? request.Patch.PosthogAutocapture.Value : group.PosthogAutocapture,
            PosthogHeatmaps = request.Patch.PosthogHeatmaps.HasValue ? request.Patch.PosthogHeatmaps.Value : group.PosthogHeatmaps,
            PosthogToolbar = request.Patch.PosthogToolbar.HasValue ? request.Patch.PosthogToolbar.Value : group.PosthogToolbar
        };

        var provider = Enum.TryParse<AnalyticsProviderEnum>(group.Provider, true, out var parsed)
            ? parsed : AnalyticsProviderEnum.None;
        var capabilities = AnalyticsProviderCapabilities.For(provider);

        // Validate — reject illegal combinations
        var errors = ValidateSettings(s, group, capabilities);
        if (errors.Count > 0)
        {
            return BaseCommandResponse.Failure<Guid>("ValidationFailed", errors: errors);
        }

        // Collect advisory warnings for suboptimal-but-allowed combos
        var warnings = CollectWarnings(s, group, provider, capabilities);

        // Persist all settings
        await PersistSettingsAsync(s, request.Patch, userId, cancellationToken);

        return BaseCommandResponse.Success(
            Guid.Empty,
            warnings.Count > 0 ? string.Join(" ", warnings) : null);
    }

    private static List<string> ValidateSettings(
        DTOs.Analytics.AnalyticsGovernanceSettingsDto s,
        AnalyticsSettingGroup group,
        AnalyticsProviderCapabilities capabilities)
    {
        var errors = new List<string>();

        if (s.ConsentCookieLifetimeDays is < 1 or > 730)
            errors.Add("ConsentCookieLifetimeDays must be between 1 and 730.");

        // DeclineBehavior.Cookieless requires provider cookieless support
        // (inherently cookieless providers don't need explicit cookieless mode)
        if (s.DeclineBehavior == DeclineBehavior.Cookieless
            && group.Enabled
            && !capabilities.SupportsCookielessMode
            && !capabilities.InherentlyCookieless)
        {
            errors.Add($"Provider '{group.Provider}' does not support cookieless decline behavior.");
        }

        return errors;
    }

    private static List<string> CollectWarnings(
        DTOs.Analytics.AnalyticsGovernanceSettingsDto s,
        AnalyticsSettingGroup group,
        AnalyticsProviderEnum provider,
        AnalyticsProviderCapabilities capabilities)
    {
        var warnings = new List<string>();

        // PostHog-specific features on non-PostHog provider
        if (provider != AnalyticsProviderEnum.Posthog && group.Enabled)
        {
            var hasPosthogFeatures = s.PosthogSessionReplay || s.PosthogAutocapture
                || s.PosthogHeatmaps || s.PosthogToolbar
                || s.PosthogCookielessMode != PosthogCookielessMode.Off
                || s.PosthogPersonProfiles != PosthogPersonProfiles.IdentifiedOnly;

            if (hasPosthogFeatures)
                warnings.Add("PostHog-specific features are configured but active provider is not PostHog; these will be ignored at runtime.");
        }

        // Session replay degraded in always-cookieless mode
        if (s.PosthogSessionReplay && s.PosthogCookielessMode == PosthogCookielessMode.Always)
            warnings.Add("Session replay is degraded in always-cookieless mode.");

        // Cookie consent banner unnecessary for inherently cookieless provider
        if (s.CookieConsentEnabled && capabilities.InherentlyCookieless && group.Enabled)
            warnings.Add($"Cookie consent banner is unnecessary for inherently cookieless provider '{group.Provider}'.");

        return warnings;
    }

    private async Task PersistSettingsAsync(
        DTOs.Analytics.AnalyticsGovernanceSettingsDto s,
        DTOs.Instance.PatchAnalyticsGovernanceSettingsDto patch,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (patch.CookieConsentEnabled.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.CookieConsentEnabled,
                s.CookieConsentEnabled.ToString().ToLowerInvariant(),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.DeclineBehavior.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.DeclineBehavior,
                s.DeclineBehavior.ToString().ToLowerInvariant(),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.ConsentCookieLifetimeDays.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.ConsentCookieLifetimeDays,
                s.ConsentCookieLifetimeDays.ToString(),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.GlobalDisableClientTracking.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.GlobalDisableClientTracking,
                s.GlobalDisableClientTracking.ToString().ToLowerInvariant(),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.PosthogCookielessMode.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.PosthogCookielessMode,
                ToSnakeCase(s.PosthogCookielessMode.ToString()),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.PosthogPersonProfiles.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.PosthogPersonProfiles,
                ToSnakeCase(s.PosthogPersonProfiles.ToString()),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.PosthogSessionReplay.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.PosthogSessionReplay,
                s.PosthogSessionReplay.ToString().ToLowerInvariant(),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.PosthogAutocapture.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.PosthogAutocapture,
                s.PosthogAutocapture.ToString().ToLowerInvariant(),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.PosthogHeatmaps.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.PosthogHeatmaps,
                s.PosthogHeatmaps.ToString().ToLowerInvariant(),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }

        if (patch.PosthogToolbar.HasValue)
        {
            await settingsResolver.SetValueAsync(
                GovernanceSettingKeys.Analytics.PosthogToolbar,
                s.PosthogToolbar.ToString().ToLowerInvariant(),
                SettingScope.Instance, Guid.Empty, userId, cancellationToken);
        }
    }

    private static string ToSnakeCase(string value)
    {
        // Converts PascalCase enum names (e.g., "OnReject") to snake_case ("on_reject")
        return string.Concat(value.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
