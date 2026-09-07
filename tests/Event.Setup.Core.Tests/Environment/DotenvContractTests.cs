namespace ISLAMU.Setup.Core.EnvironmentTests;

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ISLAMU.Event.Setup.Core.Environment;

public sealed class DotenvContractTests
{
    private readonly EnvironmentContractRuntime _runtime = new();

    [Test]
    public async Task FinalDotenvCodecAndReadinessOwnersExist()
    {
        string[] missing = _runtime.MissingDotenvPrerequisites();

        await Assert.That(missing).IsEmpty()
            .Because("SA-330 must supply the isolated dotenv codec and readiness owners. Missing count="
                + missing.Length);
        if (missing.Length == 0)
            await Assert.That(_runtime.VerifyDotenvPublicSurface()).IsEmpty();
    }

    [Test]
    public async Task SyntheticDialectBreakerRejectsInjectionExpansionMalformedInputAndControls()
    {
        await Assert.That(EnvironmentInvariantVerifier.VerifyDotenvText(
            Encoding.UTF8.GetString(EnvironmentContractExpectedVectors.CanonicalDotenv))).IsEmpty();
        foreach (DotenvRejectionFixture fixture in EnvironmentContractExpectedVectors.DotenvRejections)
        {
            string[] failures = EnvironmentInvariantVerifier.VerifyDotenvText(fixture.Text);
            await Assert.That(failures.Contains(fixture.ExpectedCode, StringComparer.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task SyntheticDialectBreakerEnforcesFileLineKeyValueAndCountBounds()
    {
        string oversizedFile = "SAFE_KEY=" + new string('a',
            EnvironmentContractExpectedVectors.MaximumDotenvFileUtf8Bytes) + "\n";
        string oversizedLine = "SAFE_KEY=" + new string('a',
            EnvironmentContractExpectedVectors.MaximumDotenvLineUtf8Bytes) + "\n";
        string oversizedKey = new('A', EnvironmentContractExpectedVectors.MaximumDotenvKeyCharacters + 1);
        string oversizedValue = "SAFE_KEY=" + new string('a',
            EnvironmentContractExpectedVectors.MaximumDotenvValueUtf8Bytes + 1) + "\n";
        string tooMany = string.Concat(Enumerable.Range(
            0, EnvironmentContractExpectedVectors.MaximumDotenvEntryCount + 1)
            .Select(index => $"KEY_{index}=\n"));

        await Assert.That(EnvironmentInvariantVerifier.VerifyDotenvText(oversizedFile))
            .Contains("dotenv-file-too-large");
        await Assert.That(EnvironmentInvariantVerifier.VerifyDotenvText(oversizedLine))
            .Contains("dotenv-line-too-large");
        await Assert.That(EnvironmentInvariantVerifier.VerifyDotenvText(oversizedKey + "=\n"))
            .Contains("dotenv-key-invalid");
        await Assert.That(EnvironmentInvariantVerifier.VerifyDotenvText(oversizedValue))
            .Contains("dotenv-value-too-large");
        await Assert.That(EnvironmentInvariantVerifier.VerifyDotenvText(tooMany))
            .Contains("dotenv-count-exceeded");
    }

    [Test]
    public async Task SyntheticReadinessUsesRelevantRequiredClassificationOnly()
    {
        IReadOnlyList<CatalogueDefinitionFixture> relevant = EnvironmentContractExpectedVectors.ValidDefinitions;
        IReadOnlyList<CatalogueDefinitionFixture> withoutDatabase = relevant.Skip(1).ToArray();
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        var secretPresent = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DATABASE_PASSWORD"] = "generated-reference",
        };
        (string State, string[] Missing, string[] Blocked) blocked =
            EnvironmentInvariantVerifier.ComputeReadiness(relevant, empty);
        (string State, string[] Missing, string[] Blocked) ready =
            EnvironmentInvariantVerifier.ComputeReadiness(relevant, secretPresent);
        (string State, string[] Missing, string[] Blocked) irrelevantMissing =
            EnvironmentInvariantVerifier.ComputeReadiness(withoutDatabase, empty);

        await Assert.That(blocked.State).IsEqualTo("blocked");
        await Assert.That(blocked.Blocked).IsEquivalentTo(["DATABASE_PASSWORD"]);
        await Assert.That(blocked.Missing).IsEmpty();
        await Assert.That(ready.State).IsEqualTo("ready");
        await Assert.That(irrelevantMissing.State).IsEqualTo("ready");
    }

    [Test]
    public async Task ParseRenderUsesUtf8LfOrdinalOrderExactPlaceholdersAndOptionalFinalNewline()
    {
        if (!_runtime.IsDotenvComplete()) return;

        object document = _runtime.CreateDotenvDocument(
        [
            ("MAIL_PORT", "587", "SafeDefault", false, "CatalogueDefault"),
            ("DATABASE_PASSWORD", null, "EmptyPlaceholder", true, "UserInput"),
            ("MAIL_FROM_NAME", null, "EmptyPlaceholder", false, "UserInput"),
        ]);
        object withNewline = _runtime.RenderDotenv(document, true);
        object withoutNewline = _runtime.RenderDotenv(document, false);
        byte[] canonical = EnvironmentContractExpectedVectors.CanonicalDotenv;

        await Assert.That(EnvironmentContractRuntime.RenderedBytes(withNewline)).IsEquivalentTo(canonical);
        await Assert.That(EnvironmentContractRuntime.RenderedBytes(withoutNewline))
            .IsEquivalentTo(canonical[..^1]);
        await Assert.That(canonical[0] == 0xEF && canonical[1] == 0xBB && canonical[2] == 0xBF).IsFalse();
        await Assert.That(canonical.Contains((byte)'\r')).IsFalse();
    }

    [Test]
    public async Task QuotingEscapingUnicodeAndCommentsRoundTripToOneCanonicalForm()
    {
        if (!_runtime.IsDotenvComplete()) return;

        byte[] input = Encoding.UTF8.GetBytes(
            "# ignored on canonical render\nUNICODE_NAME=\"سلام\"\nHASH_TEXT=\"value # text\"\nQUOTE_TEXT=\"a\\\"b\\\\c\"\n");
        byte[] expected = Encoding.UTF8.GetBytes(
            "HASH_TEXT=\"value # text\"\nQUOTE_TEXT=\"a\\\"b\\\\c\"\nUNICODE_NAME=\"سلام\"\n");
        object first = _runtime.ParseDotenv(input);
        object rendered = _runtime.RenderDotenv(
            EnvironmentContractRuntime.RequiredProperty(first, "Document"), true);
        byte[] canonical = EnvironmentContractRuntime.RenderedBytes(rendered);
        object second = _runtime.ParseDotenv(canonical);
        object rerendered = _runtime.RenderDotenv(
            EnvironmentContractRuntime.RequiredProperty(second, "Document"), true);

        await Assert.That(EnvironmentContractRuntime.DiagnosticCodes(first)).IsEmpty();
        await Assert.That(canonical).IsEquivalentTo(expected);
        await Assert.That(EnvironmentContractRuntime.RenderedBytes(rerendered)).IsEquivalentTo(expected);
    }

    [Test]
    public async Task ParserRejectsEveryForbiddenVectorAndInvalidUtf8WithStableValueSafeDiagnostics()
    {
        if (!_runtime.IsDotenvComplete()) return;

        foreach (DotenvRejectionFixture fixture in EnvironmentContractExpectedVectors.DotenvRejections)
        {
            object result = _runtime.ParseDotenv(Encoding.UTF8.GetBytes(fixture.Text));
            string[] codes = EnvironmentContractRuntime.DiagnosticCodes(result);
            string[] diagnosticStrings = EnvironmentContractRuntime.PublicDiagnosticStrings(result);

            await Assert.That(codes).Contains(fixture.ExpectedCode);
            await Assert.That(diagnosticStrings.Any(value =>
                value.Contains(fixture.Text, StringComparison.Ordinal))).IsFalse();
        }

        object invalidUtf8 = _runtime.ParseDotenv([0x53, 0x41, 0x46, 0x45, 0x5F, 0x4B, 0x45, 0x59, 0x3D, 0xFF]);
        await Assert.That(EnvironmentContractRuntime.DiagnosticCodes(invalidUtf8))
            .Contains("dotenv-utf8-invalid");
    }

    [Test]
    public async Task EntryModelSeparatesEveryValueAndProvenanceStateWithoutUnknownKinds()
    {
        if (!_runtime.IsDotenvComplete()) return;

        object document = _runtime.CreateDotenvDocument(
        [
            ("EMPTY_KEY", null, "EmptyPlaceholder", false, "UserInput"),
            ("DEFAULT_KEY", "safe", "SafeDefault", false, "CatalogueDefault"),
            ("LOCAL_KEY", "local", "LocalHumanValue", true, "UserInput"),
            ("GENERATED_KEY", "generated:reference", "GeneratedValueReference", true, "Generated"),
        ]);
        IReadOnlyList<object> entries = EnvironmentContractRuntime.Entries(document);

        await Assert.That(Enum.GetNames(_runtime.RequireType("DotenvEntryKind")))
            .IsEquivalentTo(["EmptyPlaceholder", "SafeDefault", "LocalHumanValue", "GeneratedValueReference"]);
        await Assert.That(entries.Select(item => EnvironmentContractRuntime.Property(item, "Kind")?.ToString()).OfType<string>())
            .IsEquivalentTo(["EmptyPlaceholder", "SafeDefault", "LocalHumanValue", "GeneratedValueReference"]);
        await Assert.That(entries.Count(item => EnvironmentContractRuntime.Property(item, "IsSecret") as bool? == true))
            .IsEqualTo(2);
        await Assert.That(entries.Select(item => EnvironmentContractRuntime.Property(item, "Provenance")?.ToString()).OfType<string>())
            .IsEquivalentTo(["UserInput", "CatalogueDefault", "UserInput", "Generated"]);
    }

    [Test]
    public async Task RendererRejectsDuplicateDocumentsUnknownKindsAndValueShapeBeforeReturningBytes()
    {
        var duplicate = new DotenvDocument(
        [
            new DotenvEntry("SAFE_KEY", "first", DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput),
            new DotenvEntry("SAFE_KEY", "second", DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput),
        ]);
        var unknown = new DotenvDocument(
        [
            new DotenvEntry("SAFE_KEY", "value", (DotenvEntryKind)999, false, DotenvProvenance.UserInput),
        ]);

        DotenvRenderResult duplicateResult = DotenvCodec.Render(duplicate, true);
        DotenvRenderResult unknownResult = DotenvCodec.Render(unknown, true);

        await Assert.That(duplicateResult.Diagnostics.Select(item => item.Code)).Contains("dotenv-duplicate-key");
        await Assert.That(duplicateResult.Bytes.IsEmpty).IsTrue();
        await Assert.That(unknownResult.Diagnostics.Select(item => item.Code)).Contains("dotenv-entry-kind-invalid");
        await Assert.That(unknownResult.Bytes.IsEmpty).IsTrue();
    }

    [Test]
    public async Task RendererFailsClosedForInvalidUtf16AndNullDocumentEntries()
    {
        var invalidUtf16 = new DotenvDocument(
        [
            new DotenvEntry("SAFE_KEY", "\uD800", DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput),
        ]);
        var nullEntry = new DotenvDocument([null!]);

        DotenvRenderResult invalidUtf16Result = DotenvCodec.Render(invalidUtf16, true);
        DotenvRenderResult nullEntryResult = DotenvCodec.Render(nullEntry, true);

        await Assert.That(invalidUtf16Result.Diagnostics.Select(item => item.Code))
            .Contains("dotenv-utf16-invalid");
        await Assert.That(invalidUtf16Result.Bytes.IsEmpty).IsTrue();
        await Assert.That(nullEntryResult.Diagnostics.Select(item => item.Code))
            .Contains("dotenv-entry-null");
        await Assert.That(nullEntryResult.Bytes.IsEmpty).IsTrue();
    }

    [Test]
    public async Task CatalogueDrivenCompositionSeparatesNoSecretAndSecretModes()
    {
        EnvironmentCatalogue catalogue = CreateCompositionCatalogue();
        var context = new EnvironmentActivationContext("single", ["platform"], []);
        DotenvEntry[] supplied =
        [
            new("PUBLIC_REQUIRED", "public-input", DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput),
            new("PUBLIC_DEFAULT", "default", DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput),
            new("IRRELEVANT_KEY", "irrelevant", DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput),
            new("SETUP_SECRET", "local-secret", DotenvEntryKind.LocalHumanValue, true, DotenvProvenance.UserInput),
        ];

        DotenvCompositionResult noSecret = DotenvComposer.ComposeNoSecrets(catalogue, context, supplied);
        DotenvCompositionResult withSecret = DotenvComposer.ComposeWithSecrets(catalogue, context, supplied);

        await Assert.That(noSecret.Document.Entries.Select(item => item.Key))
            .IsEquivalentTo(["PUBLIC_REQUIRED", "SETUP_SECRET"]);
        await Assert.That(noSecret.Document.Entries.Single(item => item.Key == "SETUP_SECRET").Kind)
            .IsEqualTo(DotenvEntryKind.EmptyPlaceholder);
        await Assert.That(noSecret.Readiness.State).IsEqualTo(DotenvReadinessState.Blocked);
        await Assert.That(noSecret.Diagnostics.Select(item => item.Code)).Contains("dotenv-secret-input-forbidden");
        await Assert.That(withSecret.Document.Entries.Select(item => item.Key))
            .IsEquivalentTo(["PUBLIC_REQUIRED", "SETUP_SECRET"]);
        await Assert.That(withSecret.Readiness.State).IsEqualTo(DotenvReadinessState.Ready);
    }

    [Test]
    public async Task AmbiguousAndNullCompositionInputsAreNeverSelectedOrReady()
    {
        EnvironmentCatalogue catalogue = CreateCompositionCatalogue();
        var context = new EnvironmentActivationContext("single", ["platform"], []);
        DotenvEntry[][] ambiguousInputs =
        [
            [
                new("SETUP_SECRET", "first", DotenvEntryKind.LocalHumanValue, true, DotenvProvenance.UserInput),
                new("SETUP_SECRET", "second", DotenvEntryKind.LocalHumanValue, true, DotenvProvenance.UserInput),
                null!,
            ],
            [
                new("SETUP_SECRET", "first", DotenvEntryKind.LocalHumanValue, true, DotenvProvenance.UserInput),
                new("setup_secret", "second", DotenvEntryKind.LocalHumanValue, true, DotenvProvenance.UserInput),
            ],
        ];

        foreach (DotenvEntry[] supplied in ambiguousInputs)
        {
            DotenvCompositionResult result = DotenvComposer.ComposeWithSecrets(catalogue, context, supplied);
            DotenvEntry entry = result.Document.Entries.Single(item => item.Key == "SETUP_SECRET");
            DotenvRenderResult rendered = DotenvCodec.Render(result.Document, true);

            await Assert.That(entry.Kind).IsEqualTo(DotenvEntryKind.EmptyPlaceholder);
            await Assert.That(result.Readiness.State).IsEqualTo(DotenvReadinessState.Blocked);
            await Assert.That(result.Diagnostics.Select(item => item.Code).Any(code =>
                code is "dotenv-input-duplicate-key" or "dotenv-input-key-case-collision")).IsTrue();
            await Assert.That(Encoding.UTF8.GetString(rendered.Bytes.Span))
                .IsEqualTo("PUBLIC_REQUIRED=\nSETUP_SECRET=\n");
        }
    }

    [Test]
    public async Task ParsedUnclassifiedValueCannotSatisfyProtectedReadiness()
    {
        EnvironmentCatalogue catalogue = CreateCompositionCatalogue();
        var context = new EnvironmentActivationContext("single", ["platform"], []);
        DotenvParseResult parsed = DotenvCodec.Parse(Encoding.UTF8.GetBytes("SETUP_SECRET=unclassified\n"));

        DotenvReadinessResult result = DotenvReadiness.Evaluate(catalogue, context, parsed.Document!);

        await Assert.That(result.State).IsEqualTo(DotenvReadinessState.Blocked);
        await Assert.That(result.Blocked).IsEquivalentTo(["SETUP_SECRET"]);
        await Assert.That(result.Diagnostics.Select(item => item.Code)).Contains("dotenv-entry-state-invalid");
    }

    [Test]
    public async Task ReadinessPrecedenceAndResultsUseRelevantRequiredKeysOnly()
    {
        EnvironmentCatalogue catalogue = CreateCompositionCatalogue();
        var context = new EnvironmentActivationContext("single", ["platform"], []);
        var empty = new DotenvDocument([]);
        DotenvReadinessResult result = DotenvReadiness.Evaluate(catalogue, context, empty);

        await Assert.That(result.State).IsEqualTo(DotenvReadinessState.Blocked);
        await Assert.That(result.Missing).IsEquivalentTo(["PUBLIC_REQUIRED"]);
        await Assert.That(result.Blocked).IsEquivalentTo(["SETUP_SECRET"]);
        await Assert.That(result.ToString().Contains("PUBLIC_REQUIRED", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ConfiguredBootstrapReadinessIsExactForInteractiveKeycloakAndAtprotoModes()
    {
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        EnvironmentVariableDefinition[] bootstrap = catalogue.Definitions
            .Where(item => item.Key.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal))
            .ToArray();
        var interactive = new DotenvDocument(
        [
            Public("INSTANCE_BOOTSTRAP_MODE", "Interactive"),
        ]);
        var keycloak = new DotenvDocument(
        [
            Public("INSTANCE_BOOTSTRAP_MODE", "ConfiguredAdministrator"),
            Public("INSTANCE_BOOTSTRAP_ADMIN_PROVIDER", "keycloak"),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "subject-marker-not-an-identity"),
            Public("INSTANCE_BOOTSTRAP_BINDING_GENERATION", "1"),
        ]);
        var atproto = new DotenvDocument(
        [
            Public("INSTANCE_BOOTSTRAP_MODE", "ConfiguredAdministrator"),
            Public("INSTANCE_BOOTSTRAP_ADMIN_PROVIDER", "atproto"),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "did-marker-not-an-identity"),
            Public("INSTANCE_BOOTSTRAP_BINDING_GENERATION", "1"),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_EMAIL", "email-marker-not-an-address"),
        ]);

        string[] interactiveKeys = RelevantBootstrapKeys(catalogue, "interactive", "keycloak");
        string[] keycloakKeys = RelevantBootstrapKeys(catalogue, "configured-administrator", "keycloak");
        string[] atprotoKeys = RelevantBootstrapKeys(catalogue, "configured-administrator", "atproto");
        DotenvReadinessResult interactiveReadiness = DotenvReadiness.Evaluate(
            bootstrap.Where(item => interactiveKeys.Contains(item.Key)), interactive);
        DotenvReadinessResult keycloakReadiness = DotenvReadiness.Evaluate(
            bootstrap.Where(item => keycloakKeys.Contains(item.Key)), keycloak);
        DotenvReadinessResult atprotoReadiness = DotenvReadiness.Evaluate(
            bootstrap.Where(item => atprotoKeys.Contains(item.Key)), atproto);

        await Assert.That(interactiveKeys).IsEquivalentTo(["INSTANCE_BOOTSTRAP_MODE"]);
        await Assert.That(keycloakKeys).IsEquivalentTo(
        [
            "INSTANCE_BOOTSTRAP_MODE", "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER",
            "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "INSTANCE_BOOTSTRAP_BINDING_GENERATION",
        ]);
        await Assert.That(atprotoKeys).IsEquivalentTo(
        [
            "INSTANCE_BOOTSTRAP_MODE", "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER",
            "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "INSTANCE_BOOTSTRAP_BINDING_GENERATION",
            "INSTANCE_BOOTSTRAP_ADMIN_EMAIL", "INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME",
            "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME",
        ]);
        await Assert.That(interactiveReadiness.State).IsEqualTo(DotenvReadinessState.Ready);
        await Assert.That(keycloakReadiness.State).IsEqualTo(DotenvReadinessState.Ready);
        await Assert.That(atprotoReadiness.State).IsEqualTo(DotenvReadinessState.Ready);
    }

    [Test]
    public async Task ConfiguredBootstrapInvariantBreakersUseProductionCompositionAndReadiness()
    {
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        var keycloakContext = new EnvironmentActivationContext(
            "standalone", ["identity"], ["configured-administrator", "keycloak"]);
        var atprotoContext = new EnvironmentActivationContext(
            "standalone", ["identity"], ["configured-administrator", "atproto"]);

        DotenvCompositionResult partial = DotenvComposer.ComposeWithSecrets(
            catalogue, keycloakContext,
            [Public("INSTANCE_BOOTSTRAP_MODE", "ConfiguredAdministrator")]);
        await Assert.That(partial.Diagnostics.Select(item => item.Code))
            .DoesNotContain("dotenv-input-key-unknown");
        await Assert.That(BootstrapReadiness(
            catalogue, keycloakContext, partial.Document).State).IsEqualTo(DotenvReadinessState.Blocked);
        await Assert.That(BootstrapOnly(partial.Readiness.Missing)).IsEquivalentTo(
            ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER", "INSTANCE_BOOTSTRAP_BINDING_GENERATION"]);
        await Assert.That(BootstrapOnly(partial.Readiness.Blocked))
            .IsEquivalentTo(["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"]);

        DotenvEntry[] validKeycloakShape =
        [
            Public("INSTANCE_BOOTSTRAP_MODE", "ConfiguredAdministrator"),
            Public("INSTANCE_BOOTSTRAP_ADMIN_PROVIDER", "keycloak"),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "subject-marker-not-an-identity"),
            Public("INSTANCE_BOOTSTRAP_BINDING_GENERATION", "1"),
        ];
        DotenvCompositionResult unknownMode = DotenvComposer.ComposeWithSecrets(
            catalogue, keycloakContext,
            validKeycloakShape.Select(item => item.Key == "INSTANCE_BOOTSTRAP_MODE"
                ? Public(item.Key, "Unknown") : item));
        DotenvCompositionResult unknownProvider = DotenvComposer.ComposeWithSecrets(
            catalogue, keycloakContext,
            validKeycloakShape.Select(item => item.Key == "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"
                ? Public(item.Key, "unknown") : item));
        DotenvCompositionResult invalidGeneration = DotenvComposer.ComposeWithSecrets(
            catalogue, keycloakContext,
            validKeycloakShape.Select(item => item.Key == "INSTANCE_BOOTSTRAP_BINDING_GENERATION"
                ? Public(item.Key, "0") : item));

        await Assert.That(unknownMode.Diagnostics.Any(item =>
            item.Code == "dotenv-input-value-invalid"
            && item.Key == "INSTANCE_BOOTSTRAP_MODE")).IsTrue();
        await Assert.That(BootstrapReadiness(
            catalogue, keycloakContext, unknownMode.Document).State).IsEqualTo(DotenvReadinessState.Incomplete);
        await Assert.That(BootstrapOnly(unknownMode.Readiness.Missing))
            .IsEquivalentTo(["INSTANCE_BOOTSTRAP_MODE"]);
        await Assert.That(BootstrapOnly(unknownMode.Readiness.Blocked)).IsEmpty();
        await Assert.That(unknownProvider.Diagnostics.Any(item =>
            item.Code == "dotenv-input-value-invalid"
            && item.Key == "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER")).IsTrue();
        await Assert.That(BootstrapReadiness(
            catalogue, keycloakContext, unknownProvider.Document).State).IsEqualTo(DotenvReadinessState.Incomplete);
        await Assert.That(BootstrapOnly(unknownProvider.Readiness.Missing))
            .IsEquivalentTo(["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"]);
        await Assert.That(BootstrapOnly(unknownProvider.Readiness.Blocked)).IsEmpty();
        await Assert.That(invalidGeneration.Diagnostics.Any(item =>
            item.Code == "dotenv-input-value-invalid"
            && item.Key == "INSTANCE_BOOTSTRAP_BINDING_GENERATION")).IsTrue();
        await Assert.That(BootstrapReadiness(
            catalogue, keycloakContext, invalidGeneration.Document).State).IsEqualTo(DotenvReadinessState.Incomplete);
        await Assert.That(BootstrapOnly(invalidGeneration.Readiness.Missing))
            .IsEquivalentTo(["INSTANCE_BOOTSTRAP_BINDING_GENERATION"]);
        await Assert.That(BootstrapOnly(invalidGeneration.Readiness.Blocked)).IsEmpty();

        DotenvCompositionResult inapplicableFallback = DotenvComposer.ComposeWithSecrets(
            catalogue, keycloakContext,
            validKeycloakShape.Append(Sensitive(
                "INSTANCE_BOOTSTRAP_ADMIN_EMAIL", "email-marker-not-an-address")));
        await Assert.That(inapplicableFallback.Diagnostics.Any(item =>
            item.Code == "dotenv-input-key-irrelevant"
            && item.Key == "INSTANCE_BOOTSTRAP_ADMIN_EMAIL")).IsTrue();
        await Assert.That(BootstrapReadiness(
            catalogue, keycloakContext, inapplicableFallback.Document).State).IsEqualTo(DotenvReadinessState.Ready);
        await Assert.That(BootstrapOnly(inapplicableFallback.Readiness.Missing)).IsEmpty();
        await Assert.That(BootstrapOnly(inapplicableFallback.Readiness.Blocked)).IsEmpty();

        DotenvEntry[] partialProfile =
        [
            Public("INSTANCE_BOOTSTRAP_MODE", "ConfiguredAdministrator"),
            Public("INSTANCE_BOOTSTRAP_ADMIN_PROVIDER", "atproto"),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "did-marker-not-an-identity"),
            Public("INSTANCE_BOOTSTRAP_BINDING_GENERATION", "1"),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_EMAIL", "email-marker@example.invalid"),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME", "first-name-marker"),
        ];
        DotenvCompositionResult incompleteProfile = DotenvComposer.ComposeWithSecrets(
            catalogue, atprotoContext, partialProfile);
        DotenvCompositionResult oversizedProfile = DotenvComposer.ComposeWithSecrets(
            catalogue, atprotoContext,
            partialProfile.Append(Sensitive(
                "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME", new string('a', 129))));

        await Assert.That(incompleteProfile.Diagnostics.Any(item =>
            item.Code == "dotenv-input-matrix-invalid"
            && item.Key == "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME")).IsTrue();
        await Assert.That(BootstrapReadiness(
            catalogue, atprotoContext, incompleteProfile.Document).State).IsEqualTo(DotenvReadinessState.Ready);
        await Assert.That(BootstrapOnly(incompleteProfile.Readiness.Missing)).IsEmpty();
        await Assert.That(BootstrapOnly(incompleteProfile.Readiness.Blocked)).IsEmpty();
        await Assert.That(oversizedProfile.Diagnostics.Any(item =>
            item.Code == "dotenv-input-value-invalid"
            && item.Key == "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME")).IsTrue();
        await Assert.That(BootstrapReadiness(
            catalogue, atprotoContext, oversizedProfile.Document).State).IsEqualTo(DotenvReadinessState.Ready);
        await Assert.That(BootstrapOnly(oversizedProfile.Readiness.Missing)).IsEmpty();
        await Assert.That(BootstrapOnly(oversizedProfile.Readiness.Blocked)).IsEmpty();
    }

    [Test]
    public async Task ConfiguredBootstrapSensitiveInputsAreRejectedAndNeverRendered()
    {
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        var context = new EnvironmentActivationContext(
            "standalone", ["identity"], ["configured-administrator", "atproto"]);
        string[] markers =
        [
            "subject-marker-not-an-identity", "email-marker-not-an-address",
            "first-name-marker", "last-name-marker",
        ];
        DotenvEntry[] supplied =
        [
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", markers[0]),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_EMAIL", markers[1]),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME", markers[2]),
            Sensitive("INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME", markers[3]),
        ];

        DotenvCompositionResult result = DotenvComposer.ComposeNoSecrets(catalogue, context, supplied);
        DotenvRenderResult rendered = DotenvCodec.Render(result.Document, true);
        string renderedText = Encoding.UTF8.GetString(rendered.Bytes.Span);
        string[] forbiddenKeys = result.Diagnostics
            .Where(item => item.Code == "dotenv-secret-input-forbidden")
            .Select(item => item.Key!).ToArray();

        await Assert.That(result.Diagnostics.Select(item => item.Code))
            .DoesNotContain("dotenv-input-key-unknown");
        await Assert.That(forbiddenKeys).IsEquivalentTo(
        [
            "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "INSTANCE_BOOTSTRAP_ADMIN_EMAIL",
            "INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME", "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME",
        ]);
        await Assert.That(result.Document.Entries.Where(item => item.IsSecret)
            .All(item => item.Value is null && item.Kind == DotenvEntryKind.EmptyPlaceholder)).IsTrue();
        await Assert.That(markers.Any(marker => renderedText.Contains(
            marker, StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ApprovedGeneratorConsumesFreshEntropyAndDeniesUnapprovedKeysWithoutRetention()
    {
        using var entropy = new SequenceEntropySource();
        ConstructorInfo constructor = typeof(LocalSecretGenerator).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RandomNumberGenerator)], null)!;
        using var generator = (LocalSecretGenerator)constructor.Invoke([entropy]);
        using LocalSecretGenerationResult first = generator.Generate(
            "SETUP_SECRET", LocalSecretGenerationProfile.OpaqueUrlSafe256);
        using LocalSecretGenerationResult second = generator.Generate(
            "SETUP_SECRET", LocalSecretGenerationProfile.OpaqueUrlSafe256);
        using LocalSecretGenerationResult denied = generator.Generate(
            "STRIPE_PLATFORM_SECRET_KEY", LocalSecretGenerationProfile.OpaqueUrlSafe256);
        string firstValue = first.Output!.CopyValue();
        string secondValue = second.Output!.CopyValue();
        byte[] firstBytes = first.Output.CopyUtf8Bytes();

        try
        {
            await Assert.That(firstValue.Length).IsEqualTo(43);
            await Assert.That(firstBytes.Length).IsEqualTo(43);
            await Assert.That(firstBytes.All(value => value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z'
                or (byte)'-' or (byte)'_')).IsTrue();
            await Assert.That(secondValue.Length).IsEqualTo(43);
            await Assert.That(string.Equals(firstValue, secondValue, StringComparison.Ordinal)).IsFalse();
            await Assert.That(first.Output.Provenance).IsEqualTo(DotenvProvenance.Generated);
            await Assert.That(denied.Output is null).IsTrue();
            await Assert.That(denied.Diagnostics.Select(item => item.Code)).Contains("secret-generation-key-unapproved");
            await Assert.That(first.ToString().Contains(firstValue, StringComparison.Ordinal)).IsFalse();
            await Assert.That(first.Output.ToString().Contains(firstValue, StringComparison.Ordinal)).IsFalse();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstBytes);
        }
    }

    private static EnvironmentCatalogue CreateCompositionCatalogue()
    {
        EnvironmentGenerationPolicy generation = new(EnvironmentGenerationSurface.Dotenv, 1, null, false);
        EnvironmentDocumentationMetadata documentation = new("localization.key", "help.key", "test-anchor");
        EnvironmentActivationExpression active = EnvironmentActivationExpression.Capability("platform");
        EnvironmentActivationExpression inactive = EnvironmentActivationExpression.Capability("other");
        EnvironmentVariableDefinition[] definitions =
        [
            new("SETUP_SECRET", EnvironmentVariableCategory.Platform, EnvironmentVariableSensitivity.Secret,
                EnvironmentVariableRequirement.Required, null, 1, active, "platform-setting", generation,
                EnvironmentRestartBehavior.Process, documentation),
            new("PUBLIC_REQUIRED", EnvironmentVariableCategory.Platform, EnvironmentVariableSensitivity.Public,
                EnvironmentVariableRequirement.Required, null, 2, active, "platform-setting",
                new(EnvironmentGenerationSurface.Dotenv, 2, null, false), EnvironmentRestartBehavior.Process, documentation),
            new("PUBLIC_DEFAULT", EnvironmentVariableCategory.Platform, EnvironmentVariableSensitivity.Public,
                EnvironmentVariableRequirement.Defaulted, "default", 3, active, "platform-setting",
                new(EnvironmentGenerationSurface.Dotenv, 3, null, false), EnvironmentRestartBehavior.Process, documentation),
            new("IRRELEVANT_KEY", EnvironmentVariableCategory.Platform, EnvironmentVariableSensitivity.Public,
                EnvironmentVariableRequirement.Required, null, 4, inactive, "platform-setting",
                new(EnvironmentGenerationSurface.Dotenv, 4, null, false), EnvironmentRestartBehavior.Process, documentation),
        ];
        var graph = new EnvironmentActivationGraph(["single"], ["platform", "other"], [], []);
        return EnvironmentCatalogue.Create(definitions, graph, ["SETUP_SECRET"]).Catalogue!;
    }

    private static string[] RelevantBootstrapKeys(
        EnvironmentCatalogue catalogue,
        string mode,
        string provider) => catalogue.Relevant(new EnvironmentActivationContext(
            "standalone", ["identity"], [mode, provider]))
        .Select(item => item.Key)
        .Where(key => key.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal))
        .ToArray();

    private static string[] BootstrapOnly(IEnumerable<string> keys) => keys
        .Where(key => key.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal))
        .ToArray();

    private static DotenvReadinessResult BootstrapReadiness(
        EnvironmentCatalogue catalogue,
        EnvironmentActivationContext context,
        DotenvDocument document) => DotenvReadiness.Evaluate(
        catalogue.Relevant(context).Where(definition =>
            definition.Key.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal)
            && definition.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Dotenv)),
        document);

    private static DotenvEntry Public(string key, string value) => new(
        key, value, DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput);

    private static DotenvEntry Sensitive(string key, string value) => new(
        key, value, DotenvEntryKind.LocalHumanValue, true, DotenvProvenance.UserInput);

    private sealed class SequenceEntropySource : RandomNumberGenerator
    {
        private byte _next = 1;

        public override void GetBytes(byte[] data) => FillSequence(data);
        public override void GetBytes(Span<byte> data) => FillSequence(data);

        private void FillSequence(Span<byte> data)
        {
            data.Fill(_next);
            _next++;
        }
    }
}
