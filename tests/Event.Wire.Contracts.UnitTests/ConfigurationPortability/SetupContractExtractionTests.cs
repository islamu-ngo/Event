namespace ISLAMU.Wire.Contracts.UnitTests.ConfigurationPortability;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class SetupContractExtractionTests
{
    private readonly ConfigurationPortabilityContractRuntime _runtime = new();

    [Test]
    public async Task FinalPackageFreeOwnersAndDeepCodecSurfaceExist()
    {
        IReadOnlyList<string> missing = _runtime.MissingOwners();

        await Assert.That(missing.Count).IsEqualTo(0).Because(
            "SA-210 Red: final Event.Wire.Contracts owners are missing while old owners remain at "
            + "ISLAMU.Wire.Contracts.ConfigurationPortability and "
            + "Explore.Domain.ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownCodec. Missing count="
            + missing.Count.ToString(CultureInfo.InvariantCulture)
            + "; first missing owners: "
            + string.Join(", ", missing.Take(6)));
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
    public async Task CanonicalJsonBytesDigestRecordsAndCollectionsAreStable()
    {
        if (!_runtime.IsComplete)
            return;

        byte[] manifestInput = ConfigurationPortabilityExpectedVectors.ManifestBytes;
        byte[] packageInput = ConfigurationPortabilityExpectedVectors.PackageBytes;
        object manifest = _runtime.ParseManifest(manifestInput);
        object package = _runtime.ParsePackage(packageInput);
        byte[] manifestBytes = _runtime.SerializeManifest(manifest);
        byte[] packageBytes = _runtime.SerializePackage(package);

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
        await Assert.That(_runtime.SerializeManifest(manifest))
            .IsEquivalentTo(manifestBytes);
        await Assert.That(_runtime.SerializePackage(package))
            .IsEquivalentTo(packageBytes);

        object manifestClone = CloneRecord(manifest);
        await Assert.That(manifestClone.Equals(manifest)).IsTrue();
        PropertyInfo schema = manifestClone.GetType().GetProperty("Schema")!;
        schema.SetValue(manifestClone, "different-schema");
        await Assert.That(manifestClone.Equals(manifest)).IsFalse();

        object spec = ConfigurationPortabilityContractRuntime.Property(manifest, "Spec")!;
        object instance = ConfigurationPortabilityContractRuntime.Property(spec, "Instance")!;
        object settings = ConfigurationPortabilityContractRuntime.Property(instance, "Settings")!;
        object tenants = ConfigurationPortabilityContractRuntime.Property(spec, "Tenants")!;
        await AssertCollectionRejectsMutation(settings);
        await AssertCollectionRejectsMutation(tenants);
        await AssertCallerOwnedDictionaryIsSnapshotted();

        object reconstructed = _runtime.ParseManifest(manifestBytes);
        await Assert.That(_runtime.SerializeManifest(reconstructed))
            .IsEquivalentTo(manifestBytes);
    }

    [Test]
    public async Task StrictCodecRejectsMalformedBoundedSmuggledAndWrongScopeArtifactsSafely()
    {
        if (!_runtime.IsComplete)
            return;

        foreach (InvalidArtifactVector vector in ConfigurationPortabilityExpectedVectors.InvalidArtifacts)
        {
            Exception failure = _runtime.ParseManifestFailure(Encoding.UTF8.GetBytes(vector.Json));
            await AssertPortableFailure(failure, vector.Code, vector.Path, vector.Name);
        }

        string overDepth = ConfigurationPortabilityExpectedVectors.ManifestJson.Replace(
            "true",
            new string('[', ConfigurationPortabilityExpectedVectors.MaximumJsonDepth + 1)
                + "true"
                + new string(']', ConfigurationPortabilityExpectedVectors.MaximumJsonDepth + 1),
            StringComparison.Ordinal);
        await AssertPortableFailure(
            _runtime.ParseManifestFailure(Encoding.UTF8.GetBytes(overDepth)),
            ConfigurationPortabilityExpectedVectors.DepthExceeded,
            "$",
            "depth-limit");

        byte[] overBytes = new byte[
            ConfigurationPortabilityExpectedVectors.MaximumArtifactUtf8Bytes + 1];
        await AssertPortableFailure(
            _runtime.ParseManifestFailure(overBytes),
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
            _runtime.ParseManifestFailure(Encoding.UTF8.GetBytes(tooManyTenants)),
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
            Exception failure = _runtime.ParseManifestFailure(Encoding.UTF8.GetBytes(smuggled));
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
        if (!_runtime.IsComplete)
            return;

        Type manifestMetadata = _runtime.RequireType("ConfigurationManifestContractMetadata");
        Type packageMetadata = _runtime.RequireType("TenantConfigurationPackageContractMetadata");
        Type limits = _runtime.RequireType("ConfigurationPortabilityContentLimits");
        Type legalLimits = _runtime.RequireType("LegalMarkdownContentLimits");

        await Assert.That(ConfigurationPortabilityContractRuntime.StringConstant(manifestMetadata, "SchemaId"))
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.ManifestSchemaId);
        await Assert.That(ConfigurationPortabilityContractRuntime.StringConstant(packageMetadata, "SchemaId"))
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.PackageSchemaId);
        await Assert.That(ConfigurationPortabilityContractRuntime.StringConstant(manifestMetadata, "ApiVersion"))
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.ApiVersion);
        await Assert.That(ConfigurationPortabilityContractRuntime.StringConstant(manifestMetadata, "Kind"))
            .IsEqualTo("ConfigurationManifest");
        await Assert.That(ConfigurationPortabilityContractRuntime.StringConstant(packageMetadata, "Kind"))
            .IsEqualTo("TenantConfigurationPackage");
        await Assert.That(ConfigurationPortabilityContractRuntime.StringConstant(manifestMetadata, "MediaType"))
            .IsEqualTo("application/vnd.islamu.configuration-manifest.v1alpha2+json");
        await Assert.That(ConfigurationPortabilityContractRuntime.StringConstant(packageMetadata, "MediaType"))
            .IsEqualTo("application/vnd.islamu.tenant-configuration-package.v1alpha2+json");
        await Assert.That(Enum.GetNames(_runtime.RequireType("ConfigurationImportApplyMode")))
            .IsEquivalentTo(
            [
                "PreviewOnly", "CreateNew", "MergeMissing", "ApplySelected",
                "ReplacePortableConfiguration", "ReconcileManaged"
            ]);

        await AssertLimit(limits, "MaximumArtifactUtf8Bytes", ConfigurationPortabilityExpectedVectors.MaximumArtifactUtf8Bytes);
        await AssertLimit(limits, "MaximumJsonDepth", ConfigurationPortabilityExpectedVectors.MaximumJsonDepth);
        await AssertLimit(limits, "MaximumTenantCount", ConfigurationPortabilityExpectedVectors.MaximumTenantCount);
        await AssertLimit(legalLimits, "MaximumDocumentsPerScope", ConfigurationPortabilityExpectedVectors.MaximumLegalDocumentsPerScope);
        await AssertLimit(legalLimits, "MaximumLocalesPerDocument", ConfigurationPortabilityExpectedVectors.MaximumLegalLocalesPerDocument);
        await AssertLimit(legalLimits, "MaximumMarkdownUtf8BytesPerLocale", ConfigurationPortabilityExpectedVectors.MaximumLegalMarkdownUtf8BytesPerLocale);
        await AssertLimit(legalLimits, "MaximumLinksPerLocale", ConfigurationPortabilityExpectedVectors.MaximumLegalLinksPerLocale);
        await AssertLimit(legalLimits, "MaximumPlaceholdersPerLocale", ConfigurationPortabilityExpectedVectors.MaximumLegalPlaceholdersPerLocale);
        await AssertLimit(legalLimits, "MaximumTitleLength", ConfigurationPortabilityExpectedVectors.MaximumLegalTitleLength);
        await AssertLimit(legalLimits, "MaximumSummaryLength", ConfigurationPortabilityExpectedVectors.MaximumLegalSummaryLength);
        await AssertLimit(legalLimits, "MaximumLanguageTagLength", ConfigurationPortabilityExpectedVectors.MaximumLanguageTagLength);
        await AssertLimit(legalLimits, "MaximumLinkLength", ConfigurationPortabilityExpectedVectors.MaximumLegalLinkLength);
        await AssertLimit(legalLimits, "MaximumIdentityValueLength", ConfigurationPortabilityExpectedVectors.MaximumLegalIdentityValueLength);

        Type[] portableRoots = ConfigurationPortabilityContractRuntime.RequiredRecordNames
            .Select(_runtime.RequireType)
            .Append(_runtime.RequireType("ConfigurationPortabilityDiagnostic"))
            .Append(_runtime.RequireType("LegalMarkdownDiagnostic"))
            .ToArray();
        IReadOnlyList<string> forbidden =
            ConfigurationPortabilityInvariantVerifier.FindForbiddenPublicMembers(portableRoots);
        await Assert.That(forbidden).IsEmpty().Because(
            "Portable records and diagnostics cannot carry secret, PII, provider, target, topology, operational, or acceptance authority: "
            + string.Join(", ", forbidden));
    }

    [Test]
    public async Task LegalMarkdownNormalizesAndRendersDeterministicallyWhileFailingClosed()
    {
        if (!_runtime.IsComplete)
            return;

        string normalized = _runtime.NormalizeLegalMarkdown(
            ConfigurationPortabilityExpectedVectors.LegalMarkdown.Replace("\n", "\r\n", StringComparison.Ordinal));
        await Assert.That(normalized).IsEqualTo(ConfigurationPortabilityExpectedVectors.LegalMarkdown);
        await Assert.That(_runtime.NormalizeLegalMarkdown(normalized)).IsEqualTo(normalized);

        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        object rendered;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            rendered = _runtime.RenderLegalMarkdown(
                normalized,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        await Assert.That(ConfigurationPortabilityContractRuntime.Property(rendered, "IsReady") as bool?)
            .IsTrue();
        await Assert.That(ConfigurationPortabilityContractRuntime.StringProperty(rendered, "Html"))
            .IsEqualTo(ConfigurationPortabilityExpectedVectors.LegalHtml);
        object inspection = _runtime.InspectLegalMarkdown(normalized);
        object reparsed = _runtime.InspectLegalMarkdown(_runtime.NormalizeLegalMarkdown(normalized));
        await Assert.That(ConfigurationPortabilityContractRuntime.Property(inspection, "LinkCount"))
            .IsEqualTo(ConfigurationPortabilityContractRuntime.Property(reparsed, "LinkCount"));

        foreach (LegalRejectionVector vector in ConfigurationPortabilityExpectedVectors.LegalRejections)
        {
            Exception failure = _runtime.InspectLegalFailure(vector.Markdown);
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
            _ = _runtime.InspectLegalFailure(invalid);

        const string unresolvedValue = "sentinel-identity-value";
        object unresolved = _runtime.RenderLegalMarkdown(
            "# Policy\n\nOperator: {{accountable_identity}}.",
            new Dictionary<string, string>(StringComparer.Ordinal));
        await Assert.That(ConfigurationPortabilityContractRuntime.Property(unresolved, "IsReady") as bool?)
            .IsFalse();
        await Assert.That(ConfigurationPortabilityContractRuntime.StringProperty(unresolved, "Html"))
            .IsEmpty();
        IReadOnlyList<string> leaks = ConfigurationPortabilityInvariantVerifier.FindValueLeaks(
            ConfigurationPortabilityInvariantVerifier.PublicEnumerable(unresolved, "Diagnostics"),
            unresolvedValue);
        await Assert.That(leaks).IsEmpty();

        object weakLink = _runtime.RenderLegalMarkdown(
            "# Policy\n\n[click here](https://example.test/legal)",
            new Dictionary<string, string>(StringComparer.Ordinal));
        object weakDiagnostic = ConfigurationPortabilityInvariantVerifier
            .PublicEnumerable(weakLink, "Diagnostics").Single();
        await Assert.That(ConfigurationPortabilityContractRuntime.StringProperty(weakDiagnostic, "Code"))
            .IsEqualTo("legal_markdown_link_text_weak");
        await Assert.That(ConfigurationPortabilityContractRuntime.Property(weakDiagnostic, "Subject"))
            .IsNull();
    }

    private static async Task AssertPortableFailure(
        Exception failure,
        string code,
        string path,
        string vector,
        string? sentinel = null)
    {
        await Assert.That(failure.GetType().Name)
            .IsEqualTo("ConfigurationPortabilityContractException");
        await Assert.That(ConfigurationPortabilityContractRuntime.StringProperty(failure, "Code"))
            .IsEqualTo(code).Because(vector);
        await Assert.That(ConfigurationPortabilityContractRuntime.StringProperty(failure, "Path"))
            .IsEqualTo(path).Because(vector);
        await Assert.That(code.Length).IsLessThanOrEqualTo(80);
        await Assert.That(path.Length).IsLessThanOrEqualTo(256);
        await Assert.That(failure.ToString().Length).IsLessThanOrEqualTo(1024);
        if (sentinel is not null)
            await Assert.That(failure.ToString().Contains(sentinel, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertLimit(Type owner, string name, int expected) =>
        await Assert.That(ConfigurationPortabilityContractRuntime.IntConstant(owner, name))
            .IsEqualTo(expected);

    private static object CloneRecord(object value)
    {
        MethodInfo clone = value.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(method => method.Name == "<Clone>$"
                && method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException(
                $"Final wire value '{value.GetType().FullName}' is not a record contract.");
        return clone.Invoke(value, null)!;
    }

    private async Task AssertCallerOwnedDictionaryIsSnapshotted()
    {
        Type instanceType = _runtime.RequireType("ConfigurationManifestInstanceV1Alpha2");
        Type documentType = _runtime.RequireType("ConfigurationManifestDocumentV1Alpha2");
        object instance = Activator.CreateInstance(instanceType)
            ?? throw new InvalidOperationException("Manifest instance record needs public JSON construction.");
        var callerSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["events.require_approval"] = JsonDocument.Parse("true").RootElement.Clone()
        };
        object callerDocuments = Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(string), documentType))!;
        object callerLegalDocuments = Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(
                typeof(string),
                _runtime.RequireType("ConfigurationManifestLegalDocumentV1Alpha2")))!;

        instanceType.GetProperty("Settings")!.SetValue(instance, callerSettings);
        instanceType.GetProperty("Documents")!.SetValue(instance, callerDocuments);
        instanceType.GetProperty("LegalDocuments")!.SetValue(instance, callerLegalDocuments);
        callerSettings["events.user_submission_enabled"] =
            JsonDocument.Parse("false").RootElement.Clone();

        object snapshot = ConfigurationPortabilityContractRuntime.Property(instance, "Settings")!;
        int snapshotCount = ((IEnumerable)snapshot).Cast<object>().Count();
        await Assert.That(snapshotCount).IsEqualTo(1);
        await AssertCollectionRejectsMutation(snapshot);
    }

    private static async Task AssertCollectionRejectsMutation(object collection)
    {
        bool rejected = false;
        try
        {
            switch (collection)
            {
                case IDictionary dictionary:
                    dictionary.Add("mutation", null);
                    break;
                case IList list:
                    list.Add(null);
                    break;
                default:
                    rejected = collection.GetType().Namespace == "System.Collections.Immutable";
                    break;
            }
        }
        catch (NotSupportedException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
