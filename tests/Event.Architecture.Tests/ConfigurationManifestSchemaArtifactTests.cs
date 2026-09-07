namespace Event.Architecture.Tests;

public sealed class ConfigurationManifestSchemaArtifactTests
{
    [Test]
    public async Task ContractSchemaAndGenerator_UseOnlyUnifiedNames()
    {
        string newContract = ContextSystemHelpers.RepoPath(
            "src",
            "Event.Wire.Contracts",
            "ConfigurationPortability",
            "ConfigurationManifestV1Alpha2.cs");
        string oldContract = ContextSystemHelpers.RepoPath(
            "src",
            "Explore.Application",
            "Features",
            "Tenant" + "ConfigurationManifest",
            "Contracts",
            "Tenant" + "ConfigurationManifestV1.cs");
        string newSchema = ContextSystemHelpers.RepoPath(
            "schemas",
            "configuration-manifest-v1alpha2.schema.json");
        string tenantPackageSchema = ContextSystemHelpers.RepoPath(
            "schemas",
            "tenant-configuration-package-v1alpha2.schema.json");
        string oldManifestSchema = ContextSystemHelpers.RepoPath(
            "schemas",
            "configuration-manifest-v1alpha1.schema.json");
        string oldSchema = ContextSystemHelpers.RepoPath(
            "schemas",
            "tenant-" + "configuration-manifest-v1.schema.json");
        string newGenerator = ContextSystemHelpers.RepoPath(
            "eng",
            "configuration-manifest-schema",
            "src",
            "ISLAMU.ConfigurationManifest.SchemaGenerator",
            "ISLAMU.ConfigurationManifest.SchemaGenerator.csproj");
        string oldGenerator = ContextSystemHelpers.RepoPath(
            "eng",
            "tenant-" + "manifest-schema",
            "src",
            "ISLAMU.Tenant" + "Manifest.SchemaGenerator",
            "ISLAMU.Tenant" + "Manifest.SchemaGenerator.csproj");

        await Assert.That(File.Exists(newContract)).IsTrue();
        await Assert.That(File.Exists(oldContract)).IsFalse();
        await Assert.That(File.Exists(newSchema)).IsTrue();
        await Assert.That(File.Exists(tenantPackageSchema)).IsTrue();
        await Assert.That(File.Exists(oldManifestSchema)).IsFalse();
        await Assert.That(File.Exists(oldSchema)).IsFalse();
        await Assert.That(File.Exists(newGenerator)).IsTrue();
        await Assert.That(File.Exists(oldGenerator)).IsFalse();
    }

    [Test]
    public async Task BuildWorkflow_RoutesOnlyUnifiedSchemaAndGenerator()
    {
        string workflow = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            ".github",
            "workflows",
            "test.yml"));

        await Assert.That(workflow.Contains(
            "schemas/configuration-manifest-v1alpha2.schema.json",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(workflow.Contains(
            "schemas/tenant-configuration-package-v1alpha2.schema.json",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(workflow.Contains(
            "eng/configuration-manifest-schema/",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(workflow.Contains(
            "tenant-" + "configuration-manifest-v1.schema.json",
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(workflow.Contains(
            "eng/tenant-" + "manifest-schema/",
            StringComparison.Ordinal)).IsFalse();
    }
}
