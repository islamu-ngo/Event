using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;

namespace Event.Application.UnitTests.Settings;

public sealed class HierarchicalSettingMergeFactsTests
{
    [Test]
    public async Task LockedInstanceConsentRejectsLowerScopeValuesAndPreservesMetadata()
    {
        string key = GovernanceSettingKeys.Federation.AtprotoPublishMyEvents;
        var facts = new SettingMergeFacts(
            new SystemSettingFacts("false", SettingValueType.Boolean, true, "Consent", "Federation", "[true,false]"),
            new TenantSettingFacts("true", false),
            Organization: null,
            Group: null,
            User: "true",
            HasTenantContext: true,
            SmtpDelegationValue: null);

        ResolvedSetting? resolved = HierarchicalSettingMerge.Resolve(key, facts);

        await Assert.That(resolved?.Value).IsEqualTo("false");
        await Assert.That(resolved?.Source).IsEqualTo(SettingSource.SystemLocked);
        await Assert.That(resolved?.IsLocked).IsTrue();
        await Assert.That(resolved?.ValueType).IsEqualTo(SettingValueType.Boolean);
        await Assert.That(resolved?.AllowedValues).IsEqualTo("[true,false]");
    }

    [Test]
    public async Task TenantFederationCapabilityRejectsOutOfScopeUserPreference()
    {
        string key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
        var facts = new SettingMergeFacts(
            new SystemSettingFacts("false", SettingValueType.Boolean, false, null, null, null),
            new TenantSettingFacts("true", false),
            Organization: null,
            Group: null,
            User: "false",
            HasTenantContext: true,
            SmtpDelegationValue: null);

        ResolvedSetting? resolved = HierarchicalSettingMerge.Resolve(key, facts);

        await Assert.That(resolved?.Value).IsEqualTo("true");
        await Assert.That(resolved?.Source).IsEqualTo(SettingSource.TenantOverride);
        await Assert.That(resolved?.IsLocked).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task MissingSmtpDelegationLocksEvenEmptyTenantContextButNotInstanceContext(bool tenantContext)
    {
        string key = EmailSettingDefinitions.All.First().Key;
        var facts = new SettingMergeFacts(
            new SystemSettingFacts("instance", SettingValueType.String, false, null, null, null),
            Tenant: null,
            Organization: null,
            Group: null,
            User: null,
            HasTenantContext: tenantContext,
            SmtpDelegationValue: null);

        ResolvedSetting? resolved = HierarchicalSettingMerge.Resolve(key, facts);

        await Assert.That(resolved?.Value).IsEqualTo("instance");
        await Assert.That(resolved?.IsLocked).IsEqualTo(tenantContext);
        await Assert.That(resolved?.Source).IsEqualTo(
            tenantContext ? SettingSource.SystemLocked : SettingSource.SystemDefault);

        var system = new Dictionary<string, SystemSetting>
        {
            [key] = new() { SettingKey = key, Value = "instance", ValueType = SettingValueType.String }
        };
        var fromEntities = HierarchicalSettingMerge.Resolve(
            key, system, tenantContext ? new Dictionary<string, TenantSetting>() : null);
        await Assert.That(fromEntities?.Value).IsEqualTo("instance");
        await Assert.That(fromEntities?.IsLocked).IsEqualTo(tenantContext);
        await Assert.That(fromEntities?.Source).IsEqualTo(
            tenantContext ? SettingSource.SystemLocked : SettingSource.SystemDefault);
    }
}
