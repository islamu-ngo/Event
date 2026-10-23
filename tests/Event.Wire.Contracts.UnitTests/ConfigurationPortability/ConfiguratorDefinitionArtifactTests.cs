namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using ISLAMU.Wire.Contracts.Identity;

public sealed class ConfiguratorDefinitionArtifactTests
{
    [Test]
    public async Task CanonicalDefinitionHasExpectedIdentityAndNoEmbeddedPrivateAnswers()
    {
        string directory = Path.Combine(Root(), "configurator", "definitions", "islamu-event");
        byte[] nativeBytes = File.ReadAllBytes(Path.Combine(directory, "definition.json"));
        using JsonDocument native = JsonDocument.Parse(nativeBytes);
        using JsonDocument release = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "release.json")));
        await Assert.That(native.RootElement.GetProperty("familyId").GetGuid())
            .IsEqualTo(Guid.Parse("a4bcfaa6-9979-43e5-b1f5-00d552618cdf"));
        await Assert.That(release.RootElement.GetProperty("revision").GetInt64())
            .IsEqualTo(native.RootElement.GetProperty("revision").GetInt64());
        await Assert.That(release.RootElement.GetProperty("definitionDigest").GetString())
            .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(nativeBytes)));
        await Assert.That(Directory.EnumerateFiles(directory).Select(path => Path.GetFileName(path)))
            .IsEquivalentTo(["configurator.yaml", "definition.json", "release.json"]);
        await Assert.That(Directory.EnumerateDirectories(directory)).IsEmpty();
        var selectors = release.RootElement.GetProperty("browserLaunchFragment").GetString()!.Split('&')
            .Select(part => part.Split('=', 2)).ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.Ordinal);
        await Assert.That(selectors.Keys).IsEquivalentTo(["src", "digest", "appVersion", "entry", "locale"]);
        await Assert.That(selectors["digest"]).IsEqualTo(release.RootElement.GetProperty("definitionDigest").GetString());
        await Assert.That(selectors["src"]).IsEqualTo(release.RootElement.GetProperty("definitionSource").GetString());
        await Assert.That(selectors["appVersion"]).IsEqualTo("0.0.0-development");
        await Assert.That(selectors["entry"]).IsEqualTo("configuration-manifest");
        await Assert.That(selectors["locale"]).IsEqualTo("en");
        await Assert.That(native.RootElement.GetProperty("artifacts").GetArrayLength()).IsEqualTo(5);
        await Assert.That(native.RootElement.GetProperty("fields").GetArrayLength() > 1).IsTrue();
        await Assert.That(native.RootElement.GetProperty("fields").EnumerateArray()
            .Any(field => field.GetProperty("id").GetString() == "env.IDENTITY_DATABASE_CONNECTION_STRING")).IsFalse();
        JsonElement environment = native.RootElement.GetProperty("artifacts").EnumerateArray()
            .Single(artifact => artifact.GetProperty("id").GetString() == "environment");
        await Assert.That(environment.GetProperty("projection").GetProperty("members")
            .TryGetProperty("IDENTITY_DATABASE_CONNECTION_STRING", out _)).IsFalse();
        foreach (JsonElement field in native.RootElement.GetProperty("fields").EnumerateArray())
        {
            if (field.GetProperty("sensitivity").GetString() is not "public")
            {
                await Assert.That(field.TryGetProperty("default", out _)).IsFalse();
                await Assert.That(field.TryGetProperty("recommendation", out _)).IsFalse();
            }
        }
        await Assert.That(native.RootElement.TryGetProperty("answers", out _)).IsFalse();
    }

    [Test]
    public async Task TenantRowsAreBoundedObjectsWithOrdinalIdentityAndWholeRowProjection()
    {
        using JsonDocument definition = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            Root(), "configurator", "definitions", "islamu-event", "definition.json")));
        JsonElement tenants = definition.RootElement.GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("id").GetString() == "configuration-manifest.tenants");
        await Assert.That(tenants.GetProperty("type").GetString()).IsEqualTo("list");
        await Assert.That(tenants.GetProperty("itemType").GetString()).IsEqualTo("object");
        await Assert.That(tenants.GetProperty("maxItems").GetInt32()).IsEqualTo(256);
        await Assert.That(tenants.GetProperty("uniqueBy").EnumerateArray().Select(item => item.GetString()!))
            .IsEquivalentTo(["metadata", "name"]);
        JsonElement metadata = tenants.GetProperty("properties").EnumerateArray()
            .Single(field => field.GetProperty("id").GetString() == "metadata");
        JsonElement name = metadata.GetProperty("properties")[0];
        await Assert.That(name.GetProperty("required").GetBoolean()).IsTrue();
        await Assert.That(name.GetProperty("minLength").GetInt32()).IsEqualTo(1);
        await Assert.That(name.GetProperty("maxLength").GetInt32()).IsEqualTo(100);
    }

    [Test]
    public async Task IndependentTenantVectorsKeepSelectedRowsAndRejectHostOverflow()
    {
        foreach (int count in new[] { 1, 2, 3 })
        {
            byte[] bytes = Artifact(count == 2 ? "configuration-manifest.json"
                : "configuration-manifest-" + count + "-tenants.json");
            ConfigurationManifestV1Alpha2 manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(bytes);
            await Assert.That(manifest.Spec.Tenants.Count).IsEqualTo(count);
            await Assert.That(manifest.Spec.Tenants.Select(tenant => tenant.Metadata.Name))
                .IsEquivalentTo(new[] { "community", "second-community", "third-community" }.Take(count));
            await Assert.That(manifest.Spec.Tenants[0].Spec.Settings["events.require_approval"].GetBoolean()).IsTrue();
            if (count == 1)
                await Assert.That(Encoding.UTF8.GetString(bytes)).DoesNotContain("second-community");
        }
        JsonObject source = JsonNode.Parse(Artifact("configuration-manifest-1-tenants.json"))!.AsObject();
        JsonArray tenants = source["spec"]!["tenants"]!.AsArray();
        JsonNode first = tenants[0]!.DeepClone();
        tenants.Clear();
        await Assert.That(ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            Encoding.UTF8.GetBytes(source.ToJsonString())).Spec.Tenants).IsEmpty();
        for (int index = 0; index < 256; index++)
        {
            JsonNode tenant = first.DeepClone();
            tenant["metadata"]!["name"] = "tenant-" + index;
            tenants.Add(tenant);
        }
        await Assert.That(ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            Encoding.UTF8.GetBytes(source.ToJsonString())).Spec.Tenants.Count).IsEqualTo(256);
        JsonNode overflow = first.DeepClone();
        overflow["metadata"]!["name"] = "tenant-overflow";
        tenants.Add(overflow);
        await Assert.That(() => ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            Encoding.UTF8.GetBytes(source.ToJsonString()))).Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task ComposedManifestAndPackagePreserveIndependentWireSemantics()
    {
        ConfigurationManifestV1Alpha2 manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            Artifact("configuration-manifest.json"));
        TenantConfigurationPackageV1Alpha2 package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            Artifact("tenant-package.json"));
        await Assert.That(manifest.Spec.Instance.Settings["events.require_approval"].GetBoolean()).IsTrue();
        await Assert.That(manifest.Spec.Tenants.Count).IsEqualTo(2);
        await Assert.That(manifest.Spec.Tenants[1].Metadata.Name).IsEqualTo("second-community");
        await Assert.That(package.Spec.Settings["events.require_approval"].GetBoolean()).IsTrue();
        await Assert.That(package.Metadata.Source.TenantName).IsEqualTo("community");
        await Assert.That(package.Spec.Documents["tenant.branding"].Payload.GetProperty("displayName").GetString())
            .IsEqualTo("Community");
        await Assert.That(package.Spec.LegalDocuments["TenantPrivacyNotice"].LifecycleIntent).IsEqualTo("Draft");
        ConfigurationManifestV1Alpha2 roundTrip = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest));
        await Assert.That(roundTrip.Spec.Tenants.Select(tenant => tenant.Metadata.Name))
            .IsEquivalentTo(["community", "second-community"]);
        await Assert.That(roundTrip.Spec.Tenants[0].Spec.Settings["events.require_approval"].GetBoolean()).IsTrue();
        await Assert.That(roundTrip.Spec.Tenants[0].Spec.Documents["tenant.branding"].Payload
            .GetProperty("displayName").GetString()).IsEqualTo("Community");
        await Assert.That(ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
            ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package)).Spec.DisplayName)
            .IsEqualTo(package.Spec.DisplayName);
    }

    [Test]
    public async Task IdentityContentAndRevisionRemainIndependentIntegrityPreconditions()
    {
        byte[] bytes = Artifact("operator-identity.json");
        OperatorIdentityManifest manifest = OperatorIdentityManifestJson.Parse(bytes);
        await Assert.That(manifest.Document.GetProperty("operatorId").GetGuid().Version).IsEqualTo(7);
        await Assert.That(manifest.Document.GetProperty("revision").GetGuid().Version).IsEqualTo(7);
        await Assert.That(manifest.RevisionHash).IsNotEqualTo(OperatorIdentityManifestJson.RevisionHash(null));
        await Assert.That(() => OperatorIdentityManifestJson.Validate(
            manifest with { RevisionHash = new string('0', 64) })).Throws<OperatorIdentityManifestException>();
        string changed = Encoding.UTF8.GetString(bytes).Replace(
            "Independent Operator", "Changed Operator", StringComparison.Ordinal);
        await Assert.That(() => OperatorIdentityManifestJson.Parse(Encoding.UTF8.GetBytes(changed)))
            .Throws<OperatorIdentityManifestException>();
    }

    [Test]
    public async Task IdentityCanonicalOrderingSnapshotAndValueFreeFailuresSurviveWireRoundTrip()
    {
        OperatorIdentityManifest original = OperatorIdentityManifestJson.Parse(Artifact("operator-identity.json"));
        using JsonDocument reordered = JsonDocument.Parse("{" + string.Join(",",
            original.Document.EnumerateObject().Reverse().Select(property =>
                JsonSerializer.Serialize(property.Name) + ":" + property.Value.GetRawText())) + "}");
        OperatorIdentityManifest copy = OperatorIdentityManifestJson.Create(reordered.RootElement);
        reordered.Dispose();
        await Assert.That(copy.ContentDigest).IsEqualTo(original.ContentDigest);
        await Assert.That(copy.RevisionHash).IsEqualTo(original.RevisionHash);
        await Assert.That(copy.ToString()).DoesNotContain("Independent");
        string json = Encoding.UTF8.GetString(OperatorIdentityManifestJson.Serialize(copy));
        foreach (string invalid in new[]
        {
            "{",
            json.Replace("\"kind\":", "\"unknown\":\"private-sentinel\",\"kind\":", StringComparison.Ordinal),
            json.Replace("\"legalName\":", "\"legalName\":\"private-sentinel\",\"legalName\":", StringComparison.Ordinal),
            json.Replace("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8", "0198e2a4-5340-4f89-8abc-b8bdf43e0ea8", StringComparison.Ordinal)
        })
        {
            OperatorIdentityManifestException? error = await Assert.ThrowsAsync<OperatorIdentityManifestException>(
                () => Task.FromResult(OperatorIdentityManifestJson.Parse(Encoding.UTF8.GetBytes(invalid))));
            await Assert.That(error!.ToString()).DoesNotContain("private-sentinel");
        }
        await Assert.That(() => OperatorIdentityManifestJson.Parse(new byte[OperatorIdentityManifestJson.MaximumBytes + 1]))
            .Throws<OperatorIdentityManifestException>();
    }

    [Test]
    public async Task LegalDraftOutputRetainsScopeLocalesAndSafeMarkdown()
    {
        ConfigurationManifestV1Alpha2 draft = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            Artifact("legal-drafts.json"));
        ConfigurationManifestLegalDocumentV1Alpha2 legal = draft.Spec.Instance.LegalDocuments["TermsOfService"];
        await Assert.That(legal.Kind).IsEqualTo("TermsOfService");
        await Assert.That(legal.Audience).IsEqualTo("Public");
        await Assert.That(legal.LifecycleIntent).IsEqualTo("Draft");
        await Assert.That(legal.ProposedEffectiveAt).IsNull();
        await Assert.That(legal.AccountableIdentityReference).IsNull();
        await Assert.That(legal.Localizations.Select(locale => locale.LanguageTag)).IsEquivalentTo(["en", "fr"]);
        foreach (ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2 locale in legal.Localizations)
            await Assert.That(LegalMarkdownCodec.Render(locale.Markdown, new Dictionary<string, string>()).IsReady).IsTrue();
        foreach (LegalRejectionVector vector in ConfigurationPortabilityExpectedVectors.LegalRejections)
            await Assert.That(() => LegalMarkdownCodec.Inspect(vector.Markdown)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ManifestSmugglingAndOversizedDisplayStillFailAtHostParser()
    {
        string json = Encoding.UTF8.GetString(Artifact("tenant-package.json"));
        foreach (string invalid in new[]
        {
            json.Replace("\"settings\":{", "\"settings\":{\"password\":\"private-sentinel\",", StringComparison.Ordinal),
            json.Replace("\"metadata\":", "\"unexpected\":true,\"metadata\":", StringComparison.Ordinal),
            json.Replace("\"displayName\":\"Community\"", "\"displayName\":\"" + new string('x', 501) + "\"",
                StringComparison.Ordinal)
        })
        {
            ConfigurationPortabilityContractException? error =
                await Assert.ThrowsAsync<ConfigurationPortabilityContractException>(() => Task.FromResult(
                    ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(Encoding.UTF8.GetBytes(invalid))));
            await Assert.That(error!.ToString()).DoesNotContain("private-sentinel");
        }
    }

    [Test]
    public async Task TrustedIssuerNormalizationRemainsOwnedByWire()
    {
        await Assert.That(OidcIssuerAuthority.Normalize("https://identity.example.test/realm/"))
            .IsEqualTo(OidcIssuerAuthority.Normalize("https://identity.example.test/realm"));
        await Assert.That(() => OidcIssuerAuthority.Normalize("https://user:password@example.test"))
            .Throws<ArgumentException>();
    }

    private static byte[] Artifact(string name)
    {
        return File.ReadAllBytes(Path.Combine(FixtureRoot(), "expected", name));
    }

    private static string FixtureRoot() => Path.Combine(Root(), "tests", "Event.Wire.Contracts.UnitTests",
        "Fixtures", "Configurator", "islamu-event");

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "configurator", "definitions")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Event root not found.");
    }
}
