namespace Event.Api.IntegrationTests.Features;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Importing;

public sealed class TenantConfigurationPackageAuthorityTests
{
    [Test]
    public async Task PackageMetadata_IsProvenanceOnlyAndCannotCarryTargetIdentity()
    {
        TenantConfigurationPackageV1Alpha2 package = Package();
        Guid trustedTarget = Guid.CreateVersion7();
        ConfigurationImportTarget target =
            ConfigurationImportTarget.ForTenant(trustedTarget);
        string[] publicProperties = typeof(TenantConfigurationPackageV1Alpha2)
            .GetProperties()
            .Concat(typeof(TenantConfigurationPackageMetadataV1Alpha2)
                .GetProperties())
            .Concat(typeof(TenantConfigurationPackageSourceV1Alpha2)
                .GetProperties())
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(target.TenantId).IsEqualTo(trustedTarget);
        await Assert.That(package.Metadata.Source.TenantName)
            .IsEqualTo("source-tenant");
        await Assert.That(publicProperties.Any(property =>
                property.Contains("Target", StringComparison.OrdinalIgnoreCase)
                || property.Contains("TenantId", StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
    }

    [Test]
    public async Task TenantPackageBytes_ContainNoInstanceSectionOrExcludedAuthority()
    {
        ReadOnlyMemory<byte> bytes =
            TenantConfigurationPackageSerializer.Serialize(Package());
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        string json = root.GetRawText();

        await Assert.That(root.GetProperty("kind").GetString())
            .IsEqualTo(TenantConfigurationPackageContractMetadata.Kind);
        await Assert.That(root.GetProperty("spec").TryGetProperty("instance", out _))
            .IsFalse();
        await Assert.That(json).DoesNotContain("excluded.secrets");
        await Assert.That(json).DoesNotContain("providerBindings");
        await Assert.That(json).DoesNotContain("applicationData");
        await Assert.That(json).DoesNotContain("operationalState");
    }

    internal static TenantConfigurationPackageV1Alpha2 Package() =>
        new()
        {
            Schema = TenantConfigurationPackageContractMetadata.SchemaId,
            ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
            Kind = TenantConfigurationPackageContractMetadata.Kind,
            Metadata = new TenantConfigurationPackageMetadataV1Alpha2
            {
                Name = "source-tenant-package",
                Source = new TenantConfigurationPackageSourceV1Alpha2
                {
                    TenantName = "source-tenant",
                    InstanceName = "source-instance"
                }
            },
            Spec = new TenantConfigurationPackageSpecV1Alpha2
            {
                DisplayName = "Source Tenant",
                Settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["events.require_approval"] = JsonSerializer.SerializeToElement(true)
                },
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                    StringComparer.Ordinal),
                LegalDocuments =
                    new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(
                        StringComparer.Ordinal)
            }
        };
}

public sealed class TenantConfigurationMigrationFidelityTests
{
    [Test]
    public async Task EquivalentPortableState_IsVerifiedWithNamedOmissions()
    {
        TenantConfigurationPackageV1Alpha2 package =
            TenantConfigurationPackageAuthorityTests.Package();
        ConfigurationManifestV1Alpha2 manifest = Manifest(package);
        var source = ConfigurationImportArtifactSnapshotFactory
            .FromTenantPackage(package);
        var target = ConfigurationImportArtifactSnapshotFactory
            .FromManifestTenant(manifest, "target-tenant");
        string[] selected = source.Select(section => section.SectionKey).ToArray();
        var input = new ConfigurationImportPreviewInput(
            ConfigurationImportTarget.ForTenant(Guid.CreateVersion7()),
            ConfigurationImportDigest.Compute(["artifact"]),
            ConfigurationImportArtifactSnapshotFactory.RevisionDigest(target),
            source,
            target,
            selected,
            [],
            ConfigurationImportApplyMode.ApplySelected,
            [],
            [],
            new DateTime(2026, 8, 30, 21, 0, 0, DateTimeKind.Utc));

        ConfigurationImportPreview preview =
            new ConfigurationImportPreviewComposer().Compose(input);
        string[] omissions = preview.Items
            .Where(item => item.Category == ConfigurationImportPreviewCategory.Omitted)
            .Select(item => item.SectionKey)
            .ToArray();

        await Assert.That(preview.IsApplyReady).IsTrue();
        await Assert.That(preview.Items
                .Where(item => selected.Contains(item.SectionKey, StringComparer.Ordinal))
                .All(item => item.Category == ConfigurationImportPreviewCategory.Unchanged))
            .IsTrue();
        await Assert.That(omissions).Contains("excluded.secrets");
        await Assert.That(omissions).Contains("excluded.application_data");
        await Assert.That(omissions).Contains("excluded.operational_state");
    }

    private static ConfigurationManifestV1Alpha2 Manifest(
        TenantConfigurationPackageV1Alpha2 package) =>
        new()
        {
            Schema = ConfigurationManifestContractMetadata.SchemaId,
            ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
            Kind = ConfigurationManifestContractMetadata.Kind,
            Metadata = new ConfigurationManifestMetadataV1Alpha2
            {
                Name = "target-instance"
            },
            Spec = new ConfigurationManifestSpecV1Alpha2
            {
                Instance = new ConfigurationManifestInstanceV1Alpha2
                {
                    Settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                    Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                        StringComparer.Ordinal),
                    LegalDocuments =
                        new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(
                            StringComparer.Ordinal)
                },
                Tenants =
                [
                    new ConfigurationManifestTenantV1Alpha2
                    {
                        Metadata = new ConfigurationManifestTenantMetadataV1Alpha2
                        {
                            Name = "target-tenant"
                        },
                        Spec = new ConfigurationManifestTenantSpecV1Alpha2
                        {
                            DisplayName = package.Spec.DisplayName,
                            Settings = package.Spec.Settings,
                            Documents = package.Spec.Documents,
                            LegalDocuments = package.Spec.LegalDocuments
                        }
                    }
                ]
            }
        };
}
