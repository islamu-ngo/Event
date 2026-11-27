namespace ISLAMU.Wire.Contracts.ConfigurationPortability;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

internal sealed class ConfigurationManifestSpecV1Alpha2JsonConverter
    : JsonConverter<ConfigurationManifestSpecV1Alpha2>
{
    public ConfigurationManifestSpecV1Alpha2JsonConverter() { }

    public override ConfigurationManifestSpecV1Alpha2 Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("The configuration spec must be an object.");

        ConfigurationManifestInstanceV1Alpha2? instance = null;
        IReadOnlyList<ConfigurationManifestTenantV1Alpha2> tenants = [];
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "instance":
                    instance = property.Value.Deserialize(
                        (JsonTypeInfo<ConfigurationManifestInstanceV1Alpha2>)options.GetTypeInfo(
                            typeof(ConfigurationManifestInstanceV1Alpha2)));
                    break;
                case "tenants":
                    if (property.Value.ValueKind != JsonValueKind.Array)
                        throw new JsonException("The tenants declaration must be an array.");
                    tenants = property.Value.Deserialize(
                        (JsonTypeInfo<IReadOnlyList<ConfigurationManifestTenantV1Alpha2>>)options.GetTypeInfo(
                            typeof(IReadOnlyList<ConfigurationManifestTenantV1Alpha2>)))
                        ?? throw new JsonException("The tenants declaration must be an array.");
                    if (tenants.Any(tenant => tenant is null))
                        throw new JsonException("Tenant declarations must not contain null entries.");
                    break;
                default:
                    throw new JsonException("The configuration spec contains an unknown member.");
            }
        }

        return new ConfigurationManifestSpecV1Alpha2
        {
            Instance = instance ?? throw new JsonException("The configuration spec requires an instance."),
            Tenants = tenants
        };
    }

    public override void Write(
        Utf8JsonWriter writer, ConfigurationManifestSpecV1Alpha2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("instance");
        JsonSerializer.Serialize(writer, value.Instance,
            (JsonTypeInfo<ConfigurationManifestInstanceV1Alpha2>)options.GetTypeInfo(
                typeof(ConfigurationManifestInstanceV1Alpha2)));
        writer.WritePropertyName("tenants");
        JsonSerializer.Serialize(writer, value.Tenants,
            (JsonTypeInfo<IReadOnlyList<ConfigurationManifestTenantV1Alpha2>>)options.GetTypeInfo(
                typeof(IReadOnlyList<ConfigurationManifestTenantV1Alpha2>)));
        writer.WriteEndObject();
    }
}
