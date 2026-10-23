using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;

namespace Explore.ServiceDefaults.DataProtection;

public sealed record ExternalDataProtectionRing(int Version, string Partition, long Generation, string[] Documents);

/// <summary>
/// Independently provisioned complete inventory, not an assertion accepted from the candidate ring.
/// A revoked key remains in this inventory and in the ring; expiration never removes an entry.
/// </summary>
public sealed record ExternalDataProtectionAdmission(
    int Version, string Partition, long Generation, string RingSha256, Guid ActiveKeyId,
    DateTimeOffset NotBeforeUtc, DateTimeOffset NotAfterUtc, ExternalDataProtectionKeyInventory[] Keys);

public sealed record ExternalDataProtectionKeyInventory(Guid Id, string Sha256, bool Revoked);

public sealed record ExternalDataProtectionInput(
    string Ring, string Admission, string AdmissionSha256, long MinimumGeneration)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal static class ExternalDataProtectionRingAdmission
{
    internal const int MaximumBytes = 32 * 1024;
    internal const int MaximumDocuments = 16;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    internal static (ExternalDataProtectionAdmission Admission, XElement[] Documents) Validate(
        ExternalDataProtectionInput input, string partition, DateTimeOffset now)
    {
        var ring = Parse<ExternalDataProtectionRing>(input.Ring);
        var admission = Parse<ExternalDataProtectionAdmission>(input.Admission);
        if (input.MinimumGeneration < 1
            || !string.Equals(ExternalDataProtectionInput.Hash(input.Admission), input.AdmissionSha256, StringComparison.Ordinal)
            || ring.Version != 1 || admission.Version != 1
            || ring.Partition != partition || admission.Partition != partition
            || ring.Generation < input.MinimumGeneration || admission.Generation != ring.Generation
            || admission.RingSha256 != ExternalDataProtectionInput.Hash(input.Ring)
            || admission.NotBeforeUtc > now || admission.NotAfterUtc <= now
            || admission.NotAfterUtc <= admission.NotBeforeUtc
            || admission.NotAfterUtc - admission.NotBeforeUtc > TimeSpan.FromDays(1)
            || ring.Documents is not { Length: > 0 and <= MaximumDocuments }
            || admission.Keys is not { Length: > 0 and <= MaximumDocuments })
            throw Invalid();

        var documents = ring.Documents.Select(ParseXml).ToArray();
        var keys = new Dictionary<Guid, XElement>();
        var revocations = new HashSet<Guid>();
        foreach (var document in documents)
        {
            if (document.Name == "key")
            {
                if (!Guid.TryParse(document.Attribute("id")?.Value, out var id) || id == Guid.Empty
                    || document.Attribute("version")?.Value != "1" || !keys.TryAdd(id, document))
                    throw Invalid();
                ValidateDescriptor(document);
            }
            else if (document.Name == "revocation")
            {
                if (document.Attribute("version")?.Value != "1"
                    || !Guid.TryParse(document.Element("key")?.Attribute("id")?.Value, out var id)
                    || id == Guid.Empty || !revocations.Add(id)
                    || ReadDate(document, "revocationDate") > now
                    || document.Elements().Any(element => element.Name != "revocationDate"
                        && element.Name != "key" && element.Name != "reason")
                    || document.Elements("key").Count() != 1)
                    throw Invalid();
            }
            else
                throw Invalid();
        }

        var inventory = new HashSet<Guid>();
        foreach (var entry in admission.Keys)
        {
            if (entry is null || !inventory.Add(entry.Id) || !keys.TryGetValue(entry.Id, out var key)
                || entry.Sha256 != ExternalDataProtectionInput.Hash(key.ToString(SaveOptions.DisableFormatting))
                || entry.Revoked != revocations.Contains(entry.Id))
                throw Invalid();
            var creation = ReadDate(key, "creationDate");
            var activation = ReadDate(key, "activationDate");
            var expiration = ReadDate(key, "expirationDate");
            if (creation >= expiration || activation >= expiration)
                throw Invalid();
        }

        if (keys.Count != inventory.Count || revocations.Any(id => !inventory.Contains(id))
            || !keys.TryGetValue(admission.ActiveKeyId, out var active)
            || revocations.Contains(admission.ActiveKeyId)
            || ReadDate(active, "activationDate") > now
            || ReadDate(active, "expirationDate") <= now)
            throw Invalid();
        return (admission, documents);
    }

    private static void ValidateDescriptor(XElement key)
    {
        // Never activate an external type name, encryptedSecret decryptor, or custom algorithm.
        var descriptor = key.Element("descriptor");
        var body = descriptor?.Element("descriptor");
        if (key.Elements().Count() != 4 || key.Elements("descriptor").Count() != 1
            || descriptor?.Attribute("deserializerType")?.Value != typeof(AuthenticatedEncryptorDescriptorDeserializer).AssemblyQualifiedName
            || body is null || descriptor!.Elements().Count() != 1
            || body.Elements().Count() != 3
            || body.Element("encryption")?.Attribute("algorithm")?.Value != "AES_256_CBC"
            || body.Element("validation")?.Attribute("algorithm")?.Value != "HMACSHA256"
            || body.Element("masterKey")?.Elements().Count() != 1
            || body.Element("masterKey")?.Element("value") is not { } value
            || key.Descendants().Any(element => element.Name.Namespace != XNamespace.None
                || element.Name.LocalName is "encryptedSecret" or "encryptedKey"))
            throw Invalid();
        byte[] material;
        try { material = Convert.FromBase64String(value.Value); }
        catch (FormatException) { throw Invalid(); }
        try
        {
            if (material.Length != 64) throw Invalid();
        }
        finally { CryptographicOperations.ZeroMemory(material); }
    }

    internal static DateTimeOffset ReadDate(XElement element, string name)
    {
        if (element.Elements(name).Count() != 1) throw Invalid();
        try { return XmlConvert.ToDateTimeOffset(element.Element(name)!.Value); }
        catch (FormatException) { throw Invalid(); }
    }

    internal static XElement ParseXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || Encoding.UTF8.GetByteCount(xml) > MaximumBytes)
            throw Invalid();
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumBytes
        });
        try { return XElement.Load(reader); }
        catch (XmlException) { throw Invalid(); }
    }

    private static T Parse<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateProperties(document.RootElement);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw Invalid();
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    internal static InvalidOperationException Invalid() => new("data_protection_admission_rejected");
}
