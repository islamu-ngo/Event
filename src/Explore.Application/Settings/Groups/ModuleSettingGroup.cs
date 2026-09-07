namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class ModuleSettingGroup : ISettingGroup
{
    public bool IslamicEnabled { get; private set; } = true;
    public bool TechEnabled { get; private set; } = true;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Modules.IslamicEnabled,
        GovernanceSettingKeys.Modules.TechEnabled
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Modules.IslamicEnabled, out var islamic))
            IslamicEnabled = SettingValueSerializer.Deserialize(islamic.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Modules.TechEnabled, out var tech))
            TechEnabled = SettingValueSerializer.Deserialize(tech.Value, true);
    }
}
