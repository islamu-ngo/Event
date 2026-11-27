namespace ISLAMU.Wire.Contracts.ConfigurationPortability;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    AllowTrailingCommas = false,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(ConfigurationManifestSpecV1Alpha2JsonConverter)],
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = false)]
[JsonSerializable(typeof(ConfigurationManifestV1Alpha2))]
[JsonSerializable(typeof(ConfigurationManifestInstanceV1Alpha2))]
[JsonSerializable(typeof(IReadOnlyList<ConfigurationManifestTenantV1Alpha2>))]
[JsonSerializable(typeof(TenantConfigurationPackageV1Alpha2))]
[JsonSerializable(typeof(ConfigurationManifestLegalDocumentV1Alpha2))]
[JsonSerializable(typeof(ConfigurationManifestPaidEventPolicyPayloadV1Alpha2))]
[JsonSerializable(typeof(ConfigurationModerationDocumentV1))]
public sealed partial class ConfigurationPortabilityJsonContext : JsonSerializerContext;
