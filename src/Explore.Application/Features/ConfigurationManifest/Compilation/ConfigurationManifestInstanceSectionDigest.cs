namespace Explore.Application.Features.ConfigurationManifest.Compilation;

using System.Security.Cryptography;
using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

internal static class ConfigurationManifestInstanceSectionDigest
{
    public static string Compute(
        ConfigurationManifestInstanceV1Alpha2 instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("settings");
            writer.WriteStartObject();
            foreach ((string key, JsonElement value) in instance.Settings
                         .OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                WriteCanonicalJson(writer, value);
            }

            writer.WriteEndObject();
            writer.WritePropertyName("documents");
            writer.WriteStartObject();
            foreach ((
                         string key,
                         ConfigurationManifestDocumentV1Alpha2 document)
                     in instance.Documents
                         .OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                writer.WriteStartObject();
                writer.WriteNumber(
                    "schemaVersion",
                    document.SchemaVersion);
                writer.WritePropertyName("payload");
                WriteCanonicalJson(writer, document.Payload);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonicalJson(
        Utf8JsonWriter writer,
        JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            JsonProperty[] properties = element.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray();
            if (properties.Select(property => property.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != properties.Length)
            {
                throw new InvalidOperationException(
                    "The instance section contains duplicate JSON properties.");
            }

            writer.WriteStartObject();
            foreach (JsonProperty property in properties)
            {
                writer.WritePropertyName(property.Name);
                WriteCanonicalJson(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray())
            {
                WriteCanonicalJson(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }
}
