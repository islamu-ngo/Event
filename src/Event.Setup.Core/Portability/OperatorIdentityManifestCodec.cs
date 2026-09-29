namespace ISLAMU.Event.Setup.Core;

using ISLAMU.Wire.Contracts.ConfigurationPortability;
using ISLAMU.Event.Setup.Core.Composition;
using System.Text;
using System.Text.Json;

public enum OperatorIdentityManifestFormat { Json, Yaml }

public static class OperatorIdentityManifestCodec
{
    public static OperatorIdentityManifest Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > OperatorIdentityManifestJson.MaximumBytes)
            throw new OperatorIdentityManifestException();
        try
        {
            CompositionMap document = SetupCompositionYamlParser.Parse(
                bytes, SetupCompositionLimits.Default, CancellationToken.None);
            return OperatorIdentityManifestJson.Parse(
                SetupCompositionNormalizer.WriteJson(document, CancellationToken.None));
        }
        catch (SetupCompositionException)
        {
            throw new OperatorIdentityManifestException();
        }
    }

    public static byte[] Write(OperatorIdentityManifest manifest,
        OperatorIdentityManifestFormat format = OperatorIdentityManifestFormat.Json)
    {
        byte[] json = OperatorIdentityManifestJson.Serialize(manifest);
        if (format == OperatorIdentityManifestFormat.Json) return json;
        if (format != OperatorIdentityManifestFormat.Yaml) throw new ArgumentOutOfRangeException(nameof(format));
        using JsonDocument document = JsonDocument.Parse(json);
        var yaml = new StringBuilder();
        WriteMapping(document.RootElement, 0);
        return Encoding.UTF8.GetBytes(yaml.ToString());

        void WriteMapping(JsonElement mapping, int indent)
        {
            foreach (JsonProperty property in mapping.EnumerateObject())
            {
                yaml.Append(' ', indent).Append(property.Name).Append(':');
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    yaml.Append('\n');
                    WriteMapping(property.Value, indent + 2);
                }
                else
                {
                    yaml.Append(' ').Append(property.Value.GetRawText()).Append('\n');
                }
            }
        }
    }
}
