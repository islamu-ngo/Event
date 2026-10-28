namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ModerationPortabilityContractTests
{
    private const string Document = """
        {
          "schemaVersion":1,"catalogueRevision":1,"modes":["Embedded","RemoteStrict"],
          "classes":[{"reference":{"code":"conduct","revision":1},"meaning":"Confirmed conduct","ground":"CompatibleConfirmedConduct","basis":"ConfirmedWarning"}],
          "reasons":[{"reference":{"code":"warning","revision":1},"meaning":"Confirmed warning","class":{"code":"conduct","revision":1}}],
          "profiles":[{
            "rule":{"code":"notice","revision":1},"reason":{"code":"warning","revision":1},
            "referenceProfile":"CFG01","referenceRevision":1,"grade":"G0",
            "scopeKinds":["Tenant"],"subjectKinds":["Human"],
            "effects":[{"kind":"ReasonedNotice","mode":"Notice","target":"SelectedOperations","capability":null,"execution":"NativeOwner"}],
            "bundle":"Unaffected","protectionPurpose":null,"strategies":[],"timing":{"kind":"NoEnforcementInterval","duration":"00:00:00"},
            "requiredApprovals":1,"contestationCalendarMonths":1,"recognition":"Never",
            "recipes":[{
              "strategy":null,"content":"PreserveSafePublished","management":"CurrentAuthority","resources":"CurrentAudience",
              "newAdmission":"CurrentEligibility","existingAdmission":"CurrentEligibility","privateDisclosure":"CurrentAudience",
              "settlement":"NoBookingChange","capacity":"NoSeatChange","credentials":"CurrentCredentialLifecycle",
              "notifications":"DurableSafeSubjectAndAdmin","recognition":"NoCaptureFromThisControl",
              "pendingWork":"RecheckCurrentEffectAtUseAndDispatch","restoration":"RecomputeCurrentRightsOnly"
            }]
          }],
          "supportedProfiles":[{"code":"notice","revision":1}]
        }
        """;

    [Test]
    public async Task ExplicitNullSection_IsRejectedRatherThanErasingRequestedSupport()
    {
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(Package("null")))
            .Throws<ConfigurationPortabilityContractException>();
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(Manifest("null")))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task Serialization_ReappliesSensitiveDuplicateAndCountGuards()
    {
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(Package(Document));
        var sensitive = package with
        {
            Spec = package.Spec with
            {
                Settings = new Dictionary<string, JsonElement> { ["accessToken"] = JsonSerializer.SerializeToElement<object?>(null) }
            }
        };
        await Assert.That(() => ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(sensitive))
            .Throws<ConfigurationPortabilityContractException>();
        var source = package.Spec.Moderation!;
        var duplicate = package with { Spec = package.Spec with { Moderation = source with { Classes = [source.Classes[0], source.Classes[0]] } } };
        await Assert.That(() => ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(duplicate))
            .Throws<ConfigurationPortabilityContractException>();
        var excessive = package with
        {
            Spec = package.Spec with
            {
                Moderation = source with { Classes = Enumerable.Repeat(source.Classes[0], 257).ToArray() }
            }
        };
        await Assert.That(() => ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(excessive))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task CompleteSection_RoundTripsAtAllThreeOwners()
    {
        var manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(Manifest(Document));
        var roundTrip = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest));
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(Package(Document));
        var packageRoundTrip = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package));
        foreach (var document in new[]
        {
            roundTrip.Spec.Instance.Moderation!, roundTrip.Spec.Tenants[0].Spec.Moderation!,
            packageRoundTrip.Spec.Moderation!
        })
        {
            await Assert.That(document.Profiles[0].Recipes[0].Restoration)
                .IsEqualTo(ConfigurationModerationRestorationMode.RecomputeCurrentRightsOnly);
            await Assert.That(document.Profiles[0].Effects[0].Execution)
                .IsEqualTo(ConfigurationModerationExecution.NativeOwner);
            await Assert.That(document.SupportedProfiles[0].Code).IsEqualTo("notice");
        }
    }

    [Test]
    public async Task EventScope_RoundTripsWithoutBecomingAResourceOrTenantScope()
    {
        var document = Document.Replace("\"scopeKinds\":[\"Tenant\"]", "\"scopeKinds\":[\"Event\"]", StringComparison.Ordinal);
        var manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(Manifest(document));
        var roundTrip = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest));
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(Package(document));
        var packageRoundTrip = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package));
        foreach (var restored in new[]
        {
            roundTrip.Spec.Instance.Moderation!, roundTrip.Spec.Tenants[0].Spec.Moderation!,
            packageRoundTrip.Spec.Moderation!
        })
            await Assert.That(restored.Profiles[0].ScopeKinds.Single()).IsEqualTo(ConfigurationModerationScopeKind.Event);
    }

    [Test]
    [Arguments("\"7\"")]
    [Arguments("7")]
    [Arguments("\"event\"")]
    [Arguments("\"Unknown\"")]
    public async Task EventScope_RejectsOrdinalCaseAndUnknownAliases(string token)
    {
        var document = Document.Replace("\"scopeKinds\":[\"Tenant\"]", $"\"scopeKinds\":[{token}]", StringComparison.Ordinal);
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(Package(document)))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    [Arguments("caseId")]
    [Arguments("jurisdictionId")]
    [Arguments("profileId")]
    [Arguments("approval")]
    [Arguments("operationalReadiness")]
    [Arguments("endpoint")]
    [Arguments("accessToken")]
    public async Task AuthorityOrSensitiveMembers_AreRejectedAtNestedDepth(string member)
    {
        var document = JsonNode.Parse(Document)!;
        document["profiles"]![0]![member] = null;
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Package(document.ToJsonString()))).Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    [Arguments("\"grade\":\"G0\"", "\"grade\":0")]
    [Arguments("\"grade\":\"G0\"", "\"grade\":\"0\"")]
    [Arguments("\"grade\":\"G0\"", "\"grade\":\"g0\"")]
    [Arguments("\"catalogueRevision\":1", "\"catalogueRevision\":1.0")]
    [Arguments("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")]
    [Arguments("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [Arguments("\"scopeKinds\":[\"Tenant\"]", "\"scopeKinds\":[\"Tenant\",\"Tenant\"]")]
    [Arguments("\"restoration\":\"RecomputeCurrentRightsOnly\"", "\"restoration\":\"Unknown\"")]
    [Arguments("\"strategy\":null,", "")]
    [Arguments("\"capability\":null,", "")]
    [Arguments("\"protectionPurpose\":null,", "")]
    public async Task AmbiguousOrInvalidShape_FailsClosed(string original, string replacement)
    {
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Package(Document.Replace(original, replacement, StringComparison.Ordinal))))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task SemanticDuplicatesAndOverBudgetDefinitions_AreRejected()
    {
        var document = JsonNode.Parse(Document)!;
        var classes = document["classes"]!.AsArray();
        classes.Add(classes[0]!.DeepClone());
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Package(document.ToJsonString()))).Throws<ConfigurationPortabilityContractException>();
        classes.Clear();
        for (int i = 0; i < 257; i++)
            classes.Add(JsonNode.Parse($$"""
                {"reference":{"code":"class{{i}}","revision":1},"meaning":"Meaning","ground":"PublicationMisuse","basis":"FinalConductFinding"}
                """));
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Package(document.ToJsonString()))).Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task MissingRecipeDimensionAndNullCollections_AreRejected()
    {
        var document = JsonNode.Parse(Document)!;
        document["profiles"]![0]!["recipes"]![0]!.AsObject().Remove("settlement");
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Package(document.ToJsonString()))).Throws<ConfigurationPortabilityContractException>();
        document = JsonNode.Parse(Document)!;
        document["profiles"]![0]!["effects"] = null;
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Package(document.ToJsonString()))).Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task InMemoryInvalidEnum_CannotEscapeViaSerialization()
    {
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(Package(Document));
        var source = package.Spec.Moderation!;
        var profiles = source.Profiles.ToArray();
        profiles[0] = profiles[0] with { Grade = (ConfigurationModerationGrade)999 };
        var invalid = package with
        {
            Spec = package.Spec with { Moderation = source with { Profiles = profiles } }
        };
        await Assert.That(() => ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(invalid))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task CallerOwnedNestedCollections_CannotMutateTheDocument()
    {
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(Package(Document));
        var source = package.Spec.Moderation!;
        var recipes = source.Profiles[0].Recipes.ToArray();
        var scopes = source.Profiles[0].ScopeKinds.ToArray();
        var effects = source.Profiles[0].Effects.ToArray();
        var profiles = new[] { source.Profiles[0] with { Recipes = recipes, ScopeKinds = scopes, Effects = effects } };
        var snapshot = source with { Profiles = profiles };
        recipes[0] = recipes[0] with { Restoration = ConfigurationModerationRestorationMode.AuthorizedObjectDecision };
        scopes[0] = ConfigurationModerationScopeKind.Instance;
        effects[0] = effects[0] with { Execution = ConfigurationModerationExecution.OverlayDenial };
        profiles[0] = profiles[0] with { RequiredApprovals = 8 };
        var encoded = ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(
            package with { Spec = package.Spec with { Moderation = snapshot } });
        var retained = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(encoded).Spec.Moderation!;
        await Assert.That(retained.Profiles[0].Recipes[0].Restoration)
            .IsEqualTo(ConfigurationModerationRestorationMode.RecomputeCurrentRightsOnly);
        await Assert.That(retained.Profiles[0].ScopeKinds[0]).IsEqualTo(ConfigurationModerationScopeKind.Tenant);
        await Assert.That(retained.Profiles[0].Effects[0].Execution).IsEqualTo(ConfigurationModerationExecution.NativeOwner);
        await Assert.That(retained.Profiles[0].RequiredApprovals).IsEqualTo(1);
    }

    [Test]
    public async Task ExcessiveDepthAndMeaningLength_AreRejected()
    {
        string deep = new string('[', 40) + "null" + new string(']', 40);
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Package(Document.Replace("\"strategies\":[]", "\"strategies\":" + deep, StringComparison.Ordinal))))
            .Throws<ConfigurationPortabilityContractException>();
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Package(Document.Replace("Confirmed conduct", new string('x', 4001), StringComparison.Ordinal))))
            .Throws<ConfigurationPortabilityContractException>();
    }

    private static byte[] Manifest(string document) => Encoding.UTF8.GetBytes("""
        {"$schema":"https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
         "apiVersion":"configuration.islamu.org/v1alpha2","kind":"ConfigurationManifest","metadata":{"name":"sample"},
         "spec":{"instance":{"settings":{},"documents":{},"legalDocuments":{},"moderation":MODERATION_DOCUMENT},
         "tenants":[{"metadata":{"name":"sample"},"spec":{"displayName":"Sample","settings":{},"documents":{},"legalDocuments":{},"moderation":MODERATION_DOCUMENT}}]}}
        """.Replace("MODERATION_DOCUMENT", document, StringComparison.Ordinal));

    private static byte[] Package(string document) => Encoding.UTF8.GetBytes("""
        {"$schema":"https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json",
         "apiVersion":"configuration.islamu.org/v1alpha2","kind":"TenantConfigurationPackage",
         "metadata":{"name":"sample","source":{"tenantName":"sample"}},
         "spec":{"displayName":"Sample","settings":{},"documents":{},"legalDocuments":{},"moderation":MODERATION_DOCUMENT}}
        """.Replace("MODERATION_DOCUMENT", document, StringComparison.Ordinal));
}
