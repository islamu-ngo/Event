namespace Explore.Application.Services.Federation;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;

public sealed record AtprotoEventGovernance(
    bool EventsEnabled,
    string ValidationProfile,
    bool PublishMyEvents);

public sealed class AtprotoEventGovernanceResolver(IHierarchicalSettingsResolver settingsResolver)
{
    private static readonly string[] TenantKeys =
    [
        GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
        GovernanceSettingKeys.Federation.AtprotoEventValidationProfile
    ];

    private static readonly string[] ConsentKey =
    [
        GovernanceSettingKeys.Federation.AtprotoPublishMyEvents
    ];

    public async Task<AtprotoEventGovernance> ResolveAsync(
        Guid tenantId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ResolvedSetting> tenantSettings = await settingsResolver.ResolveBatchAsync(
            TenantKeys,
            new SettingContext(TenantId: tenantId),
            cancellationToken);

        var values = tenantSettings.ToDictionary(setting => setting.Key, StringComparer.Ordinal);
        var group = new AtprotoFederationSettingGroup();
        group.Populate(values);

        var publishMyEvents = false;
        if (userId.HasValue)
        {
            IReadOnlyList<ResolvedSetting> consentSettings = await settingsResolver.ResolveBatchAsync(
                ConsentKey,
                new SettingContext(TenantId: tenantId, UserId: userId),
                cancellationToken);
            ResolvedSetting? consent = consentSettings.FirstOrDefault(
                setting => setting.Key == GovernanceSettingKeys.Federation.AtprotoPublishMyEvents);
            publishMyEvents = SettingValueSerializer.DeserializeBool(consent?.Value, false);
        }

        return new AtprotoEventGovernance(
            group.EventsEnabled,
            group.EventValidationProfile,
            publishMyEvents);
    }
}
