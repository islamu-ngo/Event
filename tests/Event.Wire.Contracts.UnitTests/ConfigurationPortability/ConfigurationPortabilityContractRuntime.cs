namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Reflection;
using ISLAMU.Wire.Contracts.Admissions;

internal sealed class ConfigurationPortabilityContractRuntime
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private readonly Assembly _wireAssembly = typeof(AdmissionQrPayloadCodec).Assembly;

    internal static readonly string[] RequiredRecordNames =
    [
        "ConfigurationManifestV1Alpha2",
        "ConfigurationManifestMetadataV1Alpha2",
        "ConfigurationManifestExportMetadataV1Alpha2",
        "ConfigurationManifestSpecV1Alpha2",
        "ConfigurationManifestInstanceV1Alpha2",
        "ConfigurationManifestTenantV1Alpha2",
        "ConfigurationManifestTenantMetadataV1Alpha2",
        "ConfigurationManifestTenantSpecV1Alpha2",
        "ConfigurationManifestDocumentV1Alpha2",
        "ConfigurationManifestBrandingPayloadV1Alpha2",
        "ConfigurationManifestPaidEventPolicyPayloadV1Alpha2",
        "ConfigurationManifestPaidEventPolicyCurrencyRiskLimitV1Alpha2",
        "TenantConfigurationPackageV1Alpha2",
        "TenantConfigurationPackageMetadataV1Alpha2",
        "TenantConfigurationPackageSourceV1Alpha2",
        "TenantConfigurationPackageSpecV1Alpha2",
        "ConfigurationManifestLegalDocumentV1Alpha2",
        "ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2",
        "ConfigurationManifestLegalTemplateProvenanceV1Alpha2"
    ];

    internal static readonly string[] RequiredOwnerNames =
    [
        .. RequiredRecordNames,
        "ConfigurationManifestContractMetadata",
        "TenantConfigurationPackageContractMetadata",
        "ConfigurationImportApplyMode",
        "ConfigurationPortabilityContentLimits",
        "ConfigurationPortabilityDiagnostic",
        "ConfigurationPortabilityContractException",
        "ConfigurationPortabilityJsonCodec",
        "LegalMarkdownContentLimits",
        "LegalMarkdownDiagnosticCodes",
        "LegalMarkdownDiagnostic",
        "LegalMarkdownInspection",
        "LegalMarkdownRenderResult",
        "LegalMarkdownCodec"
    ];

    internal bool IsComplete => MissingOwners().Count == 0;

    internal IReadOnlyList<string> MissingOwners()
    {
        var missing = RequiredOwnerNames
            .Where(name => Type(name) is null)
            .Select(name => $"{ConfigurationPortabilityExpectedVectors.Namespace}.{name}")
            .ToList();

        Type? codec = Type("ConfigurationPortabilityJsonCodec");
        if (codec is not null)
        {
            RequireMethodName(codec, "ParseConfigurationManifest", missing);
            RequireMethodName(codec, "ParseTenantConfigurationPackage", missing);
            RequireMethodName(codec, "SerializeConfigurationManifest", missing);
            RequireMethodName(codec, "SerializeTenantConfigurationPackage", missing);
        }

        Type? legal = Type("LegalMarkdownCodec");
        if (legal is not null)
        {
            RequireMethodName(legal, "Normalize", missing);
            RequireMethodName(legal, "Inspect", missing);
            RequireMethodName(legal, "Render", missing);
        }

        return missing.Order(StringComparer.Ordinal).Take(40).ToArray();
    }

    internal Type RequireType(string shortName) =>
        Type(shortName)
        ?? throw new InvalidOperationException($"Missing final owner '{ConfigurationPortabilityExpectedVectors.Namespace}.{shortName}'.");

    internal object ParseManifest(byte[] bytes) =>
        InvokeCodec("ParseConfigurationManifest", new ReadOnlyMemory<byte>(bytes));

    internal object ParsePackage(byte[] bytes) =>
        InvokeCodec("ParseTenantConfigurationPackage", new ReadOnlyMemory<byte>(bytes));

    internal byte[] SerializeManifest(object manifest) =>
        (byte[])InvokeCodec("SerializeConfigurationManifest", manifest);

    internal byte[] SerializePackage(object package) =>
        (byte[])InvokeCodec("SerializeTenantConfigurationPackage", package);

    internal Exception ParseManifestFailure(byte[] bytes)
    {
        try
        {
            _ = ParseManifest(bytes);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return exception.InnerException;
        }

        throw new InvalidOperationException("Invalid manifest vector was accepted.");
    }

    internal string NormalizeLegalMarkdown(string markdown) =>
        (string)InvokeLegal("Normalize", markdown);

    internal object InspectLegalMarkdown(string markdown) =>
        InvokeLegal("Inspect", markdown);

    internal object RenderLegalMarkdown(
        string markdown,
        IReadOnlyDictionary<string, string> identities) =>
        InvokeLegal("Render", markdown, identities);

    internal Exception InspectLegalFailure(string markdown)
    {
        try
        {
            _ = InspectLegalMarkdown(markdown);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return exception.InnerException;
        }

        throw new InvalidOperationException("Invalid legal Markdown vector was accepted.");
    }

    internal static object? Property(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, PublicInstance)
            ?? throw new InvalidOperationException($"Missing public member '{target.GetType().FullName}.{name}'.");
        return property.GetValue(target);
    }

    internal static string StringProperty(object target, string name) =>
        Property(target, name) as string
        ?? throw new InvalidOperationException($"Public member '{target.GetType().FullName}.{name}' is not a string.");

    internal static int IntConstant(Type type, string name) =>
        type.GetField(name, PublicStatic)?.GetRawConstantValue() as int?
        ?? throw new InvalidOperationException($"Missing public limit '{type.FullName}.{name}'.");

    internal static string StringConstant(Type type, string name) =>
        type.GetField(name, PublicStatic)?.GetRawConstantValue() as string
        ?? throw new InvalidOperationException($"Missing public constant '{type.FullName}.{name}'.");

    private object InvokeCodec(string name, object argument) =>
        InvokeSingleArgument(RequireType("ConfigurationPortabilityJsonCodec"), name, argument);

    private object InvokeLegal(string name, params object[] arguments)
    {
        MethodInfo method = RequireType("LegalMarkdownCodec")
            .GetMethods(PublicStatic)
            .SingleOrDefault(candidate =>
                candidate.Name == name && candidate.GetParameters().Length == arguments.Length)
            ?? throw new InvalidOperationException($"Missing public legal codec method '{name}'.");
        return method.Invoke(null, arguments)
            ?? throw new InvalidOperationException($"Legal codec method '{name}' returned null.");
    }

    private static object InvokeSingleArgument(Type owner, string name, object argument)
    {
        MethodInfo method = owner.GetMethods(PublicStatic)
            .SingleOrDefault(candidate =>
                candidate.Name == name && candidate.GetParameters().Length == 1)
            ?? throw new InvalidOperationException($"Missing public codec method '{owner.FullName}.{name}'.");
        return method.Invoke(null, [argument])
            ?? throw new InvalidOperationException($"Codec method '{owner.FullName}.{name}' returned null.");
    }

    private Type? Type(string shortName) =>
        _wireAssembly.GetType($"{ConfigurationPortabilityExpectedVectors.Namespace}.{shortName}", throwOnError: false);

    private static void RequireMethodName(
        Type owner,
        string methodName,
        List<string> missing)
    {
        if (!owner.GetMethods(PublicStatic).Any(method => method.Name == methodName))
            missing.Add($"{owner.FullName}.{methodName}");
    }
}
