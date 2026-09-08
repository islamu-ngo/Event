namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfigurationManifestV1Alpha2ContractTests
{
    private const string ContractNamespace =
        "ISLAMU.Wire.Contracts.ConfigurationPortability.";

    private static readonly Assembly WireAssembly =
        typeof(ConfigurationManifestV1Alpha2).Assembly;

    [Test]
    public async Task ContractMetadata_UsesDistinctV1Alpha2ArtifactIdentities()
    {
        Type? manifestMetadata = ContractType("ConfigurationManifestContractMetadata");
        Type? tenantPackageMetadata =
            ContractType("TenantConfigurationPackageContractMetadata");

        await Assert.That(manifestMetadata).IsNotNull();
        await Assert.That(tenantPackageMetadata).IsNotNull();

        await Assert.That(ReadConstant(manifestMetadata!, "SchemaId"))
            .IsEqualTo(
                "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json");
        await Assert.That(ReadConstant(manifestMetadata, "ApiVersion"))
            .IsEqualTo("configuration.islamu.org/v1alpha2");
        await Assert.That(ReadConstant(manifestMetadata, "Kind"))
            .IsEqualTo("ConfigurationManifest");
        await Assert.That(ReadConstant(manifestMetadata, "MediaType"))
            .IsEqualTo(
                "application/vnd.islamu.configuration-manifest.v1alpha2+json");

        await Assert.That(ReadConstant(tenantPackageMetadata!, "SchemaId"))
            .IsEqualTo(
                "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json");
        await Assert.That(ReadConstant(tenantPackageMetadata, "ApiVersion"))
            .IsEqualTo("configuration.islamu.org/v1alpha2");
        await Assert.That(ReadConstant(tenantPackageMetadata, "Kind"))
            .IsEqualTo("TenantConfigurationPackage");
        await Assert.That(ReadConstant(tenantPackageMetadata, "MediaType"))
            .IsEqualTo(
                "application/vnd.islamu.tenant-configuration-package.v1alpha2+json");
    }

    [Test]
    public async Task ArtifactRoots_AreDistinctClosedRequiredContracts()
    {
        Type? manifest = ContractType("ConfigurationManifestV1Alpha2");
        Type? tenantPackage = ContractType("TenantConfigurationPackageV1Alpha2");

        await Assert.That(manifest).IsNotNull();
        await Assert.That(tenantPackage).IsNotNull();
        await Assert.That(manifest).IsNotEqualTo(tenantPackage);

        await AssertClosedRequiredProperties(
            manifest!,
            "Schema",
            "ApiVersion",
            "Kind",
            "Metadata",
            "Spec");
        await AssertClosedRequiredProperties(
            tenantPackage!,
            "Schema",
            "ApiVersion",
            "Kind",
            "Metadata",
            "Spec");
    }

    [Test]
    public async Task TenantPackageMetadata_CannotSelectTargetAuthority()
    {
        Type? tenantPackage = ContractType("TenantConfigurationPackageV1Alpha2");
        await Assert.That(tenantPackage).IsNotNull();

        Type metadataType = RequiredProperty(tenantPackage!, "Metadata").PropertyType;
        string[] propertyPaths = PublicContractPropertyPaths(metadataType)
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
        Type? applyMode = ContractType("ConfigurationImportApplyMode");

        await Assert.That(applyMode).IsNotNull();
        await Assert.That(applyMode!.IsEnum).IsTrue();
        await Assert.That(Enum.GetNames(applyMode)).IsEquivalentTo(
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
        Type? limits = ContractType("ConfigurationPortabilityContentLimits");
        await Assert.That(limits).IsNotNull();

        int artifactBytes = ReadIntConstant(limits!, "MaximumArtifactUtf8Bytes");
        int legalDocuments = ReadIntConstant(limits, "MaximumLegalDocumentsPerScope");
        int locales = ReadIntConstant(limits, "MaximumLegalLocalesPerDocument");
        int markdownBytes = ReadIntConstant(
            limits,
            "MaximumLegalMarkdownUtf8BytesPerLocale");
        int links = ReadIntConstant(limits, "MaximumLegalLinksPerLocale");
        int placeholders = ReadIntConstant(
            limits,
            "MaximumLegalPlaceholdersPerLocale");

        await Assert.That(artifactBytes).IsBetween(1_048_576, 16_777_216);
        await Assert.That(legalDocuments).IsBetween(1, 64);
        await Assert.That(locales).IsBetween(1, 64);
        await Assert.That(markdownBytes).IsBetween(1_024, artifactBytes);
        await Assert.That(links).IsBetween(1, 256);
        await Assert.That(placeholders).IsBetween(1, 128);
    }

    [Test]
    public async Task V1Alpha1ContractTypes_AreRemovedWithoutAliases()
    {
        string[] obsoleteTypes =
        [
            "ConfigurationManifestV1Alpha1",
            "ConfigurationManifestMetadataV1Alpha1",
            "ConfigurationManifestSpecV1Alpha1",
            "ConfigurationManifestInstanceV1Alpha1",
            "ConfigurationManifestTenantV1Alpha1",
            "ConfigurationManifestDocumentV1Alpha1"
        ];

        foreach (string obsoleteType in obsoleteTypes)
            await Assert.That(ContractType(obsoleteType)).IsNull();
    }

    [Test]
    public async Task LegalDocumentContract_IsSourceOnlyAndAcceptanceFree()
    {
        Type? legalDocument = ContractType("ConfigurationManifestLegalDocumentV1Alpha2");
        Type? localizedSource =
            ContractType("ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2");
        Type? templateProvenance =
            ContractType("ConfigurationManifestLegalTemplateProvenanceV1Alpha2");

        await Assert.That(legalDocument).IsNotNull();
        await Assert.That(localizedSource).IsNotNull();
        await Assert.That(templateProvenance).IsNotNull();

        foreach (string ownerTypeName in new[]
                 {
                     "ConfigurationManifestInstanceV1Alpha2",
                     "ConfigurationManifestTenantSpecV1Alpha2",
                     "TenantConfigurationPackageSpecV1Alpha2"
                 })
        {
            Type owner = ContractType(ownerTypeName)!;
            await Assert.That(owner.GetProperty("LegalDocuments")).IsNotNull();
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
                     legalDocument!,
                     localizedSource!,
                     templateProvenance!
                 })
        {
            await Assert.That(type.GetProperties()
                    .Select(property => property.Name)
                    .Intersect(forbidden, StringComparer.Ordinal))
                .IsEmpty();
        }
    }

    private static Type? ContractType(string name) =>
        WireAssembly.GetType(ContractNamespace + name);

    private static string ReadConstant(Type type, string name) =>
        type.GetField(name, BindingFlags.Public | BindingFlags.Static)?
            .GetRawConstantValue() as string
        ?? throw new InvalidOperationException($"Missing contract constant '{name}'.");

    private static int ReadIntConstant(Type type, string name) =>
        type.GetField(name, BindingFlags.Public | BindingFlags.Static)?
            .GetRawConstantValue() as int?
        ?? throw new InvalidOperationException($"Missing contract constant '{name}'.");

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
