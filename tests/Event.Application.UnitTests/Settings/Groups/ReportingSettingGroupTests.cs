namespace Event.Application.UnitTests.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;

public sealed class ReportingSettingGroupTests
{
    [Test]
    public async Task Populate_WithNoSettings_KeepsTenantProvidersDisabled()
    {
        var group = new ReportingSettingGroup();

        group.Populate(new Dictionary<string, ResolvedSetting>());

        await Assert.That(group.EnableTenantOspreyProvider).IsFalse();
        await Assert.That(group.EnableTenantCoopProvider).IsFalse();
        await Assert.That(group.TenantExternalSyncEnabled).IsTrue();
        await Assert.That(group.OspreyRoutingMode).IsEqualTo(ReportingRoutingMode.Both);
        await Assert.That(group.CoopRoutingMode).IsEqualTo(ReportingRoutingMode.Both);
        await Assert.That(group.EvidenceMode).IsEqualTo(EventReportProviderEvidenceMode.MetadataOnly);
        await Assert.That(group.IsTenantOspreyConfigured).IsFalse();
        await Assert.That(group.IsTenantCoopConfigured).IsFalse();
    }

    [Test]
    public async Task Populate_WithEnabledOspreyProvider_RequiresEndpointAndApiKey()
    {
        var group = new ReportingSettingGroup();

        group.Populate(CreateSettings(
            (GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, "true"),
            (GovernanceSettingKeys.Reporting.OspreyEndpointUrl, "\"https://osprey.example\"")));

        await Assert.That(group.EnableTenantOspreyProvider).IsTrue();
        await Assert.That(group.IsTenantOspreyConfigured).IsFalse();

        group.Populate(CreateSettings(
            (GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, "true"),
            (GovernanceSettingKeys.Reporting.OspreyRoutingMode, "\"tenant\""),
            (GovernanceSettingKeys.Reporting.EvidenceMode, "\"ReporterText\""),
            (GovernanceSettingKeys.Reporting.OspreyEndpointUrl, "\"https://osprey.example\""),
            (InfrastructureSecretSettingKeys.Reporting.OspreyApiKey, "\"secret\""),
            (InfrastructureSecretSettingKeys.Reporting.OspreyWebhookSecret, "\"osprey-webhook\"")));

        await Assert.That(group.IsTenantOspreyConfigured).IsTrue();
        await Assert.That(group.OspreyRoutingMode).IsEqualTo(ReportingRoutingMode.Tenant);
        await Assert.That(group.EvidenceMode).IsEqualTo(EventReportProviderEvidenceMode.ReporterText);
        await Assert.That(group.OspreyWebhookSecret).IsEqualTo("osprey-webhook");
    }

    [Test]
    public async Task Populate_WithEnabledCoopProvider_ParsesEndpointCredentialsAndWebhookSecret()
    {
        var group = new ReportingSettingGroup();

        group.Populate(CreateSettings(
            (GovernanceSettingKeys.Reporting.EnableTenantCoopProvider, "true"),
            (GovernanceSettingKeys.Reporting.CoopRoutingMode, "\"instance\""),
            (GovernanceSettingKeys.Reporting.CoopEndpointUrl, "\"https://coop.example\""),
            (InfrastructureSecretSettingKeys.Reporting.CoopApiKey, "\"secret\""),
            (InfrastructureSecretSettingKeys.Reporting.CoopWebhookSecret, "\"webhook-secret\"")));

        await Assert.That(group.EnableTenantCoopProvider).IsTrue();
        await Assert.That(group.IsTenantCoopConfigured).IsTrue();
        await Assert.That(group.CoopRoutingMode).IsEqualTo(ReportingRoutingMode.Instance);
        await Assert.That(group.CoopWebhookSecret).IsEqualTo("webhook-secret");
    }

    private static Dictionary<string, ResolvedSetting> CreateSettings(params (string key, string value)[] entries) =>
        entries.ToDictionary(e => e.key, e => new ResolvedSetting { Value = e.value });
}
