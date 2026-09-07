namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Text;

internal static class ConfigurationPortabilityExpectedVectors
{
    internal const string Namespace =
        "ISLAMU.Wire.Contracts.ConfigurationPortability";
    internal const string ManifestSchemaId =
        "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json";
    internal const string PackageSchemaId =
        "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json";
    internal const string ApiVersion = "configuration.islamu.org/v1alpha2";
    internal const int MaximumArtifactUtf8Bytes = 4 * 1024 * 1024;
    internal const int MaximumJsonDepth = 32;
    internal const int MaximumTenantCount = 256;
    internal const int MaximumLegalDocumentsPerScope = 16;
    internal const int MaximumLegalLocalesPerDocument = 32;
    internal const int MaximumLegalMarkdownUtf8BytesPerLocale = 256 * 1024;
    internal const int MaximumLegalLinksPerLocale = 128;
    internal const int MaximumLegalPlaceholdersPerLocale = 64;
    internal const int MaximumLegalTitleLength = 200;
    internal const int MaximumLegalSummaryLength = 500;
    internal const int MaximumLanguageTagLength = 35;
    internal const int MaximumLegalLinkLength = 2048;
    internal const int MaximumLegalIdentityValueLength = 500;

    internal const string ManifestJson =
        "{\"$schema\":\"https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json\",\"apiVersion\":\"configuration.islamu.org/v1alpha2\",\"kind\":\"ConfigurationManifest\",\"metadata\":{\"name\":\"primary-deployment\"},\"spec\":{\"instance\":{\"settings\":{\"events.require_approval\":true},\"documents\":{},\"legalDocuments\":{}},\"tenants\":[{\"metadata\":{\"name\":\"default\"},\"spec\":{\"displayName\":\"Primary Community\",\"settings\":{},\"documents\":{},\"legalDocuments\":{}}}]}}";
    internal const string ManifestSha256 =
        "5db39807ece53e930dc420429dd8ad56b77a1dca076053f90752eb81f831c6db";
    internal const string PackageJson =
        "{\"$schema\":\"https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json\",\"apiVersion\":\"configuration.islamu.org/v1alpha2\",\"kind\":\"TenantConfigurationPackage\",\"metadata\":{\"name\":\"primary-community\",\"source\":{\"tenantName\":\"default\"}},\"spec\":{\"displayName\":\"Primary Community\",\"settings\":{\"events.require_approval\":true},\"documents\":{},\"legalDocuments\":{}}}";
    internal const string PackageSha256 =
        "2f73ea25f9e2fd7e4ed120636596f1fae0ff0a832188d2aa2122d5d808659ae4";

    internal const string ContractInvalid = "configuration_portability_contract_invalid";
    internal const string TooLarge = "configuration_portability_too_large";
    internal const string DepthExceeded = "configuration_portability_depth_exceeded";
    internal const string CountExceeded = "configuration_portability_count_exceeded";
    internal const string StringTooLong = "configuration_portability_string_too_long";
    internal const string ForbiddenMember =
        "configuration_portability_sensitive_member_forbidden";
    internal const string ScopeInvalid = "configuration_portability_scope_invalid";

    internal const string LegalMarkdown =
        "# Policy\n\n- First item\n- **Strong** and *emphasized*\n\n> Quoted text\n\n| A | B |\n| - | - |\n\nRead [policy details](https://example.test/legal).";
    internal const string LegalHtml =
        "<h2>Policy</h2>\n<ul>\n<li>First item</li>\n<li><strong>Strong</strong> and <em>emphasized</em></li>\n</ul>\n<p>&gt; Quoted text</p>\n<p>| A | B | | - | - |</p>\n<p>Read <a href=\"https://example.test/legal\" rel=\"noopener noreferrer\">policy details</a>.</p>\n";

    internal static byte[] ManifestBytes => Encoding.UTF8.GetBytes(ManifestJson);
    internal static byte[] PackageBytes => Encoding.UTF8.GetBytes(PackageJson);

    internal static IReadOnlyList<InvalidArtifactVector> InvalidArtifacts =>
    [
        new("unknown-member", ManifestJson.Replace("\"metadata\":", "\"unexpected\":true,\"metadata\":", StringComparison.Ordinal), ContractInvalid, "$.unexpected"),
        new("duplicate-member", ManifestJson.Replace("\"kind\":\"ConfigurationManifest\",", "\"kind\":\"ConfigurationManifest\",\"kind\":\"ConfigurationManifest\",", StringComparison.Ordinal), ContractInvalid, "$.kind"),
        new("wrong-casing", ManifestJson.Replace("\"apiVersion\":", "\"ApiVersion\":", StringComparison.Ordinal), ContractInvalid, "$.ApiVersion"),
        new("wrong-type", ManifestJson.Replace("\"apiVersion\":\"configuration.islamu.org/v1alpha2\"", "\"apiVersion\":7", StringComparison.Ordinal), ContractInvalid, "$.apiVersion"),
        new("wrong-kind", ManifestJson.Replace("\"ConfigurationManifest\"", "\"TenantConfigurationPackage\"", StringComparison.Ordinal), ContractInvalid, "$.kind"),
        new("wrong-version", ManifestJson.Replace(ApiVersion, "configuration.islamu.org/v2", StringComparison.Ordinal), ContractInvalid, "$.apiVersion"),
        new("trailing-content", ManifestJson + " false", ContractInvalid, "$"),
        new("string-limit", ManifestJson.Replace("Primary Community", new string('x', MaximumLegalSummaryLength + 1), StringComparison.Ordinal), StringTooLong, "$.spec.tenants[0].spec.displayName"),
        new("wrong-scope", ManifestJson.Replace("\"documents\":{},\"legalDocuments\":{}", "\"documents\":{\"tenant.branding\":{\"schemaVersion\":1,\"payload\":{}}},\"legalDocuments\":{}", StringComparison.Ordinal), ScopeInvalid, "$.spec.instance.documents.tenant.branding")
    ];

    internal static IReadOnlyList<string> SmugglingMembers =>
    [
        "password", "apiKey", "accessToken", "connectionString", "buyerEmail",
        "userId", "tenantId", "targetTenantId", "providerCredentials",
        "connectedAccounts", "deploymentHost", "databaseHost", "jobCheckpoint",
        "reconciliationState", "applicationData"
    ];

    internal static IReadOnlyList<LegalRejectionVector> LegalRejections =>
    [
        new("raw-html", "# Policy\n\n<div>unsafe</div>"),
        new("image", "# Policy\n\n![remote](https://example.test/image.png)"),
        new("unsafe-scheme", "# Policy\n\n[unsafe](javascript:alert(1))"),
        new("tracked-link", "# Policy\n\n[tracked](https://example.test/legal?tracking=1)"),
        new("local-resource", "# Policy\n\n[local](https://127.0.0.1/legal)"),
        new("malformed-placeholder", "# Policy\n\n{{accountable_identity}"),
        new("heading-jump", "# Policy\n\n### Skipped")
    ];
}

internal sealed record InvalidArtifactVector(
    string Name,
    string Json,
    string Code,
    string Path);

internal sealed record LegalRejectionVector(string Name, string Markdown);
