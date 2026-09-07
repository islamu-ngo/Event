namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Reflection;
using System.Text;
using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Domain;

public sealed class ConfigurationManifestExportQueryTests
{
    private const int MaximumExportBytes = 4 * 1024 * 1024;
    private const string QueryTypeName =
        "Explore.Application.Features.ConfigurationManifest.Requests.Queries.ExportConfigurationManifestQuery";
    private const string SerializerTypeName = "ConfigurationManifestExportJsonSerializer";

    // The export query, contract, result, and serializer are declared in Explore.Application, while
    // the manifest DTOs now live in Event.Wire.Contracts. Anchor the Application-type probes on an
    // Explore.Application type so they resolve against the assembly that actually declares them.
    private static readonly Assembly ApplicationAssembly =
        typeof(AuthorizeResourceAttribute).Assembly;

    [Test]
    public async Task Query_UsesCurrentInstanceSettingsViewAndExplicitExportFacts()
    {
        Type queryType = RequireApplicationType(QueryTypeName);
        AuthorizeResourceAttribute authorization = queryType
            .GetCustomAttribute<AuthorizeResourceAttribute>()
            ?? throw new InvalidOperationException("The whole-instance export query must declare resource authorization.");

        await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(queryType)).IsTrue();

        ConstructorInfo constructor = queryType.GetConstructors().Single();
        await Assert.That(constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(Guid)))
            .IsFalse()
            .Because("trusted server context, not the caller, selects the current instance");

        object overrides = Enum.Parse(
            constructor.GetParameters().Single().ParameterType,
            "Overrides",
            ignoreCase: false);
        var request = (ISecureRequest)constructor.Invoke([overrides]);
        await Assert.That(request.ResourceId).IsEqualTo("instance.configuration-manifest.export");
        await Assert.That(request.AuthorizationFacts).IsNotNull();
        await Assert.That(request.AuthorizationFacts!.GetType().Name)
            .IsEqualTo("ConfigurationManifestExportAuthorizationFacts");
    }

    [Test]
    public async Task TenantRepository_ExposesDedicatedActiveCurrentInstanceEntityRead()
    {
        MethodInfo? query = typeof(ITenantRepository).GetMethod(
            "GetAllActiveForConfigurationManifestExportAsync",
            BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(query).IsNotNull()
            .Because("whole-instance export must not reuse an ambient-tenant or unconstrained maintenance query");
        ParameterInfo[] parameters = query!.GetParameters();
        await Assert.That(parameters.Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo([typeof(int), typeof(CancellationToken)]);
        await Assert.That(query.ReturnType).IsEqualTo(typeof(Task<IReadOnlyList<Tenant>>));
    }

    [Test]
    public async Task Serializer_SingleAndMultiTenantDocumentsAreTypedAndOrdinallyOrdered()
    {
        ConfigurationManifestV1Alpha2 single = Manifest(
            "Overrides",
            flattened: false,
            Tenant("primary", "Primary Community", "z.value", "tenant.branding"));
        ConfigurationManifestV1Alpha2 multiple = Manifest(
            "Overrides",
            flattened: false,
            Tenant("z-community", "Z Community", "z.value", "tenant.branding"),
            Tenant("a-community", "A Community", "a.value", "tenant.paid_event_policy"));

        byte[] singleBytes = Serialize(single);
        byte[] first = Serialize(multiple);
        byte[] second = Serialize(multiple);
        using JsonDocument singleJson = JsonDocument.Parse(singleBytes);
        using JsonDocument multiJson = JsonDocument.Parse(first);

        await Assert.That(singleJson.RootElement.GetProperty("spec").GetProperty("tenants").GetArrayLength())
            .IsEqualTo(1);
        JsonElement tenants = multiJson.RootElement.GetProperty("spec").GetProperty("tenants");
        await Assert.That(tenants.GetArrayLength()).IsEqualTo(2);
        await Assert.That(tenants[0].GetProperty("metadata").GetProperty("name").GetString())
            .IsEqualTo("a-community");
        await Assert.That(tenants[1].GetProperty("metadata").GetProperty("name").GetString())
            .IsEqualTo("z-community");
        await Assert.That(tenants[0].GetProperty("spec").GetProperty("documents")
                .GetProperty("tenant.paid_event_policy").GetProperty("schemaVersion").GetInt32())
            .IsEqualTo(1);
        await Assert.That(first).IsEquivalentTo(second);
        await Assert.That(first[^1]).IsEqualTo((byte)'\n');
        await Assert.That(first.AsSpan().IndexOf("\r\n"u8)).IsEqualTo(-1);
    }

    [Test]
    public async Task Serializer_OverridesAndPortablePinFlatteningAndSovereignOmission()
    {
        byte[] overrides = Serialize(Manifest(
            "Overrides",
            flattened: false,
            Tenant("primary", "Primary Community", "events.require_approval", "tenant.paid_event_policy")));
        byte[] portable = Serialize(Manifest(
            "Portable",
            flattened: true,
            Tenant("primary", "Primary Community", "events.user_submission_enabled", "tenant.paid_event_policy")));

        using JsonDocument overridesJson = JsonDocument.Parse(overrides);
        using JsonDocument portableJson = JsonDocument.Parse(portable);
        JsonElement overridesMetadata = overridesJson.RootElement.GetProperty("metadata").GetProperty("export");
        JsonElement portableMetadata = portableJson.RootElement.GetProperty("metadata").GetProperty("export");

        await Assert.That(overridesMetadata.GetProperty("view").GetString()).IsEqualTo("Overrides");
        await Assert.That(overridesMetadata.GetProperty("effectiveValuesFlattened").GetBoolean()).IsFalse();
        await Assert.That(portableMetadata.GetProperty("view").GetString()).IsEqualTo("Portable");
        await Assert.That(portableMetadata.GetProperty("effectiveValuesFlattened").GetBoolean()).IsTrue();
        await Assert.That(portableMetadata.GetProperty("sensitiveValuesOmitted").GetBoolean()).IsTrue();
        await Assert.That(portableMetadata.GetProperty("sovereignValuesOmitted").GetBoolean()).IsTrue();
        await Assert.That(portableMetadata.GetProperty("sovereignLockedFields")
                .EnumerateArray().Select(value => value.GetString()).ToArray())
            .IsEquivalentTo(new string?[] { "providerCredentials", "saleControl", "liability", "refundExecution" });

        string output = Encoding.UTF8.GetString(portable);
        foreach (string forbidden in new[]
                 {
                     "must-never-export",
                     "buyerEmail",
                     "providerAccountId",
                     "secretBinding",
                     "inlineCiphertext",
                     "infisicalPath",
                     "environmentVariableName",
                     "reconciliationState",
                     "acceptanceEvidence"
                 })
        {
            await Assert.That(output).DoesNotContain(forbidden, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Test]
    public async Task ExportContract_PreflightsFourMiBAggregateBeforeExposingBytes()
    {
        Type contract = RequireApplicationType(
            "Explore.Application.Features.ConfigurationManifest.Requests.Queries.ConfigurationManifestExportContract");
        FieldInfo maximum = contract.GetField(
            "MaximumUtf8Bytes",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("The export contract must expose its import-compatible byte ceiling.");
        FieldInfo failureCode = contract.GetField(
            "TooLargeFailureCode",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("The export contract must expose its stable overflow code.");

        await Assert.That(maximum.GetRawConstantValue()).IsEqualTo(MaximumExportBytes);
        await Assert.That(failureCode.GetRawConstantValue())
            .IsEqualTo("configuration_manifest_export_too_large");

        Type resultType = RequireApplicationType(
            "Explore.Application.Features.ConfigurationManifest.Requests.Queries.ConfigurationManifestExportResult");
        await Assert.That(resultType.GetProperty("Utf8Json")?.PropertyType)
            .IsEqualTo(typeof(ReadOnlyMemory<byte>));
        await Assert.That(resultType.GetProperty("FileName")?.PropertyType).IsEqualTo(typeof(string));
    }

    private static byte[] Serialize(ConfigurationManifestV1Alpha2 manifest)
    {
        Type serializer = ApplicationAssembly.GetTypes()
            .SingleOrDefault(type => type.Name == SerializerTypeName)
            ?? throw new InvalidOperationException(
                $"Missing {SerializerTypeName}; the current tenant-scoped serializer is not the whole-instance contract.");
        MethodInfo method = serializer.GetMethod(
            "Serialize",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(ConfigurationManifestV1Alpha2)],
            modifiers: null)
            ?? throw new InvalidOperationException("The canonical serializer must accept ConfigurationManifestV1Alpha2.");
        return (byte[])method.Invoke(null, [manifest])!;
    }

    private static Type RequireApplicationType(string fullName) =>
        ApplicationAssembly.GetType(fullName)
        ?? throw new InvalidOperationException($"Missing planned whole-instance production type: {fullName}.");

    private static ConfigurationManifestV1Alpha2 Manifest(
        string view,
        bool flattened,
        params ConfigurationManifestTenantV1Alpha2[] tenants) =>
        new()
        {
            Schema = ConfigurationManifestContractMetadata.SchemaId,
            ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
            Kind = ConfigurationManifestContractMetadata.Kind,
            Metadata = new ConfigurationManifestMetadataV1Alpha2
            {
                Name = "current-instance",
                Export = new ConfigurationManifestExportMetadataV1Alpha2
                {
                    View = view,
                    EffectiveValuesFlattened = flattened,
                    SensitiveValuesOmitted = true,
                    AuthorityScope = "InstanceAndTenants",
                    SovereignValuesOmitted = true,
                    SovereignLockedFields =
                        ["providerCredentials", "saleControl", "liability", "refundExecution"]
                }
            },
            Spec = new ConfigurationManifestSpecV1Alpha2
            {
                Instance = new ConfigurationManifestInstanceV1Alpha2
                {
                    Settings = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["branding.display_name"] = Json("\"ISLAMU Event\"")
                    },
                    Documents = new SortedDictionary<string, ConfigurationManifestDocumentV1Alpha2>(StringComparer.Ordinal)
                    {
                        [ConfigurationManifestDocumentKeys.InstancePaidEventPolicy] = PaidPolicyDocument()
                    }
                },
                Tenants = tenants
            }
        };

    private static ConfigurationManifestTenantV1Alpha2 Tenant(
        string slug,
        string displayName,
        string settingKey,
        string documentKey) =>
        new()
        {
            Metadata = new ConfigurationManifestTenantMetadataV1Alpha2 { Name = slug },
            Spec = new ConfigurationManifestTenantSpecV1Alpha2
            {
                DisplayName = displayName,
                Settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [settingKey] = Json("true")
                },
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(StringComparer.Ordinal)
                {
                    [documentKey] = documentKey == "tenant.branding"
                        ? new ConfigurationManifestDocumentV1Alpha2
                        {
                            SchemaVersion = 1,
                            Payload = Json("""{"logoUrl":"https://cdn.example/logo.svg","displayName":"Community"}""")
                        }
                        : PaidPolicyDocument()
                }
            }
        };

    private static ConfigurationManifestDocumentV1Alpha2 PaidPolicyDocument() =>
        new()
        {
            SchemaVersion = 1,
            Payload = Json(
                """{"requiresLocalVerification":false,"isPaymentsEnabled":true,"allowedOrganizerKindIds":[2],"allowedCurrencyCodes":["USD"],"defaultCurrencyCode":"USD","refundProtectionIds":[],"currencyRiskLimits":[],"requiresFirstPaidEventReview":false,"farFutureReviewThresholdDays":null}""")
        };

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
