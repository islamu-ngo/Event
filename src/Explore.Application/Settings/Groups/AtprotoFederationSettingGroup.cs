namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public sealed class AtprotoFederationSettingGroup : ISettingGroup
{
    public const string PlatformProfile = "platform";
    public const string CommunityLexiconProfile = "community_lexicon";
    public const string DowntimeOnlyBackfillMode = "downtime_only";
    public const string FullBackfillMode = "full";

    public bool EventsEnabled { get; private set; }
    public string EventValidationProfile { get; private set; } = PlatformProfile;
    public bool EventsBackfillEnabled { get; private set; }
    public string EventsBackfillMode { get; private set; } = DowntimeOnlyBackfillMode;
    public bool PublishMyEvents { get; private set; }

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
        GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
        GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled,
        GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode,
        GovernanceSettingKeys.Federation.AtprotoPublishMyEvents
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, out var enabled))
        {
            EventsEnabled = SettingValueSerializer.DeserializeBool(enabled.Value, false);
        }

        if (settings.TryGetValue(GovernanceSettingKeys.Federation.AtprotoPublishMyEvents, out var consent))
        {
            PublishMyEvents = SettingValueSerializer.DeserializeBool(consent.Value, false);
        }

        if (settings.TryGetValue(GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled, out var backfillEnabled))
        {
            EventsBackfillEnabled = SettingValueSerializer.DeserializeBool(backfillEnabled.Value, false);
        }

        if (settings.TryGetValue(GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode, out var backfillMode))
        {
            var mode = SettingValueSerializer.DeserializeString(backfillMode.Value, DowntimeOnlyBackfillMode);
            EventsBackfillMode = string.Equals(mode, FullBackfillMode, StringComparison.OrdinalIgnoreCase)
                ? FullBackfillMode
                : DowntimeOnlyBackfillMode;
        }

        if (!EventsEnabled
            || !settings.TryGetValue(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile, out var profile))
        {
            return;
        }

        var value = SettingValueSerializer.DeserializeString(profile.Value, PlatformProfile);
        EventValidationProfile = string.Equals(value, CommunityLexiconProfile, StringComparison.OrdinalIgnoreCase)
            ? CommunityLexiconProfile
            : PlatformProfile;
    }
}
