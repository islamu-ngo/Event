namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfigurationManifestContractTests
{
    private const string ContractNamespace =
        "ISLAMU.Wire.Contracts.ConfigurationPortability.";

    // The v1alpha2 manifest contract types are declared in the Event.Wire.Contracts assembly and
    // consumed by controllers, handlers, validators, and the OpenAPI transformer. Anchor the
    // reflection probes on the assembly that actually declares them rather than Explore.Application.
    private static readonly Assembly ContractAssembly =
        typeof(ConfigurationManifestContractMetadata).Assembly;

    [Test]
    public async Task ContractMetadata_UsesOneAlignedV1Alpha2Identity()
    {
        Type? metadataType = ContractAssembly.GetType(
            ContractNamespace + "ConfigurationManifestContractMetadata");

        await Assert.That(metadataType).IsNotNull();
        await Assert.That(ReadConstant(metadataType!, "SchemaId"))
            .IsEqualTo("https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json");
        await Assert.That(ReadConstant(metadataType, "ApiVersion"))
            .IsEqualTo("configuration.islamu.org/v1alpha2");
        await Assert.That(ReadConstant(metadataType, "Kind"))
            .IsEqualTo("ConfigurationManifest");
        await Assert.That(ReadConstant(metadataType, "MediaType"))
            .IsEqualTo("application/vnd.islamu.configuration-manifest.v1alpha2+json");
    }

    [Test]
    public async Task RootContract_RequiresClosedInstanceAndTenantScopes()
    {
        Type? rootType = ContractAssembly.GetType(
            ContractNamespace + "ConfigurationManifestV1Alpha2");

        await Assert.That(rootType).IsNotNull();
        await AssertClosedRequiredProperties(
            rootType!,
            "Schema",
            "ApiVersion",
            "Kind",
            "Metadata",
            "Spec");

        Type specType = RequiredProperty(rootType, "Spec").PropertyType;
        await AssertClosedRequiredProperties(specType, "Instance", "Tenants");

        Type instanceType = RequiredProperty(specType, "Instance").PropertyType;
        await AssertClosedRequiredProperties(
            instanceType,
            "Settings",
            "Documents",
            "LegalDocuments");
        await Assert.That(RequiredProperty(instanceType, "Settings").PropertyType)
            .IsEqualTo(typeof(IReadOnlyDictionary<string, JsonElement>));

        Type tenantsType = RequiredProperty(specType, "Tenants").PropertyType;
        await Assert.That(tenantsType.IsGenericType).IsTrue();
        await Assert.That(tenantsType.GetGenericTypeDefinition())
            .IsEqualTo(typeof(IReadOnlyList<>));

        Type tenantType = tenantsType.GetGenericArguments()[0];
        await AssertClosedRequiredProperties(tenantType, "Metadata", "Spec");

        Type tenantSpecType = RequiredProperty(tenantType, "Spec").PropertyType;
        await AssertClosedRequiredProperties(
            tenantSpecType,
            "DisplayName",
            "Settings",
            "Documents",
            "LegalDocuments");
        await Assert.That(RequiredProperty(tenantSpecType, "Settings").PropertyType)
            .IsEqualTo(typeof(IReadOnlyDictionary<string, JsonElement>));
    }

    [Test]
    public async Task Deserialize_StrictUnifiedEnvelope_AcceptsInstanceAndTenantSections()
    {
        Type? rootType = ContractAssembly.GetType(
            ContractNamespace + "ConfigurationManifestV1Alpha2");
        await Assert.That(rootType).IsNotNull();

        const string json =
            """
            {
              "$schema": "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
              "apiVersion": "configuration.islamu.org/v1alpha2",
              "kind": "ConfigurationManifest",
              "metadata": { "name": "primary-instance" },
              "spec": {
                "instance": {
                  "settings": {},
                  "documents": {},
                  "legalDocuments": {}
                },
                "tenants": [
                  {
                    "metadata": { "name": "default" },
                    "spec": {
                      "displayName": "Primary Community",
                      "settings": {},
                      "documents": {},
                      "legalDocuments": {}
                    }
                  }
                ]
              }
            }
            """;

        object? manifest = JsonSerializer.Deserialize(
            json,
            rootType!,
            SerializerOptions());

        await Assert.That(manifest).IsNotNull();
    }

    [Test]
    [Arguments("unexpected", "true")]
    [Arguments("spec.instance.unexpected", "true")]
    [Arguments("spec.tenants[0].spec.unexpected", "true")]
    public async Task Deserialize_UnknownMemberAtAnyScope_ThrowsJsonException(
        string path,
        string rawValue)
    {
        Type? rootType = ContractAssembly.GetType(
            ContractNamespace + "ConfigurationManifestV1Alpha2");
        await Assert.That(rootType).IsNotNull();

        string json = ManifestJsonWithUnknown(path, rawValue);
        JsonException? exception = null;

        try
        {
            _ = JsonSerializer.Deserialize(json, rootType!, SerializerOptions());
        }
        catch (JsonException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
    }

    private static JsonSerializerOptions SerializerOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false
        };

    private static string ReadConstant(Type type, string name) =>
        type.GetField(name, BindingFlags.Public | BindingFlags.Static)?
            .GetRawConstantValue() as string
        ?? throw new InvalidOperationException($"Missing contract constant '{name}'.");

    private static PropertyInfo RequiredProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"Missing required contract property '{type.FullName}.{name}'.");

    private static async Task AssertClosedRequiredProperties(
        Type type,
        params string[] expectedNames)
    {
        JsonUnmappedMemberHandlingAttribute? unmapped =
            type.GetCustomAttribute<JsonUnmappedMemberHandlingAttribute>();
        await Assert.That(unmapped).IsNotNull();
        await Assert.That(unmapped!.UnmappedMemberHandling)
            .IsEqualTo(JsonUnmappedMemberHandling.Disallow);

        string[] actualNames = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] orderedExpected = expectedNames
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(actualNames.SequenceEqual(
            orderedExpected,
            StringComparer.Ordinal)).IsTrue();

        foreach (string name in expectedNames)
        {
            PropertyInfo property = RequiredProperty(type, name);
            bool isRequired =
                property.GetCustomAttribute<RequiredMemberAttribute>() is not null
                || property.GetCustomAttribute<JsonRequiredAttribute>() is not null;
            await Assert.That(isRequired).IsTrue();
        }
    }

    private static string ManifestJsonWithUnknown(string path, string rawValue)
    {
        string rootUnknown = path == "unexpected"
            ? $""","unexpected":{rawValue}"""
            : string.Empty;
        string instanceUnknown = path == "spec.instance.unexpected"
            ? $""","unexpected":{rawValue}"""
            : string.Empty;
        string tenantUnknown = path == "spec.tenants[0].spec.unexpected"
            ? $""","unexpected":{rawValue}"""
            : string.Empty;

        return
            $$"""
            {
              "$schema": "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
              "apiVersion": "configuration.islamu.org/v1alpha2",
              "kind": "ConfigurationManifest",
              "metadata": { "name": "primary-instance" },
              "spec": {
                "instance": {
                  "settings": {},
                  "documents": {},
                  "legalDocuments": {}{{instanceUnknown}}
                },
                "tenants": [
                  {
                    "metadata": { "name": "default" },
                    "spec": {
                      "displayName": "Primary Community",
                      "settings": {},
                      "documents": {},
                      "legalDocuments": {}{{tenantUnknown}}
                    }
                  }
                ]
              }{{rootUnknown}}
            }
            """;
    }
}
