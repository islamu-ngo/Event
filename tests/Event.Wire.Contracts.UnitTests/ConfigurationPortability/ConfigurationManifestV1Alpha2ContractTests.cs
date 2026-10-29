namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfigurationManifestV1Alpha2ContractTests
{
    private const string ContractNamespace =
        "ISLAMU.Wire.Contracts.ConfigurationPortability.";

    [Test]
    public async Task ContractMetadata_UsesDistinctV1Alpha2ArtifactIdentities()
    {
        await Assert.That(ConfigurationManifestContractMetadata.SchemaId)
            .IsEqualTo(
                "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json");
        await Assert.That(ConfigurationManifestContractMetadata.ApiVersion)
            .IsEqualTo("configuration.islamu.org/v1alpha2");
        await Assert.That(ConfigurationManifestContractMetadata.Kind)
            .IsEqualTo("ConfigurationManifest");
        await Assert.That(ConfigurationManifestContractMetadata.MediaType)
            .IsEqualTo(
                "application/vnd.islamu.configuration-manifest.v1alpha2+json");

        await Assert.That(TenantConfigurationPackageContractMetadata.SchemaId)
            .IsEqualTo(
                "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json");
        await Assert.That(TenantConfigurationPackageContractMetadata.ApiVersion)
            .IsEqualTo("configuration.islamu.org/v1alpha2");
        await Assert.That(TenantConfigurationPackageContractMetadata.Kind)
            .IsEqualTo("TenantConfigurationPackage");
        await Assert.That(TenantConfigurationPackageContractMetadata.MediaType)
            .IsEqualTo(
                "application/vnd.islamu.tenant-configuration-package.v1alpha2+json");
    }

    [Test]
    public async Task ArtifactRoots_AreDistinctClosedRequiredContracts()
    {
        await AssertClosedRequiredProperties(
            typeof(ConfigurationManifestV1Alpha2),
            "Schema",
            "ApiVersion",
            "Kind",
            "Metadata",
            "Spec");
        await AssertClosedRequiredProperties(
            typeof(TenantConfigurationPackageV1Alpha2),
            "Schema",
            "ApiVersion",
            "Kind",
            "Metadata",
            "Spec");
    }

    [Test]
    public async Task TenantPackageMetadata_CannotSelectTargetAuthority()
    {
        string[] propertyPaths = PublicContractPropertyPaths(typeof(TenantConfigurationPackageMetadataV1Alpha2))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] forbiddenAuthorityMembers =
        [
            "Target",
            "TargetTenant",
            "TargetTenantId",
            "TargetInstance",
            "TargetInstanceId",
            "TenantId",
            "InstanceId",
            "ActorId",
            "UserId"
        ];

        foreach (string propertyPath in propertyPaths)
        {
            string member = propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries)[^1];
            await Assert.That(
                    forbiddenAuthorityMembers.Contains(member, StringComparer.Ordinal)
                    || member.StartsWith("Target", StringComparison.Ordinal))
                .IsFalse();
        }
    }

    [Test]
    public async Task ApplyMode_UsesOnlyNamedNonImplicitSemantics()
    {
        await Assert.That(Enum.GetNames<ConfigurationImportApplyMode>()).IsEquivalentTo(
        [
            "PreviewOnly",
            "CreateNew",
            "MergeMissing",
            "ApplySelected",
            "ReplacePortableConfiguration",
            "ReconcileManaged"
        ]);
    }

    [Test]
    public async Task LegalContentLimits_AreBoundedByTheArtifactContract()
    {
        await Assert.That(ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes).IsBetween(1_048_576, 16_777_216);
        await Assert.That(ConfigurationPortabilityContentLimits.MaximumLegalDocumentsPerScope).IsBetween(1, 64);
        await Assert.That(ConfigurationPortabilityContentLimits.MaximumLegalLocalesPerDocument).IsBetween(1, 64);
        await Assert.That(ConfigurationPortabilityContentLimits.MaximumLegalMarkdownUtf8BytesPerLocale)
            .IsBetween(1_024, ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes);
        await Assert.That(ConfigurationPortabilityContentLimits.MaximumLegalLinksPerLocale).IsBetween(1, 256);
        await Assert.That(ConfigurationPortabilityContentLimits.MaximumLegalPlaceholdersPerLocale).IsBetween(1, 128);
    }

    [Test]
    public async Task V1Alpha1Artifacts_AreRejectedWithoutAliases()
    {
        byte[] manifest = Encoding.UTF8.GetBytes(ConfigurationPortabilityExpectedVectors.ManifestJson.Replace(
            ConfigurationPortabilityExpectedVectors.ApiVersion, "configuration.islamu.org/v1alpha1", StringComparison.Ordinal));
        byte[] package = Encoding.UTF8.GetBytes(ConfigurationPortabilityExpectedVectors.PackageJson.Replace(
            ConfigurationPortabilityExpectedVectors.ApiVersion, "configuration.islamu.org/v1alpha1", StringComparison.Ordinal));

        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(manifest))
            .Throws<ConfigurationPortabilityContractException>();
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(package))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task LegalDocumentContract_IsSourceOnlyAndAcceptanceFree()
    {
        foreach (Type owner in new[]
                 {
                     typeof(ConfigurationManifestInstanceV1Alpha2),
                     typeof(ConfigurationManifestTenantSpecV1Alpha2),
                     typeof(TenantConfigurationPackageSpecV1Alpha2)
                 })
        {
            await Assert.That(owner.GetProperty(nameof(ConfigurationManifestInstanceV1Alpha2.LegalDocuments))).IsNotNull();
        }

        string[] forbidden =
        [
            "AcceptedAt",
            "AcceptedBy",
            "AcceptanceHistory",
            "AcceptanceRecordId",
            "TargetTenantId",
            "TargetInstanceId",
            "UserId",
            "SubjectId"
        ];
        foreach (Type type in new[]
                 {
                     typeof(ConfigurationManifestLegalDocumentV1Alpha2),
                     typeof(ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2),
                     typeof(ConfigurationManifestLegalTemplateProvenanceV1Alpha2)
                 })
        {
            await Assert.That(type.GetProperties()
                    .Select(property => property.Name)
                    .Intersect(forbidden, StringComparer.Ordinal))
                .IsEmpty();
        }
    }

    private static PropertyInfo RequiredProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"Missing required contract property '{type.FullName}.{name}'.");

    private static async Task AssertClosedRequiredProperties(
        Type type,
        params string[] expectedNames)
    {
        JsonUnmappedMemberHandlingAttribute? unmapped =
            type.GetCustomAttribute<JsonUnmappedMemberHandlingAttribute>();
        await Assert.That(unmapped).IsNotNull();
        await Assert.That(unmapped!.UnmappedMemberHandling)
            .IsEqualTo(JsonUnmappedMemberHandling.Disallow);

        string[] actualNames = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(actualNames).IsEquivalentTo(expectedNames);

        foreach (string name in expectedNames)
        {
            await Assert.That(
                    RequiredProperty(type, name)
                        .GetCustomAttribute<RequiredMemberAttribute>())
                .IsNotNull();
        }
    }

    private static IEnumerable<string> PublicContractPropertyPaths(
        Type type,
        string prefix = "",
        int depth = 0)
    {
        if (depth > 4)
            yield break;

        foreach (PropertyInfo property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            string path = string.IsNullOrEmpty(prefix)
                ? property.Name
                : $"{prefix}.{property.Name}";
            yield return path;

            Type nested = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            if (nested.Namespace == ContractNamespace.TrimEnd('.'))
            {
                foreach (string child in PublicContractPropertyPaths(
                             nested,
                             path,
                             depth + 1))
                {
                    yield return child;
                }
            }
        }
    }
}
