using Explore.Application.Analytics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Analytics;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class GetAnalyticsGovernanceSettingsQueryHandler(
    IHierarchicalSettingsResolver settingsResolver,
    IAnalyticsRuntimeProfileResolver runtimeProfileResolver)
    : IRequestHandler<GetAnalyticsGovernanceSettingsQuery, AnalyticsGovernanceSettingsDto>
{
    public async Task<AnalyticsGovernanceSettingsDto> Handle(
        GetAnalyticsGovernanceSettingsQuery request, CancellationToken cancellationToken)
    {
        var group = await settingsResolver.ResolveGroupAsync<AnalyticsSettingGroup>(
            new SettingContext(), cancellationToken);

        var profile = runtimeProfileResolver.Resolve(group);

        return new AnalyticsGovernanceSettingsDto
        {
            Provider = group.Provider,
            Enabled = group.Enabled,
            EndpointUrl = group.EndpointUrl,
            HasApiKey = !string.IsNullOrEmpty(group.ApiKey),

            CookieConsentEnabled = group.CookieConsentEnabled,
            DeclineBehavior = group.DeclineBehavior,
            ConsentCookieLifetimeDays = group.ConsentCookieLifetimeDays,
            GlobalDisableClientTracking = group.GlobalDisableClientTracking,

            PosthogCookielessMode = group.PosthogCookielessMode,
            PosthogPersonProfiles = group.PosthogPersonProfiles,
            PosthogSessionReplay = group.PosthogSessionReplay,
            PosthogAutocapture = group.PosthogAutocapture,
            PosthogHeatmaps = group.PosthogHeatmaps,
            PosthogToolbar = group.PosthogToolbar,

            CookieBannerRequired = profile.CookieBannerEnabled,
            CanRunBeforeConsent = profile.CanRunBeforeConsent,
            StorageProfile = profile.StorageProfile.ToString(),
            ResolveReasons = profile.ResolveReasons.Select(r => r.ToString()).ToList()
        };
    }
}
