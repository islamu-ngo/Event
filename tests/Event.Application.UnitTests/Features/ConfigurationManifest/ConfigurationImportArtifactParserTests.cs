namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Text;
using System.Text.Json;
using Explore.Application.DTOs.PaidEventPolicies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Importing;

public sealed class ConfigurationImportArtifactParserTests
{
    [Test]
    public async Task Parse_ValidManifestReturnsExactByteDigest()
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            ConfigurationManifestTestData.Valid(),
            ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);

        ConfigurationImportParsedArtifact parsed =
            new ConfigurationImportArtifactParser().Parse(bytes);

        await Assert.That(parsed.ByteLength).IsEqualTo(bytes.Length);
        await Assert.That(parsed.Sha256Digest)
            .IsEqualTo(ConfigurationImportDigest.ComputeBytes(bytes));
        await Assert.That(parsed.Manifest.Kind).IsEqualTo("ConfigurationManifest");
        await Assert.That(parsed.ToString()).DoesNotContain(parsed.Sha256Digest);
    }

    [Test]
    public async Task Parse_DuplicateOrUnknownMemberFailsClosed()
    {
        string canonical = Encoding.UTF8.GetString(
            JsonSerializer.SerializeToUtf8Bytes(
                ConfigurationManifestTestData.Valid(),
                ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2));
        string duplicate = canonical.Replace(
            "\"kind\":\"ConfigurationManifest\",",
            "\"kind\":\"ConfigurationManifest\",\"kind\":\"ConfigurationManifest\",",
            StringComparison.Ordinal);
        string unknown = canonical.Replace(
            "\"kind\":\"ConfigurationManifest\",",
            "\"kind\":\"ConfigurationManifest\",\"unexpected\":true,",
            StringComparison.Ordinal);

        ConfigurationImportSessionException duplicateFailure =
            await Assert.That(() =>
                    new ConfigurationImportArtifactParser().Parse(
                        Encoding.UTF8.GetBytes(duplicate)))
                .Throws<ConfigurationImportSessionException>();
        ConfigurationImportSessionException unknownFailure =
            await Assert.That(() =>
                    new ConfigurationImportArtifactParser().Parse(
                        Encoding.UTF8.GetBytes(unknown)))
                .Throws<ConfigurationImportSessionException>();

        await Assert.That(duplicateFailure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.ContractInvalid);
        await Assert.That(unknownFailure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.ContractInvalid);
    }

    [Test]
    public async Task Parse_InvalidOrOversizedBytesNeverReflectContent()
    {
        const string sentinel = "sensitive-sentinel-value";
        byte[] invalid = Encoding.UTF8.GetBytes($"{{\"value\":\"{sentinel}\"");
        byte[] oversized =
            new byte[ConfigurationImportSessionLimits.MaximumArtifactBytes + 1];

        ConfigurationImportSessionException invalidFailure =
            await Assert.That(() =>
                    new ConfigurationImportArtifactParser().Parse(invalid))
                .Throws<ConfigurationImportSessionException>();
        ConfigurationImportSessionException sizeFailure =
            await Assert.That(() =>
                    new ConfigurationImportArtifactParser().Parse(oversized))
                .Throws<ConfigurationImportSessionException>();

        await Assert.That(invalidFailure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.ContractInvalid);
        await Assert.That(invalidFailure.Message).DoesNotContain(sentinel);
        await Assert.That(sizeFailure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.TooLarge);
    }

    [Test]
    public async Task ParseTenantPackage_AcceptsOnlyStrictTenantArtifact()
    {
        ConfigurationManifestTenantV1Alpha2 tenant =
            ConfigurationManifestTestData.Valid().Spec.Tenants[0];
        var package = new TenantConfigurationPackageV1Alpha2
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
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            package,
            ConfigurationPortabilityJsonContext.Default
                .TenantConfigurationPackageV1Alpha2);

        ConfigurationImportParsedTenantPackage parsed =
            new ConfigurationImportArtifactParser()
                .ParseTenantPackage(bytes);

        await Assert.That(parsed.Package.Kind)
            .IsEqualTo(TenantConfigurationPackageContractMetadata.Kind);
        await Assert.That(parsed.Sha256Digest)
            .IsEqualTo(ConfigurationImportDigest.ComputeBytes(bytes));
        await Assert.That(parsed.ToString())
            .DoesNotContain(parsed.Sha256Digest);

        TenantConfigurationPackageV1Alpha2 wrongAuthority = package with
        {
            Metadata = package.Metadata with
            {
                Export = new ConfigurationManifestExportMetadataV1Alpha2
                {
                    View =
                        ConfigurationManifestExportMetadataValues.PortableView,
                    EffectiveValuesFlattened = true,
                    SensitiveValuesOmitted = true,
                    AuthorityScope =
                        ConfigurationManifestExportMetadataValues
                            .InstanceAndTenantsAuthorityScope,
                    SovereignValuesOmitted = true,
                    SovereignLockedFields =
                        PaidEventPolicyAuthorityMetadata.SovereignLockedFields
                }
            }
        };
        byte[] wrongAuthorityBytes = JsonSerializer.SerializeToUtf8Bytes(
            wrongAuthority,
            ConfigurationPortabilityJsonContext.Default
                .TenantConfigurationPackageV1Alpha2);

        ConfigurationImportSessionException failure =
            await Assert.That(() =>
                    new ConfigurationImportArtifactParser()
                        .ParseTenantPackage(wrongAuthorityBytes))
                .Throws<ConfigurationImportSessionException>();
        await Assert.That(failure.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.ContractInvalid);
    }
}
