namespace ISLAMU.Wire.Contracts.ConfigurationPortability;

using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

[JsonConverter(typeof(OperatorIdentityManifestJsonConverter))]
public sealed record OperatorIdentityManifest
{
    private readonly JsonElement _document;

    public OperatorIdentityManifest(
        string apiVersion, string kind, string settingKey, string contentDigest,
        string revisionHash, JsonElement document)
    {
        ApiVersion = apiVersion;
        Kind = kind;
        SettingKey = settingKey;
        ContentDigest = contentDigest;
        RevisionHash = revisionHash;
        _document = document.Clone();
    }

    public string ApiVersion { get; init; }
    public string Kind { get; init; }
    public string SettingKey { get; init; }
    public string ContentDigest { get; init; }
    public string RevisionHash { get; init; }
    public JsonElement Document => _document;
    public override string ToString() => nameof(OperatorIdentityManifest);
}

public sealed class OperatorIdentityManifestJsonConverter : JsonConverter<OperatorIdentityManifest>
{
    public override OperatorIdentityManifest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return OperatorIdentityManifestJson.Parse(Encoding.UTF8.GetBytes(document.RootElement.GetRawText()));
        }
        catch (OperatorIdentityManifestException)
        {
            throw new JsonException("The operator identity manifest is invalid.");
        }
    }

    public override void Write(Utf8JsonWriter writer, OperatorIdentityManifest value, JsonSerializerOptions options) =>
        writer.WriteRawValue(OperatorIdentityManifestJson.Serialize(value));
}

public sealed class OperatorIdentityManifestException : Exception
{
    public OperatorIdentityManifestException() : base("The operator identity manifest is invalid.") { }
}

public static class OperatorIdentityManifestJson
{
    public const int MaximumBytes = 65_536;
    public const string ApiVersion = "islamu.org/operator-identity/v1";
    public const string Kind = "InstanceOperatorIdentity";
    public const string SettingKey = "instance.operator_identity";

    private static readonly string[] DocumentMembers =
    [
        "isOfficialInstance", "jurisdictionCountryCode", "legalName", "legalNoticeUrl",
        "officialOrigin", "operatorId", "operatorKindCode", "privacyUrl", "publicContactEmail",
        "publicName", "registrationIdentifier", "revision", "termsUrl", "websiteUrl"
    ];

    public static OperatorIdentityManifest Create(JsonElement document)
    {
        byte[] canonical = CanonicalDocument(document);
        return new(ApiVersion, Kind, SettingKey, Digest(canonical),
            RevisionHash(document.GetProperty("revision").GetGuid()), document);
    }

    public static OperatorIdentityManifest Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes) throw new OperatorIdentityManifestException();
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = parsed.RootElement;
            RequireMembers(root, ["apiVersion", "kind", "settingKey", "contentDigest", "revisionHash", "document"]);
            var manifest = new OperatorIdentityManifest(
                RequiredString(root, "apiVersion"), RequiredString(root, "kind"),
                RequiredString(root, "settingKey"), RequiredString(root, "contentDigest"),
                RequiredString(root, "revisionHash"), root.GetProperty("document"));
            Validate(manifest);
            return manifest;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new OperatorIdentityManifestException();
        }
    }

    public static byte[] Serialize(OperatorIdentityManifest manifest)
    {
        Validate(manifest);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("apiVersion", manifest.ApiVersion);
            writer.WriteString("kind", manifest.Kind);
            writer.WriteString("settingKey", manifest.SettingKey);
            writer.WriteString("contentDigest", manifest.ContentDigest);
            writer.WriteString("revisionHash", manifest.RevisionHash);
            writer.WritePropertyName("document");
            writer.WriteRawValue(CanonicalDocument(manifest.Document));
            writer.WriteEndObject();
        }
        if (buffer.Length > MaximumBytes) throw new OperatorIdentityManifestException();
        return buffer.ToArray();
    }

    public static bool Validate(OperatorIdentityManifest manifest)
    {
        if (manifest is null || manifest.ApiVersion != ApiVersion || manifest.Kind != Kind
            || manifest.SettingKey != SettingKey)
            throw new OperatorIdentityManifestException();
        OperatorIdentityManifest canonical = Create(manifest.Document);
        if (!string.Equals(manifest.ContentDigest, canonical.ContentDigest, StringComparison.Ordinal)
            || !string.Equals(manifest.RevisionHash, canonical.RevisionHash, StringComparison.Ordinal))
            throw new OperatorIdentityManifestException();
        return true;
    }

    /// <summary>
    /// Hash of the target's server-owned revision. Null is the explicit absent-document
    /// precondition, not permission to overwrite an existing document.
    /// </summary>
    public static string RevisionHash(Guid? revision) =>
        Digest(Encoding.UTF8.GetBytes(revision?.ToString("D") ?? "absent"));

    private static byte[] CanonicalDocument(JsonElement document)
    {
        RequireMembers(document, DocumentMembers);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (string name in DocumentMembers)
            {
                JsonElement value = document.GetProperty(name);
                if (name == "isOfficialInstance")
                {
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new OperatorIdentityManifestException();
                }
                else if (name is "operatorId" or "revision")
                {
                    if (value.ValueKind != JsonValueKind.String || !value.TryGetGuid(out Guid id)
                        || id.Version != 7)
                        throw new OperatorIdentityManifestException();
                    writer.WriteString(name, id);
                    continue;
                }
                else if (value.ValueKind != JsonValueKind.Null
                    && (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 2048))
                    throw new OperatorIdentityManifestException();
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        if (buffer.Length > MaximumBytes) throw new OperatorIdentityManifestException();
        return buffer.ToArray();
    }

    private static void RequireMembers(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new OperatorIdentityManifestException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new OperatorIdentityManifestException();
        }
        if (seen.Count != expected.Length) throw new OperatorIdentityManifestException();
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.String
            ? element.GetProperty(name).GetString()!
            : throw new OperatorIdentityManifestException();

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
