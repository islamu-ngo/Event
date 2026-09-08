namespace Event.Domain.UnitTests.Settings;

using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;

public class TenantSettingsDocumentTests
{
    [Test]
    public async Task TenantSettingsDocument_ImplementsTenantAuditableConcurrencyInterfaces()
    {
        var document = TenantSettingsDocument.Create(
            Guid.NewGuid(),
            SettingsDocumentKeys.Tenant.PublicExperience,
            schemaVersion: 1,
            defaultsVersion: "2026-05-tenant-defaults",
            payloadJson: "{\"mode\":\"tenant\"}");

        await Assert.That(document is ITenantEntity).IsTrue();
        await Assert.That(document is IAuditableEntity).IsTrue();
        await Assert.That(document is IConcurrencyAware).IsTrue();
    }

    [Test]
    public async Task Create_AcceptsApprovedTenantDocumentWithObjectPayload()
    {
        var tenantId = Guid.NewGuid();

        var document = TenantSettingsDocument.Create(
            tenantId,
            SettingsDocumentKeys.Tenant.Branding,
            schemaVersion: 1,
            defaultsVersion: "2026-05-branding-defaults",
            payloadJson: "{\"displayName\":\"Open Islamu\"}");

        await Assert.That(document.TenantId).IsEqualTo(tenantId);
        await Assert.That(document.DocumentKey).IsEqualTo(SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(document.SchemaVersion).IsEqualTo(1);
        await Assert.That(document.DefaultsVersion).IsEqualTo("2026-05-branding-defaults");
    }

    [Test]
    public async Task Create_RequiresPositiveSchemaVersion()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(
            TenantSettingsDocument.Create(
                Guid.NewGuid(),
                SettingsDocumentKeys.Tenant.EventDefaults,
                schemaVersion: 0,
                defaultsVersion: "2026-05-event-defaults",
                payloadJson: "{\"requireApproval\":true}")));
    }

    [Test]
    public async Task Create_RejectsInfrastructureSecretKeys()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            TenantSettingsDocument.Create(
                Guid.NewGuid(),
                InfrastructureSecretSettingKeys.Email.SmtpPassword,
                schemaVersion: 1,
                defaultsVersion: "2026-05-secrets",
                payloadJson: "{\"value\":\"secret\"}")));
    }

    [Test]
    public async Task Create_RejectsScalarPayloadJson()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            TenantSettingsDocument.Create(
                Guid.NewGuid(),
                SettingsDocumentKeys.Tenant.ModuleGovernance,
                schemaVersion: 1,
                defaultsVersion: "2026-05-modules",
                payloadJson: "true")));
    }

    [Test]
    public async Task Taxonomy_TenantDocumentsAreNonSecretAndExcludeInfrastructureSecretKeys()
    {
        foreach (var documentKey in SettingsDocumentTaxonomy.TenantDocumentKeys)
        {
            await Assert.That(SettingsDocumentTaxonomy.IsNonSecretTenantDocument(documentKey)).IsTrue();
            await Assert.That(SettingsDocumentTaxonomy.IsKnownSecretKey(documentKey)).IsFalse();
        }

        var knownSecretKeys = new[]
        {
            InfrastructureSecretSettingKeys.Email.SmtpUsername,
            InfrastructureSecretSettingKeys.Email.SmtpPassword,
            InfrastructureSecretSettingKeys.Storage.AccessKeyId,
            InfrastructureSecretSettingKeys.Storage.SecretAccessKey,
            InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername,
            InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword,
            InfrastructureSecretSettingKeys.Authentication.KeycloakClientSecret,
            InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret,
        };

        foreach (var secretKey in knownSecretKeys)
        {
            await Assert.That(SettingsDocumentTaxonomy.IsKnownSecretKey(secretKey)).IsTrue();
            await Assert.That(SettingsDocumentTaxonomy.IsNonSecretTenantDocument(secretKey)).IsFalse();
        }
    }
}
