namespace ISLAMU.Event.SetupAssistant.Browser;

using System.Text;
using System.Text.Json;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;

public sealed record BrowserManifestResult(
    bool IsAccepted,
    string Status,
    string? Preview,
    string? DownloadHref)
{
    public static BrowserManifestResult Rejected { get; } = new(
        false,
        "Manifest rejected. Restricted and identity artifacts require an offline native Setup Assistant.",
        null,
        null);
}

public static class BrowserPublicManifest
{
    public const int MaximumBytes = 64 * 1024;
    public const string Schema = "event-setup-public-manifest/v1";
    public const string Kind = "public-template";

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "schema", "kind", "name", "topology", "capabilities", "providers"
    };

    public static BrowserManifestResult ValidateAndPreview(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
            return BrowserManifestResult.Rejected;

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !HasClosedPropertySet(root)
                || !TryReadString(root, "schema", out string? schema)
                || !TryReadString(root, "kind", out string? kind)
                || !TryReadString(root, "name", out string? name)
                || !TryReadString(root, "topology", out string? topology)
                || !string.Equals(schema, Schema, StringComparison.Ordinal)
                || !string.Equals(kind, Kind, StringComparison.Ordinal)
                || !IsSafeName(name)
                || !PlatformEnvironmentCatalogue.Catalogue.Topologies.Contains(
                    topology, StringComparer.Ordinal)
                || !TryReadSelection(
                    root, "capabilities", PlatformEnvironmentCatalogue.Catalogue.Capabilities,
                    out string[] capabilities)
                || !TryReadSelection(
                    root, "providers", PlatformEnvironmentCatalogue.Catalogue.Providers,
                    out string[] providers))
                return BrowserManifestResult.Rejected;

            SetupArtifactKind artifactKind = string.Equals(kind, Kind, StringComparison.Ordinal)
                ? SetupArtifactKind.PublicTemplate
                : SetupArtifactKind.Unknown;
            byte[] canonical = Serialize(name, topology, capabilities, providers);
            if (!SetupArtifactPolicy.TryProjectPublic(
                    artifactKind, canonical, out ReadOnlyMemory<byte> publicBytes))
                return BrowserManifestResult.Rejected;

            string preview = Encoding.UTF8.GetString(publicBytes.Span);
            return new BrowserManifestResult(
                true,
                "Public manifest validated locally and is ready to download.",
                preview,
                $"data:application/json;base64,{Convert.ToBase64String(publicBytes.Span)}");
        }
        catch (JsonException)
        {
            return BrowserManifestResult.Rejected;
        }
    }

    private static bool HasClosedPropertySet(JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return root.EnumerateObject().All(property => AllowedProperties.Contains(property.Name) && seen.Add(property.Name))
            && seen.SetEquals(AllowedProperties);
    }

    private static bool TryReadString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadSelection(
        JsonElement root,
        string propertyName,
        IReadOnlyList<string> allowed,
        out string[] values)
    {
        values = [];
        if (!root.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Array)
            return false;

        var supplied = new List<string>();
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
            string value = item.GetString() ?? string.Empty;
            if (!allowed.Contains(value, StringComparer.Ordinal))
                return false;
            supplied.Add(value);
        }

        values = supplied.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        return values.Length == supplied.Count && values.Length <= allowed.Count;
    }

    private static bool IsSafeName(string value) =>
        value.Length is > 0 and <= 64
        && value[0] is >= 'a' and <= 'z'
        && value.All(character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9' or '-');

    private static byte[] Serialize(
        string name,
        string topology,
        IReadOnlyList<string> capabilities,
        IReadOnlyList<string> providers)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("kind", Kind);
            writer.WriteString("name", name);
            writer.WriteString("topology", topology);
            writer.WriteStartArray("capabilities");
            foreach (string capability in capabilities)
                writer.WriteStringValue(capability);
            writer.WriteEndArray();
            writer.WriteStartArray("providers");
            foreach (string provider in providers)
                writer.WriteStringValue(provider);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return output.ToArray();
    }
}
