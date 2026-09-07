#:property RestorePackagesWithLockFile=false

using System.Text.Json;

const string ManifestSchemaId =
    "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json";
const string TenantPackageSchemaId =
    "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json";
const string ApiVersion = "configuration.islamu.org/v1alpha2";

string[] registryKeys =
[
    "instance.settings", "instance.documents", "instance.legal_documents",
    "tenant.settings", "tenant.documents", "tenant.legal_documents",
    "tenant.footer", "tenant.navigation", "tenant.templates", "tenant.lookups",
    "tenant.custom_property_definitions", "tenant.localization",
    "tenant.registration_policy", "tenant.modules", "extensions",
    "excluded.secrets", "excluded.pii", "excluded.application_data",
    "excluded.operational_state", "excluded.provider_bindings",
    "excluded.deployment_topology"
];
string[] excludedRegistryKeys =
[
    "excluded.secrets", "excluded.pii", "excluded.application_data",
    "excluded.operational_state", "excluded.provider_bindings",
    "excluded.deployment_topology"
];
string[] targetProperties = ["AuthorityKey", "Scope", "TenantId"];
string[] bindingProperties =
[
    "ApplyMode", "ArtifactDigest", "ExpiresAt", "MappingDigest",
    "RequiredApprovalDigest", "SelectedSectionsDigest", "Target",
    "TargetRevisionDigest"
];

if (args is not ["--write"] and not ["--check"])
{
    Console.Error.WriteLine("Usage: GenerateSetupAssistantRatchets.cs (--write|--check)");
    return 64;
}

string repositoryRoot = FindRepositoryRoot();
ValidateSchema(
    Path.Combine(repositoryRoot, "schemas", "configuration-manifest-v1alpha2.schema.json"),
    ManifestSchemaId,
    ApiVersion,
    "ConfigurationManifest");
ValidateSchema(
    Path.Combine(repositoryRoot, "schemas", "tenant-configuration-package-v1alpha2.schema.json"),
    TenantPackageSchemaId,
    ApiVersion,
    "TenantConfigurationPackage");

var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal)
{
    [Path.Combine(repositoryRoot, "eng", "setup-assistant", "generated",
        "browser-release-capabilities.json")] = GenerateBrowserCapability(),
    [Path.Combine(repositoryRoot, "eng", "setup-assistant", "generated",
        "setup-live-release-capabilities.json")] = GenerateSetupLiveCapability(),
    [Path.Combine(repositoryRoot, "eng", "setup-assistant", "generated",
        "frozen-contract-baseline.json")] = GenerateFrozenBaseline(
            ApiVersion,
            registryKeys,
            excludedRegistryKeys,
            targetProperties,
            bindingProperties)
};

if (args[0] == "--check")
{
    string[] stale = outputs
        .Where(output => !File.Exists(output.Key)
            || !File.ReadAllBytes(output.Key).AsSpan().SequenceEqual(output.Value))
        .Select(output => Path.GetRelativePath(repositoryRoot, output.Key).Replace('\\', '/'))
        .ToArray();
    if (stale.Length > 0)
    {
        Console.Error.WriteLine("Setup Assistant ratchets are missing or stale: "
            + string.Join(", ", stale));
        return 1;
    }

    Console.WriteLine("Setup Assistant ratchets are current (3/3).");
    return 0;
}

foreach ((string path, byte[] content) in outputs)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, content);
}

Console.WriteLine("Generated Setup Assistant ratchets (3/3).");
return 0;

static byte[] GenerateBrowserCapability() =>
    GenerateDisabledCapability("browser", ["secretEntry"]);

static byte[] GenerateSetupLiveCapability() =>
    GenerateDisabledCapability(
        "setup-live",
        ["targetEnrollment", "secretBindingReadiness", "secretBindingWrite", "savedProfiles"]);

static byte[] GenerateDisabledCapability(string target, IEnumerable<string> capabilities)
{
    using var stream = new MemoryStream();
    using (var writer = CreateWriter(stream))
    {
        writer.WriteStartObject();
        WriteMetadata(writer);
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("target", target);
        writer.WriteBoolean("targetEnabled", false);
        writer.WriteStartObject("capabilities");
        foreach (string capability in capabilities)
            writer.WriteBoolean(capability, false);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    return WithFinalNewline(stream);
}

static byte[] GenerateFrozenBaseline(
    string apiVersion,
    string[] registryKeys,
    string[] excludedRegistryKeys,
    string[] targetProperties,
    string[] bindingProperties)
{
    using var stream = new MemoryStream();
    using (var writer = CreateWriter(stream))
    {
        writer.WriteStartObject();
        WriteMetadata(writer);
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteStartObject("schemas");
        WriteSchemaFact(
            writer,
            "configurationManifest",
            "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
            apiVersion,
            "ConfigurationManifest");
        WriteSchemaFact(
            writer,
            "tenantConfigurationPackage",
            "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json",
            apiVersion,
            "TenantConfigurationPackage");
        writer.WriteEndObject();
        writer.WriteStartObject("portabilityRegistry");
        writer.WriteNumber("cardinality", registryKeys.Length);
        WriteStringArray(writer, "keys", registryKeys);
        WriteStringArray(writer, "excludedAuthorityKeys", excludedRegistryKeys);
        writer.WriteEndObject();
        writer.WriteStartObject("importSession");
        writer.WriteBoolean("targetBound", true);
        writer.WriteBoolean("valueFree", true);
        WriteStringArray(writer, "targetProperties", targetProperties);
        writer.WriteEndObject();
        writer.WriteStartObject("importPreview");
        writer.WriteBoolean("targetBound", true);
        writer.WriteBoolean("valueFree", true);
        WriteStringArray(writer, "bindingProperties", bindingProperties);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    return WithFinalNewline(stream);
}

static Utf8JsonWriter CreateWriter(Stream stream) =>
    new(stream, new JsonWriterOptions { Indented = true });

static void WriteMetadata(Utf8JsonWriter writer)
{
    writer.WriteStartObject("_metadata");
    writer.WriteStartArray("about");
    writer.WriteStringValue(
        "ABOUTME: Generated Setup Assistant architecture ratchet; do not edit by hand.");
    writer.WriteStringValue(
        "ABOUTME: Owned by eng/setup-assistant/GenerateSetupAssistantRatchets.cs.");
    writer.WriteEndArray();
    writer.WriteString(
        "generatedBy",
        "eng/setup-assistant/GenerateSetupAssistantRatchets.cs");
    writer.WriteEndObject();
}

static void WriteSchemaFact(
    Utf8JsonWriter writer,
    string propertyName,
    string schemaId,
    string apiVersion,
    string kind)
{
    writer.WriteStartObject(propertyName);
    writer.WriteString("schemaId", schemaId);
    writer.WriteString("apiVersion", apiVersion);
    writer.WriteString("kind", kind);
    writer.WriteBoolean("closedObjects", true);
    writer.WriteEndObject();
}

static void WriteStringArray(
    Utf8JsonWriter writer,
    string propertyName,
    IEnumerable<string> values)
{
    writer.WriteStartArray(propertyName);
    foreach (string value in values)
    {
        writer.WriteStringValue(value);
    }
    writer.WriteEndArray();
}

static byte[] WithFinalNewline(MemoryStream stream)
{
    byte[] content = stream.ToArray();
    byte[] result = new byte[content.Length + 1];
    content.CopyTo(result, 0);
    result[^1] = (byte)'\n';
    return result;
}

static string FindRepositoryRoot()
{
    DirectoryInfo? current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
            && Directory.Exists(Path.Combine(current.FullName, "schemas")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate the repository root.");
}

static void ValidateSchema(
    string path,
    string schemaId,
    string apiVersion,
    string kind)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
    JsonElement root = document.RootElement;
    bool valid = string.Equals(
            root.GetProperty("$schema").GetString(),
            "https://json-schema.org/draft/2020-12/schema",
            StringComparison.Ordinal)
        && string.Equals(root.GetProperty("$id").GetString(), schemaId,
            StringComparison.Ordinal)
        && root.GetProperty("additionalProperties").ValueKind == JsonValueKind.False
        && string.Equals(root.GetProperty("properties").GetProperty("$schema")
            .GetProperty("const").GetString(), schemaId, StringComparison.Ordinal)
        && string.Equals(root.GetProperty("properties").GetProperty("apiVersion")
            .GetProperty("const").GetString(), apiVersion, StringComparison.Ordinal)
        && string.Equals(root.GetProperty("properties").GetProperty("kind")
            .GetProperty("const").GetString(), kind, StringComparison.Ordinal)
        && AllTypedObjectsAreClosed(root);
    if (!valid)
    {
        throw new InvalidOperationException(
            $"Schema identity or closed-object behavior drifted: {Path.GetFileName(path)}");
    }
}

static bool AllTypedObjectsAreClosed(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        if (element.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), "object", StringComparison.Ordinal)
            && (!element.TryGetProperty("additionalProperties", out JsonElement additional)
                || additional.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        return element.EnumerateObject()
            .All(property => AllTypedObjectsAreClosed(property.Value));
    }

    return element.ValueKind != JsonValueKind.Array
        || element.EnumerateArray().All(AllTypedObjectsAreClosed);
}
