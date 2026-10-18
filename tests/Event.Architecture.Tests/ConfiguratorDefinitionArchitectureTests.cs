namespace Event.Architecture.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Explore.Application.Configuration;
using Explore.Application.Features.ConfigurationManifest.Validation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfiguratorDefinitionArchitectureTests
{
    [Test]
    public async Task DefinitionReceiptBindsEventOwnedMetadataAndCanonicalBytes()
    {
        string directory = ContextSystemHelpers.RepoPath(
            "configurator", "definitions", "islamu-event");
        using JsonDocument receipt = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(directory, "release.json")));
        using JsonDocument definition = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(directory, "definition.json")));
        JsonElement identity = definition.RootElement;
        JsonElement release = receipt.RootElement;

        await Assert.That(release.GetProperty("familyId").GetString())
            .IsEqualTo(identity.GetProperty("familyId").GetString());
        await Assert.That(release.GetProperty("revision").GetInt64())
            .IsEqualTo(identity.GetProperty("revision").GetInt64());
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "definition.json"));
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await Assert.That(digest).IsEqualTo(release.GetProperty("definitionDigest").GetString());
    }

    [Test]
    public async Task HostValidatorRejectsOrdinalDuplicateTenants()
    {
        ConfigurationManifestV1Alpha2 manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
            Artifact("configuration-manifest-1-tenants.json"));
        await Assert.That(ConfigurationManifestValidator.Validate(manifest).IsValid).IsTrue();
        ConfigurationManifestValidationResult result = ConfigurationManifestValidator.Validate(
            manifest with { Spec = manifest.Spec with { Tenants = [manifest.Spec.Tenants[0], manifest.Spec.Tenants[0]] } });
        await Assert.That(result.Errors.Any(error => error.Code == ConfigurationManifestFailureCodes.TenantDuplicate))
            .IsTrue();
    }

    [Test]
    public async Task HostValidatorRejectsInvalidLegalScopeAuthorityProvenanceAndLocales()
    {
        JsonObject source = JsonNode.Parse(Artifact("legal-drafts.json"))!.AsObject();
        source["spec"]!["tenants"] = JsonNode.Parse(Artifact("configuration-manifest-1-tenants.json"))!
            ["spec"]!["tenants"]!.DeepClone();
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
                case "effective": legal["proposedEffectiveAt"] = "2030-01-01T00:00:00+01:00"; break;
                case "identity": legal["accountableIdentityReference"] = new string('x', 201); break;
                case "template":
                    legal["templateProvenance"] = new JsonObject
                    {
                        ["sourceKind"] = "Unreviewed",
                        ["templateId"] = "private-sentinel",
                        ["templateVersion"] = "1",
                        ["licenseExpression"] = "MIT",
                        ["reviewReference"] = "example-review"
                    };
                    break;
                case "duplicate":
                    legal["localizations"]![1]!["languageTag"] = "EN";
                    break;
                case "markdown": legal["localizations"]![0]!["markdown"] = "<script>private-sentinel</script>"; break;
            }
            ConfigurationManifestV1Alpha2 manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
                Encoding.UTF8.GetBytes(changed.ToJsonString()));
            ConfigurationManifestValidationResult result = ConfigurationManifestValidator.Validate(manifest);
            await Assert.That(result.Errors.Any(error => error.Code == ConfigurationManifestFailureCodes.LegalDocumentInvalid))
                .IsTrue().Because(mutation);
            await Assert.That(string.Join("\n", result.Errors)).DoesNotContain("private-sentinel");
        }
        await Assert.That(ConfigurationManifestValidator.Validate(
            ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(
                Encoding.UTF8.GetBytes(source.ToJsonString()))).IsValid).IsTrue();
    }

    [Test]
    public async Task HostValidatorPreservesAllTwentyNineLegalKindScopeClassifications()
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
            foreach (bool tenantScope in new[] { false, true })
            {
                JsonObject source = JsonNode.Parse(Artifact(tenantScope ? "tenant-package.json" : "legal-drafts.json"))!.AsObject();
                if (!tenantScope)
                    source["spec"]!["tenants"] = JsonNode.Parse(Artifact("configuration-manifest-1-tenants.json"))!
                        ["spec"]!["tenants"]!.DeepClone();
                JsonObject documents = (tenantScope ? source["spec"]!["legalDocuments"]!
                    : source["spec"]!["instance"]!["legalDocuments"]!).AsObject();
                JsonNode document = documents.First().Value!.DeepClone();
                documents.Clear();
                document["kind"] = kind;
                documents[kind] = document;
                byte[] bytes = Encoding.UTF8.GetBytes(source.ToJsonString());
                ConfigurationManifestValidationResult result = tenantScope
                    ? ConfigurationManifestValidator.Validate(ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(bytes))
                    : ConfigurationManifestValidator.Validate(ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(bytes));
                if (tenantScope == tenant)
                    await Assert.That(result.IsValid).IsTrue().Because(kind);
                else
                    await Assert.That(result.Errors.Any(error => error.Code == ConfigurationManifestFailureCodes.LegalDocumentInvalid))
                        .IsTrue().Because(kind);
            }
        }
    }

    [Test]
    public async Task HostOptionsRejectDuplicateNormalizedIssuerAuthorities()
    {
        await Assert.That(IdentityCorrelationOptions.IsValid(new IdentityCorrelationOptions
        {
            TrustedIssuers = ["https://identity.example.test/realm/", "https://identity.example.test/realm"]
        })).IsFalse();
        await Assert.That(IdentityCorrelationOptions.IsValid(new IdentityCorrelationOptions
        {
            TrustedIssuers = ["https://identity.example.test/one", "https://identity.example.test/two"]
        })).IsTrue();
    }

    private static byte[] Artifact(string name)
    {
        return File.ReadAllBytes(ContextSystemHelpers.RepoPath(
            "tests", "Event.Wire.Contracts.UnitTests", "Fixtures", "Configurator",
            "islamu-event", "expected", name));
    }

    [Test]
    public async Task EventClientOwnsSetupLiveTransportWithoutSetupEngineDependency()
    {
        var adapter = typeof(Explore.Blazor.Client.Services.SetupLive.SetupLiveAccessToken);
        await Assert.That(adapter.Assembly.GetName().Name).IsEqualTo("Explore.Blazor.Client");
        XDocument client = XDocument.Load(ContextSystemHelpers.RepoPath(
            "src", "Explore.Blazor.Client", "Explore.Blazor.Client.csproj"));
        string[] forbidden = client.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(path => path.Contains("Event.Setup", StringComparison.Ordinal)
                || path.Contains("Configurator.", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(forbidden).IsEmpty();
    }
}
