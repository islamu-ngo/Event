namespace ISLAMU.Wire.Contracts.ConfigurationPortability;

using System.Text;
using System.Text.Json;

public static class ConfigurationPortabilityDiagnosticCodes
{
    public const string ContractInvalid = "configuration_portability_contract_invalid";
    public const string TooLarge = "configuration_portability_too_large";
    public const string DepthExceeded = "configuration_portability_depth_exceeded";
    public const string CountExceeded = "configuration_portability_count_exceeded";
    public const string StringTooLong = "configuration_portability_string_too_long";
    public const string SensitiveMemberForbidden = "configuration_portability_sensitive_member_forbidden";
    public const string ScopeInvalid = "configuration_portability_scope_invalid";
}

public sealed record ConfigurationPortabilityDiagnostic(string Code, string Path);

public sealed class ConfigurationPortabilityContractException : Exception
{
    public ConfigurationPortabilityContractException(string code, string path)
        : base("The configuration portability artifact is invalid.")
    {
        Code = code;
        Path = path;
    }

    public string Code { get; }
    public string Path { get; }

    public override string ToString() => $"{GetType().Name}: {Code} at {Path}";
}

public static class ConfigurationPortabilityJsonCodec
{
    private static readonly HashSet<string> ForbiddenMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "apiKey", "accessToken", "connectionString", "buyerEmail",
        "userId", "tenantId", "targetTenantId", "providerCredentials",
        "connectedAccounts", "deploymentHost", "databaseHost", "jobCheckpoint",
        "reconciliationState", "applicationData"
    };

    public static ConfigurationManifestV1Alpha2 ParseConfigurationManifest(ReadOnlyMemory<byte> artifact) =>
        Parse(artifact, ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2,
            ConfigurationManifestContractMetadata.SchemaId, ConfigurationManifestContractMetadata.Kind,
            static value =>
            {
                if (value.Spec.Tenants.Count > ConfigurationPortabilityContentLimits.MaximumTenantCount)
                    Fail(ConfigurationPortabilityDiagnosticCodes.CountExceeded, "$.spec.tenants");
            });

    public static TenantConfigurationPackageV1Alpha2 ParseTenantConfigurationPackage(ReadOnlyMemory<byte> artifact) =>
        Parse(artifact, ConfigurationPortabilityJsonContext.Default.TenantConfigurationPackageV1Alpha2,
            TenantConfigurationPackageContractMetadata.SchemaId, TenantConfigurationPackageContractMetadata.Kind,
            static _ => { });

    public static byte[] SerializeConfigurationManifest(ConfigurationManifestV1Alpha2 manifest) =>
        Serialize(manifest, ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);

    public static byte[] SerializeTenantConfigurationPackage(TenantConfigurationPackageV1Alpha2 package) =>
        Serialize(package, ConfigurationPortabilityJsonContext.Default.TenantConfigurationPackageV1Alpha2);

    private static T Parse<T>(ReadOnlyMemory<byte> artifact, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        string schema, string kind, Action<T> validate) where T : class
    {
        if (artifact.IsEmpty)
            Fail(ConfigurationPortabilityDiagnosticCodes.ContractInvalid, "$");
        if (artifact.Length > ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes)
            Fail(ConfigurationPortabilityDiagnosticCodes.TooLarge, "$");

        try
        {
            using JsonDocument document = JsonDocument.Parse(artifact, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = ConfigurationPortabilityContentLimits.MaximumJsonDepth
            });
            Inspect(document.RootElement, "$");
            T value = JsonSerializer.Deserialize(artifact.Span, typeInfo) ?? throw new JsonException();
            JsonElement root = document.RootElement;
            RequireString(root, "$schema", "$.$schema", schema);
            RequireString(root, "apiVersion", "$.apiVersion", ConfigurationManifestContractMetadata.ApiVersion);
            RequireString(root, "kind", "$.kind", kind);
            validate(value);
            return value;
        }
        catch (ConfigurationPortabilityContractException) { throw; }
        catch (JsonException exception) when (exception.Message.Contains("maximum configured depth", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.DepthExceeded, "$");
        }
        catch (JsonException exception)
        {
            string path = string.IsNullOrEmpty(exception.Path) ? "$" : exception.Path;
            throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.ContractInvalid, path);
        }
    }

    private static byte[] Serialize<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        if (bytes.Length > ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes)
            Fail(ConfigurationPortabilityDiagnosticCodes.TooLarge, "$");
        return bytes;
    }

    private static void Inspect(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string childPath = path + "." + property.Name;
                if (!names.Add(property.Name))
                    Fail(ConfigurationPortabilityDiagnosticCodes.ContractInvalid, childPath);
                if (ForbiddenMembers.Contains(property.Name))
                    Fail(ConfigurationPortabilityDiagnosticCodes.SensitiveMemberForbidden, childPath);
                if (string.Equals(path, "$.spec.instance.documents", StringComparison.Ordinal)
                    && property.Name.StartsWith("tenant.", StringComparison.Ordinal))
                    Fail(ConfigurationPortabilityDiagnosticCodes.ScopeInvalid, childPath);
                Inspect(property.Value, childPath);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
                Inspect(item, $"{path}[{index++}]");
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            string? value = element.GetString();
            if (value is null)
                return;
            int utf8Bytes = Encoding.UTF8.GetByteCount(value);
            bool tooLong = path.EndsWith(".displayName", StringComparison.Ordinal)
                    && value.Length > LegalMarkdownContentLimits.MaximumSummaryLength
                || path.EndsWith(".title", StringComparison.Ordinal)
                    && value.Length > LegalMarkdownContentLimits.MaximumTitleLength
                || path.EndsWith(".summary", StringComparison.Ordinal)
                    && value.Length > LegalMarkdownContentLimits.MaximumSummaryLength
                || path.EndsWith(".languageTag", StringComparison.Ordinal)
                    && value.Length > LegalMarkdownContentLimits.MaximumLanguageTagLength
                || path.EndsWith(".markdown", StringComparison.Ordinal)
                    && utf8Bytes > LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale;
            if (tooLong)
                Fail(ConfigurationPortabilityDiagnosticCodes.StringTooLong, path);
        }
    }

    private static void RequireString(JsonElement root, string member, string path, string expected)
    {
        if (!root.TryGetProperty(member, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
            Fail(ConfigurationPortabilityDiagnosticCodes.ContractInvalid, path);
    }

    private static void Fail(string code, string path) =>
        throw new ConfigurationPortabilityContractException(code, path);
}
