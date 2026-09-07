namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class RoutingSettingGroup : ISettingGroup
{
    public string DefaultPublicHomePage { get; private set; } = "EventList";
    public bool ResolverHeaderEnabled { get; private set; } = true;
    public bool ResolverSubdomainEnabled { get; private set; } = true;
    public bool ResolverCustomDomainEnabled { get; private set; }
    public bool ResolverPathEnabled { get; private set; }
    public string PathPrefix { get; private set; } = string.Empty;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Routing.DefaultPublicHomePage,
        GovernanceSettingKeys.Routing.ResolverHeaderEnabled,
        GovernanceSettingKeys.Routing.ResolverSubdomainEnabled,
        GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled,
        GovernanceSettingKeys.Routing.ResolverPathEnabled,
        GovernanceSettingKeys.Routing.PathPrefix
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.DefaultPublicHomePage, out var home))
            DefaultPublicHomePage = SettingValueSerializer.Deserialize(home.Value, "EventList");
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.ResolverHeaderEnabled, out var header))
            ResolverHeaderEnabled = SettingValueSerializer.Deserialize(header.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.ResolverSubdomainEnabled, out var sub))
            ResolverSubdomainEnabled = SettingValueSerializer.Deserialize(sub.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled, out var custom))
            ResolverCustomDomainEnabled = SettingValueSerializer.Deserialize(custom.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.ResolverPathEnabled, out var path))
            ResolverPathEnabled = SettingValueSerializer.Deserialize(path.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Routing.PathPrefix, out var prefix))
            PathPrefix = SettingValueSerializer.DeserializeString(prefix.Value);
    }
}
