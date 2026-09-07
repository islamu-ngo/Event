namespace ISLAMU.Setup.Core.EnvironmentTests;

using System.Text;

internal static class EnvironmentContractExpectedVectors
{
    internal const string ProductNamespace = "ISLAMU.Event.Setup.Core.Environment";
    internal const string MachineCatalogueRelativePath =
        "eng/setup-assistant/generated/environment-catalogue.json";

    internal static readonly string[] RequiredCatalogueProductTypes =
    [
        "EnvironmentVariableDefinition",
        "EnvironmentVariableCategory",
        "EnvironmentVariableSensitivity",
        "EnvironmentVariableRequirement",
        "EnvironmentActivationExpression",
        "EnvironmentActivationContext",
        "EnvironmentCatalogue",
        "EnvironmentCatalogueResult",
        "EnvironmentDiagnostic",
        "EnvironmentGenerationPolicy",
        "EnvironmentGenerationSurface",
        "EnvironmentRestartBehavior",
        "EnvironmentDocumentationMetadata",
        "CanonicalEnvironmentCatalogue",
    ];

    internal static readonly string[] RequiredDotenvProductTypes =
    [
        "DotenvDocument",
        "DotenvEntry",
        "DotenvEntryKind",
        "DotenvProvenance",
        "DotenvCodec",
        "DotenvParseResult",
        "DotenvRenderResult",
        "DotenvReadiness",
        "DotenvReadinessResult",
    ];

    internal static readonly string[] DefinitionProperties =
    [
        "Activation", "Category", "Documentation", "Generation", "HelpKey", "Key",
        "LocalizationKey", "Order", "Requirement", "RestartBehavior", "SafeDefault",
        "Sensitivity", "ValidatorId",
    ];

    internal static readonly string[] EntryProperties =
    [
        "IsSecret", "Key", "Kind", "Provenance", "Value",
    ];

    internal static readonly string[] DiagnosticProperties =
    [
        "Category", "Code", "Key", "Path",
    ];

    internal static readonly string[] RequiredExpressionFactories =
    [
        "All", "Any", "Capability", "Feature", "Not", "Provider", "Topology",
    ];

    internal static readonly string[] RequiredCatalogueMethods =
    [
        "Create", "Lookup", "Relevant",
    ];

    internal static readonly string[] RequiredDotenvCodecMethods =
    [
        "Parse", "Render",
    ];

    internal static readonly string[] SentinelKeys =
    [
        "API_HTTP_PORT", "DATABASE_PROVIDER", "SECRET_PROVIDER",
        "STRIPE_PLATFORM_SECRET_KEY", "DATABASE_ERASURE_MIGRATOR_PASSWORD",
    ];

    internal static readonly ActivationGraphFixture ValidActivationGraph = new(
        Topologies: new HashSet<string>(["combined", "split"], StringComparer.Ordinal),
        Capabilities: new HashSet<string>(["database", "mail"], StringComparer.Ordinal),
        Providers: new HashSet<string>(["postgresql", "smtp"], StringComparer.Ordinal),
        Features: new Dictionary<string, ActivationNode>(StringComparer.Ordinal)
        {
            ["mail-delivery"] = ActivationNode.All(
                ActivationNode.Capability("mail"), ActivationNode.Provider("smtp")),
            ["split-database"] = ActivationNode.All(
                ActivationNode.Topology("split"), ActivationNode.Capability("database")),
        });

    internal static readonly IReadOnlyList<InvalidActivationFixture> InvalidActivationGraphs =
    [
        new("cycle", ValidActivationGraph with
        {
            Features = new Dictionary<string, ActivationNode>(StringComparer.Ordinal)
            {
                ["first"] = ActivationNode.Feature("second"),
                ["second"] = ActivationNode.Feature("first"),
            },
        }, "activation-cycle"),
        new("self-reference", ValidActivationGraph with
        {
            Features = new Dictionary<string, ActivationNode>(StringComparer.Ordinal)
            {
                ["self"] = ActivationNode.Feature("self"),
            },
        }, "activation-self-reference"),
        new("unknown-capability", ValidActivationGraph with
        {
            Features = new Dictionary<string, ActivationNode>(StringComparer.Ordinal)
            {
                ["bad"] = ActivationNode.Capability("absent"),
            },
        }, "activation-unknown-capability"),
        new("unknown-provider", ValidActivationGraph with
        {
            Features = new Dictionary<string, ActivationNode>(StringComparer.Ordinal)
            {
                ["bad"] = ActivationNode.Provider("absent"),
            },
        }, "activation-unknown-provider"),
        new("unknown-topology", ValidActivationGraph with
        {
            Features = new Dictionary<string, ActivationNode>(StringComparer.Ordinal)
            {
                ["bad"] = ActivationNode.Topology("absent"),
            },
        }, "activation-unknown-topology"),
    ];

    internal static readonly IReadOnlyList<CatalogueDefinitionFixture> ValidDefinitions =
    [
        new("DATABASE_PASSWORD", "database", "secret", "required", null, 10,
            ActivationNode.Capability("database")),
        new("MAIL_FROM_NAME", "mail", "public", "optional", null, 20,
            ActivationNode.Feature("mail-delivery")),
        new("MAIL_PORT", "mail", "public", "defaulted", "587", 30,
            ActivationNode.Feature("mail-delivery")),
    ];

    internal static readonly IReadOnlyList<InvalidCatalogueFixture> InvalidCatalogues =
    [
        new("duplicate", [ValidDefinitions[0], ValidDefinitions[0]], "catalogue-duplicate-key"),
        new("case-collision",
            [ValidDefinitions[0], ValidDefinitions[0] with { Key = "database_password", Order = 11 }],
            "catalogue-key-case-collision"),
        new("noncanonical",
            [ValidDefinitions[0] with { Key = "Database_Password" }],
            "catalogue-key-noncanonical"),
        new("duplicate-order",
            [ValidDefinitions[0], ValidDefinitions[1] with { Order = 10 }],
            "catalogue-order-duplicate"),
        new("secret-default",
            [ValidDefinitions[0] with { SafeDefault = "not-allowed", Requirement = "defaulted" }],
            "catalogue-secret-default"),
        new("sensitive-default",
            [ValidDefinitions[0] with { Sensitivity = "sensitive", SafeDefault = "not-allowed", Requirement = "defaulted" }],
            "catalogue-sensitive-default"),
    ];

    internal static readonly byte[] CanonicalDotenv = Encoding.UTF8.GetBytes(
        "DATABASE_PASSWORD=\nMAIL_FROM_NAME=\nMAIL_PORT=587\n");

    internal static readonly IReadOnlyList<DotenvRejectionFixture> DotenvRejections =
    [
        new("duplicate", "SAFE_KEY=\nSAFE_KEY=\n", "dotenv-duplicate-key"),
        new("case-collision", "SAFE_KEY=\nsafe_key=\n", "dotenv-key-case-collision"),
        new("missing-equals", "SAFE_KEY\n", "dotenv-equals-missing"),
        new("inline-comment", "SAFE_KEY=value # comment\n", "dotenv-trailing-syntax"),
        new("unmatched-quote", "SAFE_KEY=\"value\n", "dotenv-quote-invalid"),
        new("single-quote", "SAFE_KEY='value'\n", "dotenv-quote-invalid"),
        new("unknown-escape", "SAFE_KEY=\"value\\n\"\n", "dotenv-escape-invalid"),
        new("export", "export SAFE_KEY=\n", "dotenv-export-forbidden"),
        new("command-substitution", "SAFE_KEY=$(unsafe)\n", "dotenv-expansion-forbidden"),
        new("backticks", "SAFE_KEY=`unsafe`\n", "dotenv-expansion-forbidden"),
        new("braced-expansion", "SAFE_KEY=${UNSAFE}\n", "dotenv-expansion-forbidden"),
        new("dollar-expansion", "SAFE_KEY=$UNSAFE\n", "dotenv-expansion-forbidden"),
        new("multiline-quote", "SAFE_KEY=\"first\nsecond\"\n", "dotenv-multiline-forbidden"),
        new("injection-semicolon", "SAFE_KEY=value;command\n", "dotenv-trailing-syntax"),
        new("injection-pipe", "SAFE_KEY=value|command\n", "dotenv-trailing-syntax"),
        new("injection-ampersand", "SAFE_KEY=value&command\n", "dotenv-trailing-syntax"),
        new("injection-redirect", "SAFE_KEY=value>file\n", "dotenv-trailing-syntax"),
        new("malformed-name", "bad-key=\n", "dotenv-key-invalid"),
        new("leading-space", " SAFE_KEY=\n", "dotenv-whitespace-forbidden"),
        new("space-before-equals", "SAFE_KEY =\n", "dotenv-whitespace-forbidden"),
        new("trailing-garbage", "SAFE_KEY=\"safe\"garbage\n", "dotenv-trailing-syntax"),
        new("bom", "\uFEFFSAFE_KEY=\n", "dotenv-bom-forbidden"),
        new("carriage-return", "SAFE_KEY=\r\n", "dotenv-carriage-return-forbidden"),
        new("nul", "SAFE_KEY=\0\n", "dotenv-control-character"),
        new("control", "SAFE_KEY=\u0001\n", "dotenv-control-character"),
    ];

    internal const int MaximumDotenvFileUtf8Bytes = 1_048_576;
    internal const int MaximumDotenvLineUtf8Bytes = 16_384;
    internal const int MaximumDotenvKeyCharacters = 128;
    internal const int MaximumDotenvValueUtf8Bytes = 8_192;
    internal const int MaximumDotenvEntryCount = 2_048;
}

internal sealed record ActivationGraphFixture(
    IReadOnlySet<string> Topologies,
    IReadOnlySet<string> Capabilities,
    IReadOnlySet<string> Providers,
    IReadOnlyDictionary<string, ActivationNode> Features);

internal sealed record InvalidActivationFixture(string Name, ActivationGraphFixture Graph, string ExpectedCode);

internal sealed record ActivationNode(string Kind, string? Identifier, IReadOnlyList<ActivationNode> Operands)
{
    internal static ActivationNode Topology(string value) => new("topology", value, []);
    internal static ActivationNode Capability(string value) => new("capability", value, []);
    internal static ActivationNode Provider(string value) => new("provider", value, []);
    internal static ActivationNode Feature(string value) => new("feature", value, []);
    internal static ActivationNode All(params ActivationNode[] values) => new("all", null, values);
    internal static ActivationNode Any(params ActivationNode[] values) => new("any", null, values);
    internal static ActivationNode Not(ActivationNode value) => new("not", null, [value]);
}

internal sealed record CatalogueDefinitionFixture(
    string Key,
    string Category,
    string Sensitivity,
    string Requirement,
    string? SafeDefault,
    int Order,
    ActivationNode Activation);

internal sealed record InvalidCatalogueFixture(
    string Name,
    IReadOnlyList<CatalogueDefinitionFixture> Definitions,
    string ExpectedCode);

internal sealed record DotenvRejectionFixture(string Name, string Text, string ExpectedCode);

internal sealed record EnvironmentDiagnosticFixture(
    string Code,
    string Path,
    string? Key,
    string Category,
    string? SuppliedValue = null,
    string? Message = null);
