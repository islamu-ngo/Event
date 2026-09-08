namespace ISLAMU.Wire.Contracts.SetupLive;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    AllowTrailingCommas = false,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = false)]
[JsonSerializable(typeof(CreateSetupTargetEnrollmentRequest))]
[JsonSerializable(typeof(SetupTargetEnrollmentData))]
[JsonSerializable(typeof(SetupSecretBindingReadinessItem))]
[JsonSerializable(typeof(SetupSecretBindingOperationData))]
public sealed partial class SetupLiveJsonContext : JsonSerializerContext;
