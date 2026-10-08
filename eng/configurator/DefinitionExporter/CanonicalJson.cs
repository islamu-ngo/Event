namespace ISLAMU.Event.DefinitionExporter;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class CanonicalJson
{
    internal static byte[] Bytes(JsonNode value) => Bytes(JsonSerializer.SerializeToElement(value));

    internal static byte[] Bytes(JsonElement value)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
            Write(writer, value, new Budget(), 1);
        if (output.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("canonical-byte-limit");
        return output.ToArray();
    }

    internal static string Digest(ReadOnlySpan<byte> value) => Convert.ToHexStringLower(SHA256.HashData(value));

    private static void Write(Utf8JsonWriter writer, JsonElement value, Budget budget, int depth)
    {
        if (depth > 32 || ++budget.Nodes > 65_536)
            throw new InvalidDataException("canonical-node-limit");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!seen.Add(property.Name.Normalize(NormalizationForm.FormC)) || seen.Count > 4096)
                        throw new InvalidDataException("duplicate-member");
                    budget.Scalar(property.Name);
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, budget, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                if (value.GetArrayLength() > 4096)
                    throw new InvalidDataException("canonical-array-limit");
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                    Write(writer, item, budget, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                budget.Scalar(value.GetString()!);
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                string raw = value.GetRawText();
                string[] components = raw.Split(['e', 'E']);
                string mantissa = components[0];
                string significant = mantissa.Replace(".", "", StringComparison.Ordinal)
                    .TrimStart('-').TrimStart('0').TrimEnd('0');
                int exponent = components.Length == 1 ? 0
                    : int.Parse(components[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                int point = mantissa.IndexOf('.', StringComparison.Ordinal);
                int scale = point < 0 ? 0 : mantissa.TrimEnd('0').Length - point - 1;
                if (significant.Length > 28 || exponent is < -28 or > 28
                    || scale - exponent > 28 && significant.Length > 0
                    || !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number))
                    throw new InvalidDataException("number-range");
                writer.WriteRawValue(number == 0 ? "0" : number.ToString("G29", CultureInfo.InvariantCulture));
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private sealed class Budget
    {
        internal int Nodes;
        private int _characters;

        internal void Scalar(string value)
        {
            _ = new UTF8Encoding(false, true).GetByteCount(value);
            _characters = checked(_characters + value.Length);
            if (value.Length > 65_536 || _characters > 1024 * 1024)
                throw new InvalidDataException("canonical-scalar-limit");
        }
    }
}
