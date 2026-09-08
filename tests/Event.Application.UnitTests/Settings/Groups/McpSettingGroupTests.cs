using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;

namespace Event.Application.UnitTests.Settings.Groups;

public class McpSettingGroupTests
{
    [Test]
    public async Task Populate_DefaultsToRuntimeEnabledAndLegacySseDisabled()
    {
        var group = new McpSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.Enabled).IsTrue();
        await Assert.That(group.EnableLegacySse).IsFalse();
    }

    [Test]
    public async Task Populate_ParsesRuntimeFlags()
    {
        var group = new McpSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.Mcp.Enabled] = new() { Value = "true" },
            [GovernanceSettingKeys.Mcp.EnableLegacySse] = new() { Value = "true" }
        });

        await Assert.That(group.Enabled).IsTrue();
        await Assert.That(group.EnableLegacySse).IsTrue();
    }

    [Test]
    public async Task SettingKeys_ContainsOnlyRuntimeGovernedMcpSettings()
    {
        var keys = McpSettingGroup.SettingKeys.ToArray();

        await Assert.That(keys).IsEquivalentTo(new[]
        {
            GovernanceSettingKeys.Mcp.Enabled,
            GovernanceSettingKeys.Mcp.EnableLegacySse
        });
    }
}
