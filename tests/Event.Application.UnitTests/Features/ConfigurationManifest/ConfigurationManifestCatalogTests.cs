namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;

public sealed class ConfigurationManifestCatalogTests
{
    private static readonly string[] ApprovedSettingKeys =
    [
        "appearance.default_theme_mode",
        "event_reporting.intake_enabled",
        "events.group_submission_enabled",
        "events.organization_submission_enabled",
        "events.require_approval",
        "events.user_submission_enabled",
        "groups.self_registration_enabled",
        "modules.islamic_enabled",
        "modules.tech_enabled",
        "organizations.self_registration_enabled",
        "organizations.verification_required",
        "public_experience.event_catalog_label",
        "public_experience.mode",
        "routing.default_public_home_page",
        "tenants.white_labeling_enabled"
    ];

    [Test]
    public async Task SettingCatalog_ContainsExactlyApprovedKeys()
    {
        string[] actual = ConfigurationManifestCatalog.TenantSettings.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual.SequenceEqual(ApprovedSettingKeys, StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SettingCatalog_EntriesAreRegisteredTenantSafeDefinitions()
    {
        foreach (ConfigurationManifestSettingCatalogEntry entry in
                 ConfigurationManifestCatalog.TenantSettings.Values)
        {
            await Assert.That(entry.Scope).IsEqualTo(ConfigurationManifestScope.Tenant);
            await Assert.That(ReferenceEquals(SettingRegistry.Get(entry.Definition.Key), entry.Definition)).IsTrue();
            await Assert.That(entry.Definition.MinScope <= SettingScope.Tenant).IsTrue();
            await Assert.That(entry.Definition.MaxScope >= SettingScope.Tenant).IsTrue();
            await Assert.That(entry.Definition.IsSensitive).IsFalse();
        }
    }

    [Test]
    public async Task SettingCatalog_ContainsExactlyGuardedPublicationPolicyKeys()
    {
        string[] guarded = ConfigurationManifestCatalog.TenantSettings.Values
            .Where(entry => entry.Definition.RequiresCoordinatedMutation)
            .Select(entry => entry.Definition.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = PublicationPolicySettingKeys.All
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(guarded.SequenceEqual(expected, StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task DocumentCatalog_ContainsExactlyTenantBrandingAndPaidPolicyV1()
    {
        string[] actual = ConfigurationManifestCatalog.TenantDocuments.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(
        [
            "tenant.branding",
            "tenant.paid_event_policy"
        ]);

        ConfigurationManifestDocumentCatalogEntry entry =
            ConfigurationManifestCatalog.TenantDocuments[SettingsDocumentKeys.Tenant.Branding];
        await Assert.That(entry.Scope).IsEqualTo(ConfigurationManifestScope.Tenant);
        await Assert.That(entry.SchemaVersion).IsEqualTo(1);
        await Assert.That(entry.DefaultsVersion).IsEqualTo("2026-05-14");
    }

    [Test]
    public async Task Catalog_DoesNotMirrorRegistryOrDocumentTaxonomy()
    {
        await Assert.That(ConfigurationManifestCatalog.TenantSettings.Count < SettingRegistry.Count).IsTrue();
        await Assert.That(ConfigurationManifestCatalog.TenantDocuments.Count
            < SettingsDocumentKeys.Tenant.All.Count).IsTrue();
    }
}
