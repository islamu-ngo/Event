namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Security.Cryptography;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using ISLAMU.Wire.Contracts.Identity;

public sealed class ConfiguratorDefinitionArtifactTests
{
    [Test]
    public async Task NativeAndBrowserShareImmutableDefinitionWithoutEmbeddedPrivateAnswers()
    {
        string directory = Path.Combine(Root(), "configurator", "definitions", "islamu-event");
        byte[] nativeBytes = File.ReadAllBytes(Path.Combine(directory, "setup-definition.json"));
        byte[] publicBytes = File.ReadAllBytes(Path.Combine(directory, "public-definition.json"));
        using JsonDocument native = JsonDocument.Parse(nativeBytes);
        using JsonDocument projection = JsonDocument.Parse(publicBytes);
        using JsonDocument release = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "release.json")));
        await Assert.That(native.RootElement.GetProperty("familyId").GetGuid())
            .IsEqualTo(Guid.Parse("a4bcfaa6-9979-43e5-b1f5-00d552618cdf"));
        await Assert.That(projection.RootElement.GetProperty("familyId").GetGuid())
            .IsEqualTo(native.RootElement.GetProperty("familyId").GetGuid());
        await Assert.That(projection.RootElement.GetProperty("revision").GetInt64())
            .IsEqualTo(native.RootElement.GetProperty("revision").GetInt64());
        await Assert.That(release.RootElement.GetProperty("definitionDigest").GetString())
            .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(nativeBytes)));
        await Assert.That(release.RootElement.GetProperty("publicDefinitionDigest").GetString())
            .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(publicBytes)));
        await Assert.That(publicBytes.AsSpan().SequenceEqual(nativeBytes)).IsTrue();
        await Assert.That(native.RootElement.GetProperty("artifacts").GetArrayLength()).IsEqualTo(5);
        await Assert.That(projection.RootElement.GetProperty("fields").GetArrayLength() > 1).IsTrue();
        foreach (JsonElement field in projection.RootElement.GetProperty("fields").EnumerateArray())
        {
            if (field.GetProperty("sensitivity").GetString() is not "public")
            {
                await Assert.That(field.TryGetProperty("default", out _)).IsFalse();
                await Assert.That(field.TryGetProperty("recommendation", out _)).IsFalse();
            }
        }
        await Assert.That(projection.RootElement.TryGetProperty("answers", out _)).IsFalse();
    }

    [Test]
    public async Task ExporterFreshnessReplaysEventMetadataAndHostAcceptance()
    {
        (int code, string output) = await RunExporter("--check",
            Path.GetFullPath(Path.Combine(Root(), "..", "Configurator")));
        await Assert.That(code).IsEqualTo(0).Because(output);
    }

    [Test]
    public async Task ChangedShapeAtReleasedRevisionRejectsBeforePublishingAnyArtifact()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "event-definition-input-" + Guid.NewGuid().ToString("N"));
        string inputs = Path.Combine(temporary, "inputs");
        string destination = Path.Combine(temporary, "output");
        Directory.CreateDirectory(inputs);
        try
        {
            string source = Path.Combine(Root(), "configurator", "definitions", "islamu-event", "inputs");
            foreach (string file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(inputs, Path.GetFileName(file)));
            string manifestPath = Path.Combine(inputs, "configuration-manifest.json");
            JsonNode manifest = JsonNode.Parse(File.ReadAllBytes(manifestPath))!;
            JsonArray tenants = manifest["spec"]!["tenants"]!.AsArray();
            JsonNode extra = tenants[0]!.DeepClone();
            extra["metadata"]!["name"] = "third-community";
            tenants.Add(extra);
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());

            (int code, string output) = await RunExporter("--export", inputs, destination);
            await Assert.That(code).IsEqualTo(1).Because(output);
            await Assert.That(output).Contains("definition-revision-conflict");
            await Assert.That(Directory.Exists(destination)).IsFalse();
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Test]
    public async Task NewRevisionCanDeclareBoundedCanonicalIssuerFieldsWithoutPrivateDefaults()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "event-definition-issuers-" + Guid.NewGuid().ToString("N"));
        string inputs = Path.Combine(temporary, "inputs");
        string destination = Path.Combine(temporary, "output");
        Directory.CreateDirectory(inputs);
        try
        {
            string source = Path.Combine(Root(), "configurator", "definitions", "islamu-event", "inputs");
            foreach (string file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(inputs, Path.GetFileName(file)));
            string profilePath = Path.Combine(inputs, "profile.json");
            JsonNode profile = JsonNode.Parse(File.ReadAllBytes(profilePath))!;
            profile["revision"] = profile["revision"]!.GetValue<long>() + 1;
            profile["trustedIssuerCount"] = 2;
            await File.WriteAllTextAsync(profilePath, profile.ToJsonString());

            (int code, string output) = await RunExporter("--export", inputs, destination);
            await Assert.That(code).IsEqualTo(0).Because(output);
            using JsonDocument definition = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(destination, "setup-definition.json")));
            JsonElement issuer = definition.RootElement.GetProperty("fields").EnumerateArray()
                .Single(field => field.GetProperty("id").GetString() == "env.IDENTITYCORRELATION__TRUSTEDISSUERS__1");
            await Assert.That(issuer.GetProperty("sensitivity").GetString()).IsEqualTo("public");
            await Assert.That(issuer.TryGetProperty("default", out _)).IsFalse();
            profile["trustedIssuerCount"] = 129;
            await File.WriteAllTextAsync(profilePath, profile.ToJsonString());
            (code, output) = await RunExporter("--export", inputs, Path.Combine(temporary, "oversized"));
            await Assert.That(code).IsEqualTo(1).Because(output);
            await Assert.That(Directory.Exists(Path.Combine(temporary, "oversized"))).IsFalse();
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
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
    public async Task IdentityCanonicalOrderingSnapshotAndValueFreeFailuresSurviveHandoff()
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

    [Test]
    public async Task HostVerifierRejectsLegalAuthorityScopeProvenanceAndDuplicateLocales()
    {
        JsonObject source = JsonNode.Parse(Artifact("legal-drafts.json"))!.AsObject();
        foreach (string mutation in new[] { "scope", "audience", "published", "effective", "identity", "template", "duplicate", "markdown" })
        {
            JsonObject changed = (JsonObject)source.DeepClone();
            JsonObject documents = changed["spec"]!["instance"]!["legalDocuments"]!.AsObject();
            JsonObject legal = documents["TermsOfService"]!.AsObject();
            switch (mutation)
            {
                case "scope":
                    legal["kind"] = "TenantTerms";
                    documents.Remove("TermsOfService");
                    documents["TenantTerms"] = legal;
                    break;
                case "audience": legal["audience"] = "UnknownAudience"; break;
                case "published": legal["lifecycleIntent"] = "Published"; break;
                case "effective": legal["proposedEffectiveAt"] = "2030-01-01T00:00:00Z"; break;
                case "identity": legal["accountableIdentityReference"] = "private-sentinel"; break;
                case "template":
                    legal["templateProvenance"] = new JsonObject
                    {
                        ["sourceKind"] = "Unreviewed", ["templateId"] = "private-sentinel", ["templateVersion"] = "1",
                        ["licenseExpression"] = "MIT", ["reviewReference"] = "example-review"
                    };
                    break;
                case "duplicate":
                    legal["localizations"]![1]!["languageTag"] = "EN";
                    break;
                case "markdown": legal["localizations"]![0]!["markdown"] = "<script>private-sentinel</script>"; break;
            }
            (int code, string output) = await Verify("legal-drafts", Encoding.UTF8.GetBytes(changed.ToJsonString()));
            await Assert.That(code).IsEqualTo(1).Because(mutation);
            await Assert.That(output).DoesNotContain("private-sentinel");
        }
        await Assert.That((await Verify("legal-drafts", Artifact("legal-drafts.json"))).Code).IsEqualTo(0);
    }

    [Test]
    public async Task HostVerifierPreservesAllTwentyNineLegalKindScopeClassifications()
    {
        string[] instanceKinds =
        [
            "TermsOfService", "PrivacyNotice", "CookiePolicy", "AcceptableUsePolicy", "CommunityGuidelines",
            "ModerationReportingAppealPolicy", "AccessibilityStatement", "LegalNotice", "SecurityDisclosurePolicy",
            "RetentionErasurePortabilityNotice", "SubprocessorNotice", "OpenSourceAttribution", "ApiDeveloperTerms",
            "FederationNotice", "PaymentResponsibilities", "SupportAvailabilityEolMigrationNotice"
        ];
        string[] tenantKinds =
        [
            "TenantTerms", "TenantPrivacyNotice", "TenantCodeOfConduct", "OrganizerSubmissionTerms",
            "EventPublicationModerationPolicy", "CancellationRefundPolicy", "RegistrationParticipantPrivacyNotice",
            "MediaPhotographyNotice", "SafeguardingMinorParticipationPolicy", "VenueAccessibilityPolicy",
            "ComplaintCorrectionCopyrightNotice", "SponsorshipPartnerDisclosure", "TenantRetentionContactSharingNotice"
        ];
        foreach (string kind in instanceKinds.Concat(tenantKinds))
        {
            bool tenant = tenantKinds.Contains(kind, StringComparer.Ordinal);
            JsonObject source = JsonNode.Parse(Artifact(tenant ? "tenant-package.json" : "legal-drafts.json"))!.AsObject();
            JsonObject documents = (tenant ? source["spec"]!["legalDocuments"]!
                : source["spec"]!["instance"]!["legalDocuments"]!).AsObject();
            JsonNode document = documents.First().Value!.DeepClone();
            documents.Clear();
            document["kind"] = kind;
            documents[kind] = document;
            await Assert.That((await Verify(tenant ? "tenant-package" : "legal-drafts",
                Encoding.UTF8.GetBytes(source.ToJsonString()))).Code).IsEqualTo(0).Because(kind);
        }
    }

    [Test]
    public async Task HostVerifierRejectsDuplicateIssuerAuthoritiesAndNoncanonicalIndices()
    {
        foreach (string invalid in new[]
        {
            "IDENTITYCORRELATION__TRUSTEDISSUERS__00=https://identity.example.test\n",
            "IDENTITYCORRELATION__TRUSTEDISSUERS__-1=https://identity.example.test\n",
            "IDENTITYCORRELATION__TRUSTEDISSUERS__0=https://identity.example.test/realm/\n"
                + "IDENTITYCORRELATION__TRUSTEDISSUERS__1=https://identity.example.test/realm\n"
        })
            await Assert.That((await Verify("environment", Encoding.UTF8.GetBytes(invalid))).Code).IsEqualTo(1);
        await Assert.That((await Verify("environment", Encoding.UTF8.GetBytes(
            "IDENTITYCORRELATION__TRUSTEDISSUERS__0=https://identity.example.test/one\n"
            + "IDENTITYCORRELATION__TRUSTEDISSUERS__1=https://identity.example.test/two\n"))).Code).IsEqualTo(0);
    }

    private static async Task<(int Code, string Output)> Verify(string artifact, byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), "event-definition-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            return await RunExporter("--verify", artifact, path);
        }
        finally { File.Delete(path); }
    }

    private static async Task<(int Code, string Output)> RunExporter(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(Root(), "eng", "configurator", "DefinitionExporter",
            "bin", "Release", "net10.0", "DefinitionExporter.dll"));
        foreach (string argument in args)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Exporter did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await errors);
    }

    private static byte[] Artifact(string name)
    {
        string? actual = Environment.GetEnvironmentVariable("EVENT_CONFIGURATOR_ARTIFACT_DIRECTORY");
        if (actual is not null)
            return File.ReadAllBytes(Path.Combine(actual, name));
        return File.ReadAllBytes(Path.Combine(Root(),
            "configurator", "definitions", "islamu-event", "expected", name));
    }

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "configurator", "definitions")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Event root not found.");
    }
}
