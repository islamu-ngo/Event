namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class DeploymentSettingGroup : ISettingGroup
{
    public string Mode { get; private set; } = "SingleTenant";

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Deployment.Mode
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Deployment.Mode, out var mode))
            Mode = SettingValueSerializer.Deserialize(mode.Value, "SingleTenant");
    }
}
