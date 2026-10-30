namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class SetupContractExtractionTests
{
    [Test]
    public async Task FinalPackageFreeOwnersAndDeepCodecSurfaceExist()
    {
        var wireAssembly = typeof(ConfigurationManifestV1Alpha2).Assembly;
        await Assert.That(typeof(ConfigurationPortabilityJsonCodec).Assembly).IsEqualTo(wireAssembly);
        await Assert.That(typeof(LegalMarkdownCodec).Assembly).IsEqualTo(wireAssembly);
        await Assert.That(wireAssembly.GetReferencedAssemblies()
                .Where(reference => reference.Name is not null
                    && !reference.Name.StartsWith("System", StringComparison.Ordinal)))
            .IsEmpty().Because("Portable codecs must run without an application host or third-party packages.");
    }

    [Test]
    public async Task IndependentGoldenAndBreakerVectorsAreDecisionComplete()
    {
        await Assert.That(ConfigurationPortabilityExpectedVectors.ManifestBytes.Length)
            .IsEqualTo(448);
        await Assert.That(ConfigurationPortabilityExpectedVectors.PackageBytes.Length)
            .IsEqualTo(377);
        await Assert.That(Sha256(ConfigurationPortabilityExpectedVectors.ManifestBytes))
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.ManifestSha256);
        await Assert.That(Sha256(ConfigurationPortabilityExpectedVectors.PackageBytes))
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.PackageSha256);
        await Assert.That(ConfigurationPortabilityExpectedVectors.InvalidArtifacts.Count)
            .IsEqualTo(9);
        await Assert.That(ConfigurationPortabilityExpectedVectors.SmugglingMembers.Count)
            .IsEqualTo(15);
        await Assert.That(ConfigurationPortabilityExpectedVectors.LegalRejections.Count)
            .IsEqualTo(7);

        string[] schemaFacts =
        [
            ConfigurationPortabilityExpectedVectors.ManifestSchemaId,
            ConfigurationPortabilityExpectedVectors.PackageSchemaId,
            ConfigurationPortabilityExpectedVectors.ApiVersion,
            ConfigurationPortabilityExpectedVectors.MaximumArtifactUtf8Bytes.ToString(CultureInfo.InvariantCulture),
            ConfigurationPortabilityExpectedVectors.MaximumTenantCount.ToString(CultureInfo.InvariantCulture),
            ConfigurationPortabilityExpectedVectors.MaximumLegalMarkdownUtf8BytesPerLocale.ToString(CultureInfo.InvariantCulture)
        ];
        await Assert.That(schemaFacts).IsEquivalentTo(
        [
            "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
            "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json",
            "configuration.islamu.org/v1alpha2",
            "4194304", "256", "262144"
        ]);

        IReadOnlyList<string> syntheticAuthorityFailures =
            ConfigurationPortabilityInvariantVerifier.FindForbiddenPublicMembers(
                [typeof(SyntheticForbiddenPortableRecord)]);
        IReadOnlyList<string> syntheticLeakFailures =
            ConfigurationPortabilityInvariantVerifier.FindValueLeaks(
                [new SyntheticLeakingDiagnostic("bad", "$", "sentinel-sensitive-value")],
                "sentinel-sensitive-value");

        await Assert.That(syntheticAuthorityFailures).IsNotEmpty();
        await Assert.That(syntheticLeakFailures).IsNotEmpty();
    }

    [Test]
    public async Task DeterministicJsonBytesDigestRecordsAndCollectionsAreStable()
    {
        byte[] manifestInput = ConfigurationPortabilityExpectedVectors.ManifestBytes;
        byte[] packageInput = ConfigurationPortabilityExpectedVectors.PackageBytes;
        ConfigurationManifestV1Alpha2 manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(manifestInput);
        TenantConfigurationPackageV1Alpha2 package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(packageInput);
        byte[] manifestBytes = ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest);
        byte[] packageBytes = ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package);

        await Assert.That(manifestBytes)
            .IsEquivalentTo(ConfigurationPortabilityExpectedVectors.ManifestBytes);
        await Assert.That(packageBytes)
            .IsEquivalentTo(ConfigurationPortabilityExpectedVectors.PackageBytes);
        await Assert.That(Sha256(manifestBytes))
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.ManifestSha256);
        await Assert.That(Sha256(packageBytes))
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.PackageSha256);

        manifestInput.AsSpan().Fill((byte)'x');
        packageInput.AsSpan().Fill((byte)'x');
        await Assert.That(ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest))
            .IsEquivalentTo(manifestBytes);
        await Assert.That(ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package))
            .IsEquivalentTo(packageBytes);

        await Assert.That(manifest with { }).IsEqualTo(manifest);
        await Assert.That(manifest with { Schema = "different-schema" }).IsNotEqualTo(manifest);
        await Assert.That(package with { }).IsEqualTo(package);
        await Assert.That(package with { Schema = "different-schema" }).IsNotEqualTo(package);
        await AssertSettingsRejectMutation(manifest.Spec.Instance.Settings);
        await Assert.That(() => ((IList<ConfigurationManifestTenantV1Alpha2>)manifest.Spec.Tenants).Clear())
            .Throws<NotSupportedException>();
        await AssertCallerOwnedDictionaryIsSnapshotted();

        ConfigurationManifestV1Alpha2 reconstructed = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(manifestBytes);
        await Assert.That(ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(reconstructed))
            .IsEquivalentTo(manifestBytes);
        TenantConfigurationPackageV1Alpha2 reconstructedPackage = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(packageBytes);
        await Assert.That(ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(reconstructedPackage))
            .IsEquivalentTo(packageBytes);
    }

    [Test]
    public async Task StrictCodecRejectsMalformedBoundedSmuggledAndWrongScopeArtifactsSafely()
    {
        foreach (InvalidArtifactVector vector in ConfigurationPortabilityExpectedVectors.InvalidArtifacts)
        {
            ConfigurationPortabilityContractException failure = ParseManifestFailure(Encoding.UTF8.GetBytes(vector.Json));
            await AssertPortableFailure(failure, vector.Code, vector.Path, vector.Name);
        }

        string overDepth = ConfigurationPortabilityExpectedVectors.ManifestJson.Replace(
            "true",
            new string('[', ConfigurationPortabilityExpectedVectors.MaximumJsonDepth + 1)
                + "true"
                + new string(']', ConfigurationPortabilityExpectedVectors.MaximumJsonDepth + 1),
            StringComparison.Ordinal);
        await AssertPortableFailure(
            ParseManifestFailure(Encoding.UTF8.GetBytes(overDepth)),
            ConfigurationPortabilityExpectedVectors.DepthExceeded,
            "$",
            "depth-limit");

        byte[] overBytes = new byte[
            ConfigurationPortabilityExpectedVectors.MaximumArtifactUtf8Bytes + 1];
        await AssertPortableFailure(
            ParseManifestFailure(overBytes),
            ConfigurationPortabilityExpectedVectors.TooLarge,
            "$",
            "byte-limit");

        const string tenant =
            "{\"metadata\":{\"name\":\"tenant\"},\"spec\":{\"displayName\":\"Community\",\"settings\":{},\"documents\":{},\"legalDocuments\":{}}}";
        string tooManyTenants = ConfigurationPortabilityExpectedVectors.ManifestJson.Replace(
            "[{\"metadata\":{\"name\":\"default\"},\"spec\":{\"displayName\":\"Primary Community\",\"settings\":{},\"documents\":{},\"legalDocuments\":{}}}]",
            "[" + string.Join(',', Enumerable.Repeat(tenant, ConfigurationPortabilityExpectedVectors.MaximumTenantCount + 1)) + "]",
            StringComparison.Ordinal);
        await AssertPortableFailure(
            ParseManifestFailure(Encoding.UTF8.GetBytes(tooManyTenants)),
            ConfigurationPortabilityExpectedVectors.CountExceeded,
            "$.spec.tenants",
            "tenant-count");

        const string sentinel = "sentinel-sensitive-value";
        foreach (string member in ConfigurationPortabilityExpectedVectors.SmugglingMembers)
        {
            string replacement =
                "\"settings\":{\"" + member + "\":\"" + sentinel
                + "\"},\"documents\":{},\"legalDocuments\":{}}}]";
            string smuggled = ConfigurationPortabilityExpectedVectors.ManifestJson.Replace(
                "\"settings\":{},\"documents\":{},\"legalDocuments\":{}}}]",
                replacement,
                StringComparison.Ordinal);
            ConfigurationPortabilityContractException failure = ParseManifestFailure(Encoding.UTF8.GetBytes(smuggled));
            await AssertPortableFailure(
                failure,
                ConfigurationPortabilityExpectedVectors.ForbiddenMember,
                $"$.spec.tenants[0].spec.settings.{member}",
                member,
                sentinel);
        }
    }

    [Test]
    public async Task PublicLimitsMetadataAndPortableClosureMatchCheckedSchemasWithoutAuthority()
    {
        await Assert.That(ConfigurationManifestContractMetadata.SchemaId)
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.ManifestSchemaId);
        await Assert.That(TenantConfigurationPackageContractMetadata.SchemaId)
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.PackageSchemaId);
        await Assert.That(ConfigurationManifestContractMetadata.ApiVersion)
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.ApiVersion);
        await Assert.That(ConfigurationManifestContractMetadata.Kind)
            .IsEqualTo("ConfigurationManifest");
        await Assert.That(TenantConfigurationPackageContractMetadata.Kind)
            .IsEqualTo("TenantConfigurationPackage");
        await Assert.That(ConfigurationManifestContractMetadata.MediaType)
            .IsEqualTo("application/vnd.islamu.configuration-manifest.v1alpha2+json");
        await Assert.That(TenantConfigurationPackageContractMetadata.MediaType)
            .IsEqualTo("application/vnd.islamu.tenant-configuration-package.v1alpha2+json");
        await Assert.That(Enum.GetNames<ConfigurationImportApplyMode>())
            .IsEquivalentTo(
            [
                "PreviewOnly", "CreateNew", "MergeMissing", "ApplySelected",
                "ReplacePortableConfiguration", "ReconcileManaged"
            ]);

        await Assert.That(ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumArtifactUtf8Bytes);
        await Assert.That(ConfigurationPortabilityContentLimits.MaximumJsonDepth).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumJsonDepth);
        await Assert.That(ConfigurationPortabilityContentLimits.MaximumTenantCount).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumTenantCount);
        await Assert.That(LegalMarkdownContentLimits.MaximumDocumentsPerScope).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalDocumentsPerScope);
        await Assert.That(LegalMarkdownContentLimits.MaximumLocalesPerDocument).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalLocalesPerDocument);
        await Assert.That(LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalMarkdownUtf8BytesPerLocale);
        await Assert.That(LegalMarkdownContentLimits.MaximumLinksPerLocale).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalLinksPerLocale);
        await Assert.That(LegalMarkdownContentLimits.MaximumPlaceholdersPerLocale).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalPlaceholdersPerLocale);
        await Assert.That(LegalMarkdownContentLimits.MaximumTitleLength).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalTitleLength);
        await Assert.That(LegalMarkdownContentLimits.MaximumSummaryLength).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalSummaryLength);
        await Assert.That(LegalMarkdownContentLimits.MaximumLanguageTagLength).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLanguageTagLength);
        await Assert.That(LegalMarkdownContentLimits.MaximumLinkLength).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalLinkLength);
        await Assert.That(LegalMarkdownContentLimits.MaximumIdentityValueLength).IsEqualTo(ConfigurationPortabilityExpectedVectors.MaximumLegalIdentityValueLength);

        Type[] portableRoots =
        [
            typeof(ConfigurationManifestV1Alpha2),
            typeof(TenantConfigurationPackageV1Alpha2),
            typeof(ConfigurationManifestBrandingPayloadV1Alpha2),
            typeof(ConfigurationManifestPaidEventPolicyPayloadV1Alpha2),
            typeof(ConfigurationPortabilityDiagnostic),
            typeof(LegalMarkdownDiagnostic)
        ];
        IReadOnlyList<string> forbidden =
            ConfigurationPortabilityInvariantVerifier.FindForbiddenPublicMembers(portableRoots);
        await Assert.That(forbidden).IsEmpty().Because(
            "Portable records and diagnostics cannot carry secret, PII, provider, target, topology, operational, or acceptance authority: "
            + string.Join(", ", forbidden));
    }

    [Test]
    public async Task LegalMarkdownNormalizesAndRendersDeterministicallyWhileFailingClosed()
    {
        string normalized = LegalMarkdownCodec.Normalize(
            ConfigurationPortabilityExpectedVectors.LegalMarkdown.Replace("\n", "\r\n", StringComparison.Ordinal));
        await Assert.That(normalized).IsEqualTo(ConfigurationPortabilityExpectedVectors.LegalMarkdown);
        await Assert.That(LegalMarkdownCodec.Normalize(normalized)).IsEqualTo(normalized);

        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        LegalMarkdownRenderResult rendered;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            rendered = LegalMarkdownCodec.Render(
                normalized,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        await Assert.That(rendered.IsReady).IsTrue();
        await Assert.That(rendered.Html)
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.LegalHtml);
        LegalMarkdownInspection inspection = LegalMarkdownCodec.Inspect(normalized);
        LegalMarkdownInspection reparsed = LegalMarkdownCodec.Inspect(LegalMarkdownCodec.Normalize(normalized));
        await Assert.That(inspection.LinkCount).IsEqualTo(1);
        await Assert.That(inspection.LinkCount).IsEqualTo(reparsed.LinkCount);
        await Assert.That(inspection.PlaceholderCount).IsEqualTo(0);
        await Assert.That(inspection.LinkTargets).IsEquivalentTo(["https://example.test/legal"]);
        await Assert.That(rendered.LinkTargets).IsEquivalentTo(inspection.LinkTargets);
        await Assert.That(rendered.Diagnostics).IsEmpty();

        foreach (LegalRejectionVector vector in ConfigurationPortabilityExpectedVectors.LegalRejections)
        {
            LegalMarkdownContractException failure = InspectLegalFailure(vector.Markdown);
            await Assert.That(failure.ToString().Length).IsLessThanOrEqualTo(1024);
        }

        string tooManyLinks = string.Join('\n', Enumerable.Range(
                0,
                ConfigurationPortabilityExpectedVectors.MaximumLegalLinksPerLocale + 1)
            .Select(index => $"[policy {index}](https://example.test/{index})"));
        string tooManyPlaceholders = string.Join(' ', Enumerable.Range(
                0,
                ConfigurationPortabilityExpectedVectors.MaximumLegalPlaceholdersPerLocale + 1)
            .Select(index => $"{{{{identity_{index}}}}}"));
        string tooLarge = new('a', ConfigurationPortabilityExpectedVectors.MaximumLegalMarkdownUtf8BytesPerLocale + 1);
        foreach (string invalid in new[] { tooManyLinks, tooManyPlaceholders, tooLarge })
            await Assert.That(() => LegalMarkdownCodec.Inspect(invalid)).Throws<ArgumentOutOfRangeException>();

        const string unresolvedValue = "sentinel-identity-value";
        LegalMarkdownRenderResult unresolved = LegalMarkdownCodec.Render(
            "# Policy\n\nOperator: {{accountable_identity}}. Contact: {{contact_identity}}.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contact_identity"] = unresolvedValue
            });
        await Assert.That(unresolved.IsReady).IsFalse();
        await Assert.That(unresolved.Html).IsEmpty();
        LegalMarkdownDiagnostic unresolvedDiagnostic = unresolved.Diagnostics.Single();
        await Assert.That(unresolvedDiagnostic.Code).IsEqualTo("legal_markdown_identity_unresolved");
        await Assert.That(unresolvedDiagnostic.Subject).IsEqualTo("accountable_identity");
        IReadOnlyList<string> leaks = ConfigurationPortabilityInvariantVerifier.FindValueLeaks(
            unresolved.Diagnostics,
            unresolvedValue);
        await Assert.That(leaks).IsEmpty();

        LegalMarkdownRenderResult weakLink = LegalMarkdownCodec.Render(
            "# Policy\n\n[click here](https://example.test/legal)",
            new Dictionary<string, string>(StringComparer.Ordinal));
        LegalMarkdownDiagnostic weakDiagnostic = weakLink.Diagnostics.Single();
        await Assert.That(weakDiagnostic.Code)
            .IsEqualTo("legal_markdown_link_text_weak");
        await Assert.That(weakDiagnostic.Subject).IsNull();
    }

    private static async Task AssertPortableFailure(
        ConfigurationPortabilityContractException failure,
        string code,
        string path,
        string vector,
        string? sentinel = null)
    {
        await Assert.That(failure.Code)
            .IsEqualTo(code).Because(vector);
        await Assert.That(failure.Path)
            .IsEqualTo(path).Because(vector);
        await Assert.That(failure.Code.Length).IsLessThanOrEqualTo(80);
        await Assert.That(failure.Path.Length).IsLessThanOrEqualTo(256);
        await Assert.That(failure.ToString().Length).IsLessThanOrEqualTo(1024);
        if (sentinel is not null)
            await Assert.That(failure.ToString().Contains(sentinel, StringComparison.Ordinal)).IsFalse();
    }

    private static ConfigurationPortabilityContractException ParseManifestFailure(byte[] bytes)
    {
        try
        {
            _ = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(bytes);
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Invalid manifest vector was accepted.");
    }

    private static LegalMarkdownContractException InspectLegalFailure(string markdown)
    {
        try
        {
            _ = LegalMarkdownCodec.Inspect(markdown);
        }
        catch (LegalMarkdownContractException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Invalid legal Markdown vector was accepted.");
    }

    private static async Task AssertCallerOwnedDictionaryIsSnapshotted()
    {
        using JsonDocument enabled = JsonDocument.Parse("true");
        using JsonDocument disabled = JsonDocument.Parse("false");
        var callerSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["events.require_approval"] = enabled.RootElement.Clone()
        };
        var instance = new ConfigurationManifestInstanceV1Alpha2
        {
            Settings = callerSettings,
            Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(),
            LegalDocuments = new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>()
        };
        callerSettings["events.require_approval"] = disabled.RootElement.Clone();
        callerSettings["events.user_submission_enabled"] =
            disabled.RootElement.Clone();

        await Assert.That(instance.Settings.Count).IsEqualTo(1);
        await Assert.That(instance.Settings["events.require_approval"].GetBoolean()).IsTrue();
        await AssertSettingsRejectMutation(instance.Settings);
    }

    private static async Task AssertSettingsRejectMutation(IReadOnlyDictionary<string, JsonElement> settings) =>
        await Assert.That(() => ((IDictionary<string, JsonElement>)settings).Add("mutation", default))
            .Throws<NotSupportedException>();

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
