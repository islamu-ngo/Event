namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class GroupSettingGroup : ISettingGroup
{
    public bool SelfRegistrationEnabled { get; private set; } = true;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Groups.SelfRegistrationEnabled
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Groups.SelfRegistrationEnabled, out var selfReg))
            SelfRegistrationEnabled = SettingValueSerializer.Deserialize(selfReg.Value, true);
    }
}
