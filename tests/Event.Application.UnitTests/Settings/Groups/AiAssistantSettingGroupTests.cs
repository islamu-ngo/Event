using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;

namespace Event.Application.UnitTests.Settings.Groups;

public class AiAssistantSettingGroupTests
{
    private static IReadOnlyDictionary<string, ResolvedSetting> CreateSettings(params (string key, string value)[] entries) =>
        entries.ToDictionary(e => e.key, e => new ResolvedSetting { Value = e.value });

    [Test]
    public async Task Populate_DefaultsToDisabledAndUnavailable()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.Enabled).IsFalse();
        await Assert.That(group.Provider).IsEqualTo("none");
        await Assert.That(group.IsAvailable).IsFalse();
    }

    [Test]
    public async Task Populate_OpenAiCompatibleRequiresEndpointAndModel()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(CreateSettings(
            (GovernanceSettingKeys.AiAssistant.Enabled, "true"),
            (GovernanceSettingKeys.AiAssistant.Provider, "\"openai-compatible\""),
            (GovernanceSettingKeys.AiAssistant.EndpointUrl, "\"https://ai.example.test\""),
            (GovernanceSettingKeys.AiAssistant.ModelId, "\"gpt-test\"")));

        await Assert.That(group.IsConfigured).IsTrue();
        await Assert.That(group.IsAvailable).IsTrue();
    }

    [Test]
    public async Task Populate_OpenAiRequiresApiKeyAndModelButNotEndpoint()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(CreateSettings(
            (GovernanceSettingKeys.AiAssistant.Enabled, "true"),
            (GovernanceSettingKeys.AiAssistant.Provider, "\"openai\""),
            (GovernanceSettingKeys.AiAssistant.ApiKey, "\"test-key\""),
            (GovernanceSettingKeys.AiAssistant.ModelId, "\"gpt-test\"")));

        await Assert.That(group.IsOpenAiProvider).IsTrue();
        await Assert.That(group.IsConfigured).IsTrue();
        await Assert.That(group.IsAvailable).IsTrue();
    }

    [Test]
    public async Task Populate_AnthropicRequiresApiKeyAndModelButNotEndpoint()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(CreateSettings(
            (GovernanceSettingKeys.AiAssistant.Enabled, "true"),
            (GovernanceSettingKeys.AiAssistant.Provider, "\"anthropic\""),
            (GovernanceSettingKeys.AiAssistant.ApiKey, "\"test-key\""),
            (GovernanceSettingKeys.AiAssistant.ModelId, "\"claude-test\"")));

        await Assert.That(group.IsAnthropicProvider).IsTrue();
        await Assert.That(group.IsConfigured).IsTrue();
        await Assert.That(group.IsAvailable).IsTrue();
    }

    [Test]
    public async Task Populate_OpenAiWithoutApiKeyIsUnavailable()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(CreateSettings(
            (GovernanceSettingKeys.AiAssistant.Enabled, "true"),
            (GovernanceSettingKeys.AiAssistant.Provider, "\"openai\""),
            (GovernanceSettingKeys.AiAssistant.ModelId, "\"gpt-test\"")));

        await Assert.That(group.IsConfigured).IsFalse();
        await Assert.That(group.IsAvailable).IsFalse();
    }

    [Test]
    public async Task Populate_OpenAiCompatibleWithoutModelIsUnavailable()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(CreateSettings(
            (GovernanceSettingKeys.AiAssistant.Enabled, "true"),
            (GovernanceSettingKeys.AiAssistant.Provider, "\"openai-compatible\""),
            (GovernanceSettingKeys.AiAssistant.EndpointUrl, "\"https://ai.example.test\""),
            (GovernanceSettingKeys.AiAssistant.ApiKey, "\"test-key\"")));

        await Assert.That(group.IsConfigured).IsFalse();
        await Assert.That(group.IsAvailable).IsFalse();
    }

    [Test]
    public async Task Populate_OpenAiCompatibleWithoutApiKeyIsAvailable()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(CreateSettings(
            (GovernanceSettingKeys.AiAssistant.Enabled, "true"),
            (GovernanceSettingKeys.AiAssistant.Provider, "\"openai-compatible\""),
            (GovernanceSettingKeys.AiAssistant.EndpointUrl, "\"https://ai.example.test\""),
            (GovernanceSettingKeys.AiAssistant.ModelId, "\"gpt-test\"")));

        await Assert.That(group.IsConfigured).IsTrue();
        await Assert.That(group.IsAvailable).IsTrue();
    }

    [Test]
    public async Task Populate_FakeProviderIsNotPubliclyAvailableWithoutCredentials()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(CreateSettings(
            (GovernanceSettingKeys.AiAssistant.Enabled, "true"),
            (GovernanceSettingKeys.AiAssistant.Provider, "\"fake\"")));

        await Assert.That(group.IsFakeProvider).IsTrue();
        await Assert.That(group.IsConfigured).IsFalse();
        await Assert.That(group.IsAvailable).IsFalse();
    }

    [Test]
    public async Task Populate_ParsesLimitsAndFeatureFlags()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(CreateSettings(
            (GovernanceSettingKeys.AiAssistant.MaxInputTokens, "12000"),
            (GovernanceSettingKeys.AiAssistant.MaxOutputTokens, "2048"),
            (GovernanceSettingKeys.AiAssistant.Temperature, "0.4"),
            (GovernanceSettingKeys.AiAssistant.TimeoutSeconds, "45"),
            (GovernanceSettingKeys.AiAssistant.RetentionDays, "14"),
            (GovernanceSettingKeys.AiAssistant.DailyMessageLimit, "25"),
            (GovernanceSettingKeys.AiAssistant.DailyTenantMessageLimit, "500"),
            (GovernanceSettingKeys.AiAssistant.ConcurrentRunLimit, "2"),
            (GovernanceSettingKeys.AiAssistant.SelectedReferenceLimit, "6"),
            (GovernanceSettingKeys.AiAssistant.AllowedModelIds, "[\"gpt-one\",\"gpt-two\"]"),
            (GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled, "true"),
            (GovernanceSettingKeys.AiAssistant.StreamingEnabled, "true")));

        await Assert.That(group.MaxInputTokens).IsEqualTo(12000);
        await Assert.That(group.MaxOutputTokens).IsEqualTo(2048);
        await Assert.That(group.Temperature).IsEqualTo(0.4m);
        await Assert.That(group.TimeoutSeconds).IsEqualTo(45);
        await Assert.That(group.RetentionDays).IsEqualTo(14);
        await Assert.That(group.DailyMessageLimit).IsEqualTo(25);
        await Assert.That(group.DailyTenantMessageLimit).IsEqualTo(500);
        await Assert.That(group.ConcurrentRunLimit).IsEqualTo(2);
        await Assert.That(group.SelectedReferenceLimit).IsEqualTo(6);
        await Assert.That(group.AllowedModelIds).IsEquivalentTo(["gpt-one", "gpt-two"]);
        await Assert.That(group.ToolProposalsEnabled).IsTrue();
        await Assert.That(group.StreamingEnabled).IsTrue();
    }

    [Test]
    public async Task SettingKeys_ContainsAllAiAssistantSettings()
    {
        var keys = AiAssistantSettingGroup.SettingKeys.ToList();

        await Assert.That(keys.Count).IsEqualTo(20);
        await Assert.That(keys).Contains(GovernanceSettingKeys.AiAssistant.Provider);
        await Assert.That(keys).Contains(GovernanceSettingKeys.AiAssistant.ModelId);
        await Assert.That(keys).Contains(GovernanceSettingKeys.AiAssistant.AllowedModelIds);
        await Assert.That(keys).Contains(GovernanceSettingKeys.AiAssistant.DailyTenantMessageLimit);
        await Assert.That(keys).Contains(GovernanceSettingKeys.AiAssistant.ConcurrentRunLimit);
        await Assert.That(keys).Contains(GovernanceSettingKeys.AiAssistant.SelectedReferenceLimit);
        await Assert.That(keys).Contains(GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled);
    }
}
