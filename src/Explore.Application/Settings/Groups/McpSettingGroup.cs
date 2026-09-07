namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class McpSettingGroup : ISettingGroup
{
    public bool Enabled { get; private set; } = true;
    public bool EnableLegacySse { get; private set; }

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Mcp.Enabled,
        GovernanceSettingKeys.Mcp.EnableLegacySse
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Mcp.Enabled, out var enabled))
            Enabled = SettingValueSerializer.Deserialize(enabled.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Mcp.EnableLegacySse, out var legacySse))
            EnableLegacySse = SettingValueSerializer.Deserialize(legacySse.Value, false);
    }
}
