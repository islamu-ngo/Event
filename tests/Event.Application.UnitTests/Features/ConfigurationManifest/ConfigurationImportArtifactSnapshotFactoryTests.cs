namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Importing;

public sealed class ConfigurationImportArtifactSnapshotFactoryTests
{
    [Test]
    public async Task ManifestProjection_CoversCanonicalInstanceAndTenantSections()
    {
        ConfigurationManifestV1Alpha2 manifest =
            ConfigurationManifestTestData.Valid();

        ConfigurationImportSectionSnapshot[] sections =
            ConfigurationImportArtifactSnapshotFactory
                .FromManifest(manifest)
                .ToArray();

        await Assert.That(sections.Select(section => section.SectionKey))
            .IsEquivalentTo(
            [
                "instance.settings",
                "instance.documents",
                "instance.legal_documents",
                "tenant.settings",
                "tenant.documents",
                "tenant.legal_documents"
            ]);
        await Assert.That(
                ConfigurationImportArtifactSnapshotFactory.RevisionDigest(
                    sections))
            .IsEqualTo(
                ConfigurationImportArtifactSnapshotFactory.RevisionDigest(
                    sections.Reverse()));
    }

    [Test]
    public async Task TenantPackageAndTargetTenant_UseSameCanonicalDigests()
    {
        ConfigurationManifestV1Alpha2 manifest =
            ConfigurationManifestTestData.Valid();
        ConfigurationManifestTenantV1Alpha2 tenant =
            manifest.Spec.Tenants[0];
        TenantConfigurationPackageV1Alpha2 package = Package(tenant);

        ConfigurationImportSectionSnapshot[] source =
            ConfigurationImportArtifactSnapshotFactory
                .FromTenantPackage(package)
                .ToArray();
        ConfigurationImportSectionSnapshot[] target =
            ConfigurationImportArtifactSnapshotFactory
                .FromManifestTenant(
                    manifest,
                    tenant.Metadata.Name)
                .ToArray();

        await Assert.That(source.Select(section => section.SectionKey))
            .IsEquivalentTo(
            [
                "tenant.settings",
                "tenant.documents",
                "tenant.legal_documents"
            ]);
        await Assert.That(source.Select(section => section.CanonicalDigest))
            .IsEquivalentTo(
                target.Select(section => section.CanonicalDigest));
    }

    [Test]
    public async Task IdenticalServerDerivedSections_ArePreviewedAsUnchanged()
    {
        ConfigurationManifestV1Alpha2 manifest =
            ConfigurationManifestTestData.Valid();
        ConfigurationImportSectionSnapshot[] sections =
            ConfigurationImportArtifactSnapshotFactory
                .FromManifest(manifest)
                .ToArray();
        string[] selected = sections
            .Select(section => section.SectionKey)
            .ToArray();
        string artifactDigest = Digest("artifact");
        var input = new ConfigurationImportPreviewInput(
            ConfigurationImportTarget.ForInstance(),
            artifactDigest,
            ConfigurationImportArtifactSnapshotFactory.RevisionDigest(
                sections),
            sections,
            sections,
            selected,
            new Dictionary<string, string>(),
            ConfigurationImportApplyMode.PreviewOnly,
            requiredApprovalCodes: [],
            grantedApprovalCodes: [],
            expiresAt: new DateTime(
                2026,
                8,
                30,
                22,
                0,
                0,
                DateTimeKind.Utc));

        ConfigurationImportPreview preview =
            new ConfigurationImportPreviewComposer().Compose(input);

        await Assert.That(preview.Items.Where(item =>
                selected.Contains(item.SectionKey, StringComparer.Ordinal))
            .All(item =>
                item.Category ==
                ConfigurationImportPreviewCategory.Unchanged))
            .IsTrue();
        await Assert.That(preview.IsApplyReady).IsTrue();
    }

    [Test]
    public async Task NumericLexicalVariants_HaveOneSemanticSectionDigest()
    {
        ConfigurationManifestV1Alpha2 manifest =
            ConfigurationManifestTestData.Valid();
        ConfigurationManifestTenantV1Alpha2 tenant =
            manifest.Spec.Tenants[0];
        var manifestSettings =
            new Dictionary<string, JsonElement>(
                tenant.Spec.Settings,
                StringComparer.Ordinal)
            {
                ["custom.numeric"] = JsonSerializer.SerializeToElement(1)
            };
        using JsonDocument decimalDocument = JsonDocument.Parse("1.0");
        var packageSettings =
            new Dictionary<string, JsonElement>(
                tenant.Spec.Settings,
                StringComparer.Ordinal)
            {
                ["custom.numeric"] = decimalDocument.RootElement.Clone()
            };
        ConfigurationManifestTenantV1Alpha2 manifestTenant = tenant with
        {
            Spec = tenant.Spec with { Settings = manifestSettings }
        };
        ConfigurationManifestV1Alpha2 target = manifest with
        {
            Spec = manifest.Spec with { Tenants = [manifestTenant] }
        };
        TenantConfigurationPackageV1Alpha2 package =
            Package(tenant) with
            {
                Spec = Package(tenant).Spec with
                {
                    Settings = packageSettings
                }
            };

        string sourceDigest =
            ConfigurationImportArtifactSnapshotFactory
                .FromTenantPackage(package)
                .Single(section =>
                    section.SectionKey == "tenant.settings")
                .CanonicalDigest;
        string targetDigest =
            ConfigurationImportArtifactSnapshotFactory
                .FromManifestTenant(target, tenant.Metadata.Name)
                .Single(section =>
                    section.SectionKey == "tenant.settings")
                .CanonicalDigest;

        await Assert.That(sourceDigest).IsEqualTo(targetDigest);
    }

    private static TenantConfigurationPackageV1Alpha2 Package(
        ConfigurationManifestTenantV1Alpha2 tenant) =>
        new()
        {
            Schema = TenantConfigurationPackageContractMetadata.SchemaId,
            ApiVersion =
                TenantConfigurationPackageContractMetadata.ApiVersion,
            Kind = TenantConfigurationPackageContractMetadata.Kind,
            Metadata = new TenantConfigurationPackageMetadataV1Alpha2
            {
                Name = tenant.Metadata.Name,
                Source = new TenantConfigurationPackageSourceV1Alpha2
                {
                    TenantName = tenant.Metadata.Name
                }
            },
            Spec = new TenantConfigurationPackageSpecV1Alpha2
            {
                DisplayName = tenant.Spec.DisplayName,
                Settings = tenant.Spec.Settings,
                Documents = tenant.Spec.Documents,
                LegalDocuments = tenant.Spec.LegalDocuments
            }
        };

    private static string Digest(string value) =>
        ConfigurationImportDigest.ComputeBytes(
            System.Text.Encoding.UTF8.GetBytes(value));
}
