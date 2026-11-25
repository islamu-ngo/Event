using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Shared strict JSON parsing, canonicalization, local JSON Pointer resolution, and typed property readers
/// used by OpenAPI and Configuration contract differs.
/// </summary>
internal static class JsonElementCanonicalizer
{
    private const int MaxJsonDepth = 64;
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        MaxDepth = MaxJsonDepth,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    public static bool TryParseObject(byte[] bytes, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            JsonDocument parsed = JsonDocument.Parse(bytes, ParseOptions);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                document = null;
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    public static bool TryResolveLocalRef(JsonElement root, string reference, out JsonElement resolved)
    {
        resolved = default;
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        JsonElement current = root;
        foreach (string rawSegment in reference[2..].Split('/'))
        {
            string segment = rawSegment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out JsonElement next))
            {
                return false;
            }

            current = next;
        }

        resolved = current;
        return true;
    }

    public static bool TryGetNonEmptyString(JsonElement node, string property, [NotNullWhen(true)] out string? value)
    {
        if (node.ValueKind == JsonValueKind.Object &&
            node.TryGetProperty(property, out JsonElement prop) &&
            prop.ValueKind == JsonValueKind.String &&
            prop.GetString() is { Length: > 0 } text)
        {
            value = text;
            return true;
        }

        value = null;
        return false;
    }

    public static Dictionary<string, JsonElement> EnumerateObjectMap(JsonElement element)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty prop in element.EnumerateObject())
        {
            result[prop.Name] = prop.Value;
        }

        return result;
    }

    public static Dictionary<string, JsonElement> ReadObjectMap(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(propertyName, out JsonElement child) &&
        child.ValueKind == JsonValueKind.Object
            ? EnumerateObjectMap(child)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public static Dictionary<string, JsonElement> ReadNestedObjectMap(JsonElement parent, string firstKey, string? secondKey)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(firstKey, out JsonElement first) ||
            first.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        return secondKey is null
            ? EnumerateObjectMap(first)
            : ReadObjectMap(first, secondKey);
    }

    public static HashSet<string> ReadStringArray(JsonElement node, string propertyName)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty(propertyName, out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return set;
        }

        foreach (string value in array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(text => text is { Length: > 0 })!)
        {
            set.Add(value);
        }

        return set;
    }

    public static bool AreCanonicalEqual(JsonElement left, JsonElement right) =>
        string.Equals(CanonicalJson(left), CanonicalJson(right), StringComparison.Ordinal);

    public static string CanonicalJson(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(
                ",",
                element.EnumerateObject()
                    .OrderBy(prop => prop.Name, StringComparer.Ordinal)
                    .Select(prop => $"{JsonSerializer.Serialize(prop.Name)}:{CanonicalJson(prop.Value)}")) + "}",
            JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(CanonicalJson)) + "]",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "null",
        };
}
