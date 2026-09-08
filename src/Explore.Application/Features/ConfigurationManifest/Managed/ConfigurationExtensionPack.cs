namespace Explore.Application.Features.ConfigurationManifest.Managed;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Importing;

public sealed record ConfigurationExtensionDescriptor(
    string SectionKey,
    int SchemaVersion,
    string MinimumEventVersion,
    string MaximumEventVersion,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> OwnedFieldPaths,
    string PayloadDigest);

public sealed record ConfigurationExtensionProvenance(
    string Publisher,
    string SourceReference,
    string LicenseExpression);

public sealed record ConfigurationExtensionSignature(
    string Issuer,
    string KeyId,
    string Algorithm,
    string SignedDigest,
    string Value);

public sealed record ConfigurationExtensionPack(
    string PackId,
    string PackVersion,
    ConfigurationExtensionProvenance Provenance,
    IReadOnlyList<ConfigurationExtensionDescriptor> Descriptors,
    IReadOnlyDictionary<string, JsonElement> Sections,
    ConfigurationExtensionSignature Signature);

public sealed record ConfigurationExtensionTrustedKey(
    string Issuer,
    string KeyId,
    string SubjectPublicKeyInfoBase64);

public sealed record ConfigurationExtensionTrustPolicy(
    string EventVersion,
    IReadOnlySet<string> AllowedLicenseExpressions,
    IReadOnlyList<ConfigurationExtensionTrustedKey> TrustedKeys);

public sealed record ConfigurationExtensionValidationResult(
    bool IsValid,
    string FailureCode,
    string PackDigest)
{
    public static ConfigurationExtensionValidationResult Valid(string digest) =>
        new(true, string.Empty, digest);

    public static ConfigurationExtensionValidationResult Invalid(
        string code,
        string digest = "") =>
        new(false, code, digest);
}

public static class ConfigurationExtensionPackValidator
{
    public const string SignatureAlgorithm = "ECDSA_P256_SHA256";
    private static readonly HashSet<string> ExecutablePropertyNames = new(
        [
            "script",
            "scripts",
            "sql",
            "migration",
            "migrations",
            "plugin",
            "plugins",
            "assembly",
            "command",
            "executable"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static ConfigurationExtensionValidationResult Validate(
        ConfigurationExtensionPack pack,
        ConfigurationExtensionTrustPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(policy);
        if (!Version.TryParse(policy.EventVersion, out Version? eventVersion)
            || string.IsNullOrWhiteSpace(pack.PackId)
            || !Version.TryParse(pack.PackVersion, out _)
            || pack.Descriptors.Count == 0
            || pack.Descriptors.Count != pack.Sections.Count)
        {
            return ConfigurationExtensionValidationResult.Invalid(
                "configuration_extension_contract_invalid");
        }

        if (!policy.AllowedLicenseExpressions.Contains(
                pack.Provenance.LicenseExpression))
        {
            return ConfigurationExtensionValidationResult.Invalid(
                "configuration_extension_license_untrusted");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (ConfigurationExtensionDescriptor descriptor in pack.Descriptors)
        {
            if (!keys.Add(descriptor.SectionKey)
                || !pack.Sections.TryGetValue(
                    descriptor.SectionKey,
                    out JsonElement section)
                || descriptor.SchemaVersion < 1
                || !Version.TryParse(
                    descriptor.MinimumEventVersion,
                    out Version? minimum)
                || !Version.TryParse(
                    descriptor.MaximumEventVersion,
                    out Version? maximum)
                || minimum > eventVersion
                || maximum < eventVersion
                || minimum > maximum
                || descriptor.OwnedFieldPaths.Count == 0
                || descriptor.OwnedFieldPaths.Any(path =>
                    !IsManagedFieldPath(path))
                || descriptor.Dependencies.Any(dependency =>
                    string.Equals(
                        dependency,
                        descriptor.SectionKey,
                        StringComparison.Ordinal))
                || ContainsExecutableProperty(section))
            {
                return ConfigurationExtensionValidationResult.Invalid(
                    "configuration_extension_descriptor_invalid");
            }

            string payloadDigest = CanonicalJson.Digest(section);
            if (!string.Equals(
                    payloadDigest,
                    descriptor.PayloadDigest,
                    StringComparison.Ordinal))
            {
                return ConfigurationExtensionValidationResult.Invalid(
                    "configuration_extension_payload_digest_invalid");
            }
        }

        string digest = PackDigest(pack);
        if (!string.Equals(
                pack.Signature.Algorithm,
                SignatureAlgorithm,
                StringComparison.Ordinal)
            || !string.Equals(
                pack.Signature.SignedDigest,
                digest,
                StringComparison.Ordinal))
        {
            return ConfigurationExtensionValidationResult.Invalid(
                "configuration_extension_signature_invalid",
                digest);
        }

        ConfigurationExtensionTrustedKey? trustedKey = policy.TrustedKeys
            .SingleOrDefault(key =>
                string.Equals(
                    key.Issuer,
                    pack.Signature.Issuer,
                    StringComparison.Ordinal)
                && string.Equals(
                    key.KeyId,
                    pack.Signature.KeyId,
                    StringComparison.Ordinal));
        if (trustedKey is null)
        {
            return ConfigurationExtensionValidationResult.Invalid(
                "configuration_extension_issuer_untrusted",
                digest);
        }

        try
        {
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(
                    trustedKey.SubjectPublicKeyInfoBase64),
                out _);
            bool valid = verifier.VerifyHash(
                Convert.FromHexString(digest),
                Convert.FromBase64String(pack.Signature.Value));
            return valid
                ? ConfigurationExtensionValidationResult.Valid(digest)
                : ConfigurationExtensionValidationResult.Invalid(
                    "configuration_extension_signature_invalid",
                    digest);
        }
        catch (FormatException)
        {
            return ConfigurationExtensionValidationResult.Invalid(
                "configuration_extension_signature_invalid",
                digest);
        }
        catch (CryptographicException)
        {
            return ConfigurationExtensionValidationResult.Invalid(
                "configuration_extension_signature_invalid",
                digest);
        }
    }

    public static string PackDigest(ConfigurationExtensionPack pack)
    {
        IEnumerable<string> fields =
        [
            Field("pack.id", pack.PackId),
            Field("pack.version", pack.PackVersion),
            Field("provenance.publisher", pack.Provenance.Publisher),
            Field("provenance.source", pack.Provenance.SourceReference),
            Field("provenance.license", pack.Provenance.LicenseExpression),
            .. pack.Descriptors
                .OrderBy(descriptor => descriptor.SectionKey, StringComparer.Ordinal)
                .SelectMany(descriptor => new[]
                {
                    Field($"descriptor.{descriptor.SectionKey}.schema", descriptor.SchemaVersion.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                    Field($"descriptor.{descriptor.SectionKey}.minimum", descriptor.MinimumEventVersion),
                    Field($"descriptor.{descriptor.SectionKey}.maximum", descriptor.MaximumEventVersion),
                    Field($"descriptor.{descriptor.SectionKey}.payload", descriptor.PayloadDigest),
                    Field($"descriptor.{descriptor.SectionKey}.dependencies", string.Join(',', descriptor.Dependencies.Order(StringComparer.Ordinal))),
                    Field($"descriptor.{descriptor.SectionKey}.owned", string.Join(',', descriptor.OwnedFieldPaths.Order(StringComparer.Ordinal)))
                })
        ];
        return ConfigurationImportDigest.Compute(fields);
    }

    private static string Field(string name, string value) =>
        $"{name.Length}:{name}{value.Length}:{value}";

    private static bool IsManagedFieldPath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Length <= 300
        && path.StartsWith("/", StringComparison.Ordinal)
        && !path.Contains("..", StringComparison.Ordinal)
        && !path.Contains("*", StringComparison.Ordinal);

    private static bool ContainsExecutableProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (ExecutablePropertyNames.Contains(property.Name)
                    || ContainsExecutableProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsExecutableProperty);
        }
        return false;
    }

    private static class CanonicalJson
    {
        public static string Digest(JsonElement element)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                Write(writer, element);
            }
            return ConfigurationImportDigest.ComputeBytes(stream.ToArray());
        }

        private static void Write(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty property in element.EnumerateObject()
                                 .OrderBy(property => property.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        Write(writer, property.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in element.EnumerateArray())
                        Write(writer, item);
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }
    }
}
