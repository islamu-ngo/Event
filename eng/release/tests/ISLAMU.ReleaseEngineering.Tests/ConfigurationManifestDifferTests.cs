using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ConfigurationManifestDifferTests
{
    private static readonly string BaseOid = new('a', 40);
    private static readonly string HeadOid = new('b', 40);

    [Test]
    public async Task EnvExampleDiffReportsAdditionsRemovalsAndDefaultChangesWhileRedactingAllValues()
    {
        const string baseEnv = """
            # Baseline env
            DATABASE_PROVIDER=postgres
            REMOVED_SECRET_KEY=super-secret-old-value-999
            API_TOKEN_SECRET=super-secret-token-before-111
            # OPTIONAL_FEATURE_FLAG=false
            """;

        const string headEnv = """
            # Baseline env
            DATABASE_PROVIDER=postgres
            API_TOKEN_SECRET=super-secret-token-after-222
            NEW_REQUIRED_URL=
            # OPTIONAL_FEATURE_FLAG=false
            # NEW_OPTIONAL_SETTING=enabled-value-333
            """;

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string> { [".env.example"] = baseEnv })
            .AddCommit(HeadOid, new Dictionary<string, string> { [".env.example"] = headEnv });

        ReleaseContractDiffResult result = ReleaseContractDiffPipeline.Default.Execute(reader, BaseOid, HeadOid);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "config diff");
        await Assert.That(result.Artifact!.Changes.Any(c => c.Kind == "env_var_removed" && c.Severity == ContractChangeSeverity.Breaking && c.Target == "REMOVED_SECRET_KEY")).IsTrue();
        await Assert.That(result.Artifact.Changes.Any(c => c.Kind == "env_var_default_changed" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target == "API_TOKEN_SECRET")).IsTrue();
        await Assert.That(result.Artifact.Changes.Any(c => c.Kind == "env_var_added_required" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target == "NEW_REQUIRED_URL")).IsTrue();
        await Assert.That(result.Artifact.Changes.Any(c => c.Kind == "env_var_added" && c.Severity == ContractChangeSeverity.Additive && c.Target == "NEW_OPTIONAL_SETTING")).IsTrue();

        // Redaction guarantee: none of the secret/default values may appear anywhere in serialized output.
        await Assert.That(result.Json!).DoesNotContain("super-secret-old-value-999");
        await Assert.That(result.Json!).DoesNotContain("super-secret-token-before-111");
        await Assert.That(result.Json!).DoesNotContain("super-secret-token-after-222");
        await Assert.That(result.Json!).DoesNotContain("enabled-value-333");
    }

    [Test]
    public async Task ConfigurationSchemaDiffClassifiesBreakingAdditiveAndReviewRequiredAndIsRepeatable()
    {
        const string baseSchema = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "required": ["apiVersion"],
              "properties": {
                "apiVersion": { "const": "configuration.islamu.org/v1alpha2" },
                "legacyFlag": { "type": "boolean" },
                "maxItems": { "type": "integer", "default": 20 }
              },
              "$defs": {
                "branding": {
                  "type": "object",
                  "properties": {
                    "name": { "type": ["null", "string"] }
                  }
                }
              }
            }
            """;

        const string headSchema = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "required": ["apiVersion", "region"],
              "properties": {
                "apiVersion": { "const": "configuration.islamu.org/v1alpha2" },
                "region": { "type": "string" },
                "optionalNote": { "type": ["null", "string"] },
                "maxItems": { "type": "integer", "default": 50 }
              },
              "$defs": {
                "branding": {
                  "type": "object",
                  "properties": {
                    "name": { "type": "string" }
                  }
                }
              }
            }
            """;

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string> { ["schemas/configuration-manifest-v1alpha2.schema.json"] = baseSchema })
            .AddCommit(HeadOid, new Dictionary<string, string> { ["schemas/configuration-manifest-v1alpha2.schema.json"] = headSchema });

        ReleaseContractDiffResult first = ReleaseContractDiffPipeline.Default.Execute(reader, BaseOid, HeadOid);
        ReleaseContractDiffResult second = ReleaseContractDiffPipeline.Default.Execute(reader, BaseOid, HeadOid);

        await Assert.That(first.IsValid).IsTrue().Because(first.Diagnostic ?? "schema diff");
        await Assert.That(second.Json).IsEqualTo(first.Json);
        await Assert.That(first.Artifact!.Changes.Any(c => c.Kind == "property_removed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("legacyFlag", StringComparison.Ordinal))).IsTrue();
        await Assert.That(first.Artifact.Changes.Any(c => c.Kind == "required_property_added" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("region", StringComparison.Ordinal))).IsTrue();
        await Assert.That(first.Artifact.Changes.Any(c => c.Kind == "optional_property_added" && c.Severity == ContractChangeSeverity.Additive && c.Target.Contains("optionalNote", StringComparison.Ordinal))).IsTrue();
        await Assert.That(first.Artifact.Changes.Any(c => c.Kind == "schema_type_narrowed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("branding", StringComparison.Ordinal))).IsTrue();
        await Assert.That(first.Artifact.Changes.Any(c => c.Kind == "schema_review_required" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target.Contains("maxItems", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ConfigurationSchemaDiffDetectsSiblingLimitAddedBesideUnchangedDollarRef()
    {
        const string baseSchema = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "tenantSlug": { "$ref": "#/$defs/identifier" }
              },
              "$defs": {
                "identifier": { "type": "string" }
              }
            }
            """;

        const string headSchema = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "tenantSlug": { "$ref": "#/$defs/identifier", "maxLength": 48 }
              },
              "$defs": {
                "identifier": { "type": "string" }
              }
            }
            """;

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string> { ["schemas/configuration-manifest-v1alpha2.schema.json"] = baseSchema })
            .AddCommit(HeadOid, new Dictionary<string, string> { ["schemas/configuration-manifest-v1alpha2.schema.json"] = headSchema });

        ReleaseContractDiffResult result = ReleaseContractDiffPipeline.Default.Execute(reader, BaseOid, HeadOid);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "config ref sibling diff");
        await Assert.That(result.Artifact!.Changes.Any(c =>
            c.Kind == "schema_review_required" &&
            c.Severity == ContractChangeSeverity.ReviewRequired &&
            c.Target.Contains("tenantSlug", StringComparison.Ordinal) &&
            c.Summary.Contains("maxLength", StringComparison.Ordinal))).IsTrue();
    }
}
