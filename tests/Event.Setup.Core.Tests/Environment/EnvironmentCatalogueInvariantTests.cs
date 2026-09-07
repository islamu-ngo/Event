namespace ISLAMU.Setup.Core.EnvironmentTests;

using System.Reflection;
using ISLAMU.Event.Setup.Core.Environment;

public sealed class EnvironmentCatalogueInvariantTests
{
    private readonly EnvironmentContractRuntime _runtime = new();
    private readonly string _repositoryRoot = EnvironmentMachineConfiguration.RepositoryRoot();

    [Test]
    public async Task FinalPackageFreeCatalogueOwnersAndMachineCatalogueExist()
    {
        string[] missing = _runtime.MissingCataloguePrerequisites(_repositoryRoot);

        await Assert.That(missing).IsEmpty()
            .Because("SA-320 must supply the complete Core catalogue owners and generator-owned machine catalogue. Missing count="
                + missing.Length);
    }

    [Test]
    public async Task SyntheticActivationBreakersRejectCyclesSelfReferencesAndUnknownIdentifiers()
    {
        await Assert.That(EnvironmentInvariantVerifier.VerifyActivationGraph(
            EnvironmentContractExpectedVectors.ValidActivationGraph)).IsEmpty();
        foreach (InvalidActivationFixture fixture in EnvironmentContractExpectedVectors.InvalidActivationGraphs)
        {
            string[] failures = EnvironmentInvariantVerifier.VerifyActivationGraph(fixture.Graph);
            await Assert.That(failures.Contains(fixture.ExpectedCode, StringComparer.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task SyntheticCatalogueBreakersRejectDuplicatesCollisionsOrderingAndUnsafeDefaults()
    {
        await Assert.That(EnvironmentInvariantVerifier.VerifyCatalogue(
            EnvironmentContractExpectedVectors.ValidDefinitions)).IsEmpty();
        foreach (InvalidCatalogueFixture fixture in EnvironmentContractExpectedVectors.InvalidCatalogues)
        {
            string[] failures = EnvironmentInvariantVerifier.VerifyCatalogue(fixture.Definitions);
            await Assert.That(failures.Contains(fixture.ExpectedCode, StringComparer.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task SyntheticRelevanceAndSecretParityBreakersRejectIrrelevantAndFakeSecretEntries()
    {
        IReadOnlySet<string> correctRelevant = new HashSet<string>(
            ["DATABASE_PASSWORD"], StringComparer.Ordinal);
        IReadOnlySet<string> irrelevantIncluded = new HashSet<string>(
            ["DATABASE_PASSWORD", "MAIL_FROM_NAME"], StringComparer.Ordinal);
        string[] valid = EnvironmentInvariantVerifier.VerifyRelevantProjection(
            EnvironmentContractExpectedVectors.ValidDefinitions,
            EnvironmentContractExpectedVectors.ValidActivationGraph,
            "split", new HashSet<string>(["database"], StringComparer.Ordinal),
            new HashSet<string>(["postgresql"], StringComparer.Ordinal), correctRelevant);
        string[] invalid = EnvironmentInvariantVerifier.VerifyRelevantProjection(
            EnvironmentContractExpectedVectors.ValidDefinitions,
            EnvironmentContractExpectedVectors.ValidActivationGraph,
            "split", new HashSet<string>(["database"], StringComparer.Ordinal),
            new HashSet<string>(["postgresql"], StringComparer.Ordinal), irrelevantIncluded);
        string[] mismatch = EnvironmentInvariantVerifier.VerifySecretParity(
            EnvironmentContractExpectedVectors.ValidDefinitions,
            new HashSet<string>(["MAIL_PORT", "FAKE_SECRET"], StringComparer.Ordinal));

        await Assert.That(valid).IsEmpty();
        await Assert.That(invalid).Contains("catalogue-irrelevant-key-included");
        await Assert.That(mismatch).Contains("catalogue-secret-classification-mismatch");
        await Assert.That(mismatch).Contains("catalogue-fake-secret-binding");
    }

    [Test]
    public async Task SyntheticDiagnosticLeakBreakerRejectsValuesCommentsDefaultsAndAuthorityData()
    {
        string[] forbidden =
        [
            "supplied-marker", "comment-marker", "default-marker", "credential-marker",
            "connection-marker", "token-marker", "url-marker", "host-marker", "person-marker",
        ];
        var safe = new EnvironmentDiagnosticFixture(
            "catalogue-key-invalid", "$.definitions[0].key", "SAFE_KEY", "validation");
        var leaking = safe with { SuppliedValue = forbidden[0], Message = forbidden[1] };

        await Assert.That(EnvironmentInvariantVerifier.VerifyDiagnosticValues([safe], forbidden)).IsEmpty();
        await Assert.That(EnvironmentInvariantVerifier.VerifyDiagnosticValues([leaking], forbidden))
            .Contains("diagnostic-value-leak");
        await Assert.That(EnvironmentInvariantVerifier.VerifyDiagnosticShape(typeof(SyntheticLeakingEnvironmentDiagnostic)))
            .Contains("diagnostic-value-member");
    }

    [Test]
    public async Task PublicContractIsImmutableClosedIdentifierBasedAndValueSafe()
    {
        if (!_runtime.IsCatalogueComplete(_repositoryRoot)) return;

        string[] failures = _runtime.VerifyCataloguePublicSurface();
        Type definition = _runtime.RequireType("EnvironmentVariableDefinition");
        bool hasWritableProperty = definition.GetProperties().Any(property => property.SetMethod?.IsPublic == true);

        await Assert.That(failures).IsEmpty().Because(string.Join(';', failures));
        await Assert.That(hasWritableProperty).IsFalse();
        await Assert.That(definition.IsSealed).IsTrue();
    }

    [Test]
    public async Task CanonicalMetadataDistinguishesRequirementsDefaultsValidatorsRestartAndActivation()
    {
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        EnvironmentVariableDefinition database = catalogue.Lookup("DATABASE_PROVIDER")!;
        EnvironmentVariableDefinition identity = catalogue.Lookup("KEYCLOAK_ENDPOINT")!;
        EnvironmentVariableDefinition provider = catalogue.Lookup("INFISICAL_CLIENT_ID")!;
        EnvironmentVariableDefinition external = catalogue.Lookup("FORMBRICKS_HTTP_PORT")!;
        EnvironmentVariableDefinition secret = catalogue.Lookup("STRIPE_PLATFORM_SECRET_KEY")!;
        EnvironmentVariableDefinition observable = catalogue.Lookup("REPORTING_ENABLED")!;
        EnvironmentVariableDefinition managed = catalogue.Lookup("CONTROL_PLANE_URL")!;
        string[] valueBearingKeys =
        [
            "FORMBRICKS_ENCRYPTION_KEY",
            "LUCKYPENNY_LICENSE_KEY",
            "VAPID_PRIVATE_KEY",
        ];

        await Assert.That(database.Category).IsEqualTo(EnvironmentVariableCategory.Database);
        await Assert.That(database.Requirement).IsEqualTo(EnvironmentVariableRequirement.Defaulted);
        await Assert.That(database.SafeDefault).IsEqualTo("PostgreSql");
        await Assert.That(database.ValidatorId).IsEqualTo("database-provider");
        await Assert.That(database.RestartBehavior).IsEqualTo(EnvironmentRestartBehavior.Process);
        await Assert.That(identity.Category).IsEqualTo(EnvironmentVariableCategory.Identity);
        await Assert.That(identity.Requirement).IsEqualTo(EnvironmentVariableRequirement.Required);
        await Assert.That(identity.Activation.Identifier).IsEqualTo("keycloak-config");
        await Assert.That(provider.Category).IsEqualTo(EnvironmentVariableCategory.Security);
        await Assert.That(provider.Activation.Identifier).IsEqualTo("infisical-config");
        await Assert.That(external.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Startup)).IsFalse();
        await Assert.That(external.ValidatorId).IsEqualTo("port-number");
        await Assert.That(external.Activation.Identifier).IsEqualTo("external-compose-config");
        await Assert.That(secret.Sensitivity).IsEqualTo(EnvironmentVariableSensitivity.Secret);
        await Assert.That(secret.SafeDefault).IsNull();
        await Assert.That(observable.Requirement).IsEqualTo(EnvironmentVariableRequirement.Defaulted);
        await Assert.That(observable.SafeDefault).IsEqualTo("false");
        await Assert.That(observable.RestartBehavior).IsEqualTo(EnvironmentRestartBehavior.None);
        await Assert.That(managed.Activation.Kind).IsEqualTo(EnvironmentActivationKind.All);
        await Assert.That(managed.Activation.Operands.Any(item => item.Kind == EnvironmentActivationKind.Not)).IsTrue();
        await Assert.That(valueBearingKeys.All(key =>
            catalogue.Lookup(key)!.Sensitivity != EnvironmentVariableSensitivity.Public)).IsTrue();
        await Assert.That(catalogue.Definitions.Count(item => item.Requirement == EnvironmentVariableRequirement.Required)).IsGreaterThan(1);
        await Assert.That(catalogue.Definitions.Count(item => item.Requirement == EnvironmentVariableRequirement.Defaulted)).IsGreaterThan(1);
        await Assert.That(catalogue.Definitions.Select(item => item.ValidatorId).Distinct(StringComparer.Ordinal).Count()).IsGreaterThan(5);
        await Assert.That(catalogue.Definitions.Select(item => item.RestartBehavior).Distinct().Count()).IsGreaterThan(2);
    }

    [Test]
    public async Task LocalIdentityCatalogueDefinesTwoAxisProviderAndExternalDatabaseContract()
    {
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        string[] expectedKeys =
        [
            "AUTHENTICATION_PROVIDER",
            "ATPROTO_LOGIN_ENABLED",
            "AUTHENTICATION_LOCAL_JWT_KEY",
            "AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD",
            "AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES",
            "IDENTITY_DATABASE_TOPOLOGY",
            "IDENTITY_DATABASE_PROVIDER",
            "IDENTITY_DATABASE_CONNECTION_STRING",
            "IDENTITY_DATABASE_HOST",
            "IDENTITY_DATABASE_PORT",
            "IDENTITY_DATABASE_NAME",
            "IDENTITY_DATABASE_SCHEMA",
            "IDENTITY_DATABASE_TLS_MODE",
            "IDENTITY_DATABASE_TRUST_SERVER_CERTIFICATE",
            "IDENTITY_DATABASE_RUNTIME_USERNAME",
            "IDENTITY_DATABASE_RUNTIME_PASSWORD",
            "IDENTITY_DATABASE_MIGRATOR_USERNAME",
            "IDENTITY_DATABASE_MIGRATOR_PASSWORD",
        ];
        string[] missing = expectedKeys
            .Where(key => catalogue.Lookup(key) is null)
            .ToArray();

        await Assert.That(missing).IsEmpty()
            .Because("the canonical catalogue must own every local authentication deployment key");

        EnvironmentVariableDefinition provider = catalogue.Lookup("AUTHENTICATION_PROVIDER")!;
        EnvironmentVariableDefinition atproto = catalogue.Lookup("ATPROTO_LOGIN_ENABLED")!;
        EnvironmentVariableDefinition signingKey = catalogue.Lookup("AUTHENTICATION_LOCAL_JWT_KEY")!;
        EnvironmentVariableDefinition topology = catalogue.Lookup("IDENTITY_DATABASE_TOPOLOGY")!;
        EnvironmentVariableDefinition connectionString = catalogue.Lookup("IDENTITY_DATABASE_CONNECTION_STRING")!;

        await Assert.That(provider.Category).IsEqualTo(EnvironmentVariableCategory.Platform);
        await Assert.That(provider.Sensitivity).IsEqualTo(EnvironmentVariableSensitivity.Public);
        await Assert.That(provider.Requirement).IsEqualTo(EnvironmentVariableRequirement.Optional);
        await Assert.That(provider.ValidatorId).IsEqualTo("authentication-provider");
        await Assert.That(atproto.ValidatorId).IsEqualTo("boolean");
        await Assert.That(atproto.SafeDefault).IsNull();
        await Assert.That(signingKey.Category).IsEqualTo(EnvironmentVariableCategory.Security);
        await Assert.That(signingKey.Sensitivity).IsEqualTo(EnvironmentVariableSensitivity.Secret);
        await Assert.That(signingKey.SafeDefault).IsNull();
        await Assert.That(topology.Category).IsEqualTo(EnvironmentVariableCategory.Integration);
        await Assert.That(topology.Requirement).IsEqualTo(EnvironmentVariableRequirement.Defaulted);
        await Assert.That(topology.SafeDefault).IsEqualTo("colocated");
        await Assert.That(topology.ValidatorId).IsEqualTo("identity-database-topology");
        await Assert.That(connectionString.Sensitivity).IsEqualTo(EnvironmentVariableSensitivity.Secret);
        await Assert.That(expectedKeys.All(key =>
            catalogue.Lookup(key)!.RestartBehavior == EnvironmentRestartBehavior.Process)).IsTrue();
    }

    [Test]
    public async Task ConfiguredBootstrapCatalogueHasExactClosedKeysAndValueSafeMetadata()
    {
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        string[] expectedKeys =
        [
            "INSTANCE_BOOTSTRAP_MODE",
            "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER",
            "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT",
            "INSTANCE_BOOTSTRAP_BINDING_GENERATION",
            "INSTANCE_BOOTSTRAP_ADMIN_EMAIL",
            "INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME",
            "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME",
        ];
        EnvironmentVariableDefinition[] definitions = catalogue.Definitions
            .Where(item => item.Key.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(definitions.Select(item => item.Key)).IsEquivalentTo(expectedKeys)
            .Because("configured bootstrap is a closed deployment-local key matrix");

        var expected = new Dictionary<string, (EnvironmentVariableSensitivity Sensitivity,
            EnvironmentVariableRequirement Requirement, string Validator)>(StringComparer.Ordinal)
        {
            ["INSTANCE_BOOTSTRAP_MODE"] = (EnvironmentVariableSensitivity.Public,
                EnvironmentVariableRequirement.Required, "instance-bootstrap-mode"),
            ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = (EnvironmentVariableSensitivity.Public,
                EnvironmentVariableRequirement.Required, "instance-bootstrap-provider"),
            ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = (EnvironmentVariableSensitivity.Sensitive,
                EnvironmentVariableRequirement.Required, "identity-subject"),
            ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = (EnvironmentVariableSensitivity.Public,
                EnvironmentVariableRequirement.Required, "positive-integer"),
            ["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = (EnvironmentVariableSensitivity.Sensitive,
                EnvironmentVariableRequirement.Required, "email-address"),
            ["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = (EnvironmentVariableSensitivity.Sensitive,
                EnvironmentVariableRequirement.Optional, "profile-name"),
            ["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = (EnvironmentVariableSensitivity.Sensitive,
                EnvironmentVariableRequirement.Optional, "profile-name"),
        };

        foreach (EnvironmentVariableDefinition definition in definitions)
        {
            var policy = expected[definition.Key];
            await Assert.That(definition.Category).IsEqualTo(EnvironmentVariableCategory.Identity);
            await Assert.That(definition.Sensitivity).IsEqualTo(policy.Sensitivity);
            await Assert.That(definition.Requirement).IsEqualTo(policy.Requirement);
            await Assert.That(definition.SafeDefault).IsNull();
            await Assert.That(definition.ValidatorId).IsEqualTo(policy.Validator);
            await Assert.That(definition.RestartBehavior).IsEqualTo(EnvironmentRestartBehavior.Process);
            await Assert.That(definition.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Dotenv)).IsTrue();
            await Assert.That(definition.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Startup)).IsTrue();
            await Assert.That(definition.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Compose)).IsFalse();
        }

        var identity = new HashSet<string>(["identity"], StringComparer.Ordinal);
        string[] interactive = catalogue.Relevant(new EnvironmentActivationContext(
                "standalone", identity, ["interactive", "keycloak"]))
            .Select(item => item.Key).Where(expectedKeys.Contains).ToArray();
        string[] keycloak = catalogue.Relevant(new EnvironmentActivationContext(
                "standalone", identity, ["configured-administrator", "keycloak"]))
            .Select(item => item.Key).Where(expectedKeys.Contains).ToArray();
        string[] atproto = catalogue.Relevant(new EnvironmentActivationContext(
                "standalone", identity, ["configured-administrator", "atproto"]))
            .Select(item => item.Key).Where(expectedKeys.Contains).ToArray();

        string[] keycloakKeys =
        [
            "INSTANCE_BOOTSTRAP_MODE", "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER",
            "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "INSTANCE_BOOTSTRAP_BINDING_GENERATION",
        ];

        await Assert.That(interactive).IsEquivalentTo(["INSTANCE_BOOTSTRAP_MODE"]);
        await Assert.That(keycloak).IsEquivalentTo(keycloakKeys);
        await Assert.That(atproto).IsEquivalentTo(expectedKeys);
    }

    [Test]
    public async Task ConfiguredBootstrapGeneratedCatalogueNamesKeysWithoutValues()
    {
        string[] missing = _runtime.MissingCataloguePrerequisites(_repositoryRoot);
        await Assert.That(missing).IsEmpty()
            .Because("the machine catalogue prerequisite must exist before value-safety is inspected");
        if (missing.Length != 0) return;

        string catalogueText = File.ReadAllText(Path.Combine(
            _repositoryRoot, EnvironmentContractExpectedVectors.MachineCatalogueRelativePath));
        string envText = File.ReadAllText(Path.Combine(_repositoryRoot, ".env.example"));
        MachineCatalogue machine = EnvironmentMachineConfiguration.ParseMachineCatalogue(catalogueText);
        MachineEnvironmentFile env = EnvironmentMachineConfiguration.ParseEnvironmentTemplate(envText);
        string[] expectedKeys =
        [
            "INSTANCE_BOOTSTRAP_MODE", "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER",
            "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "INSTANCE_BOOTSTRAP_BINDING_GENERATION",
            "INSTANCE_BOOTSTRAP_ADMIN_EMAIL", "INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME",
            "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME",
        ];

        MachineCatalogueDefinition[] definitions = machine.Definitions
            .Where(item => item.Key.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal))
            .ToArray();
        MachineEnvironmentEntry[] entries = env.Entries
            .Where(item => item.Key.StartsWith("INSTANCE_BOOTSTRAP_", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(definitions.Select(item => item.Key)).IsEquivalentTo(expectedKeys);
        await Assert.That(definitions.All(item => !item.HasSafeDefault)).IsTrue();
        await Assert.That(definitions.All(item => item.GenerationSurfaces == (int)(
            EnvironmentGenerationSurface.Dotenv | EnvironmentGenerationSurface.Startup))).IsTrue();
        await Assert.That(entries.Select(item => item.Key)).IsEquivalentTo(expectedKeys);
        await Assert.That(entries.All(item => item.IsEmptyPlaceholder)).IsTrue();
    }

    [Test]
    public async Task ConfiguredBootstrapSetupBoundaryHasNoRuntimeOrNetworkDependency()
    {
        Assembly assembly = typeof(EnvironmentCatalogue).Assembly;
        string[] referencedAssemblies = assembly.GetReferencedAssemblies()
            .Select(item => item.Name!).Order(StringComparer.Ordinal).ToArray();
        Type[] exportedTypes = assembly.GetExportedTypes();

        await Assert.That(referencedAssemblies.Any(name => name.StartsWith(
            "Explore.", StringComparison.Ordinal))).IsFalse();
        await Assert.That(referencedAssemblies.Any(name => name.Contains(
            "Http", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(exportedTypes.Any(type => type.Namespace?.Contains(
            "SetupLive", StringComparison.Ordinal) == true)).IsFalse();
    }

    [Test]
    public async Task RelevanceUsesTopologyCapabilityAndProviderPolicies()
    {
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        var split = new EnvironmentActivationContext(
            "split",
            ["database", "deployment", "identity", "integration", "security"],
            ["infisical", "keycloak", "postgresql"]);
        var standalone = new EnvironmentActivationContext(
            "standalone",
            ["database", "deployment", "identity", "integration", "security"],
            ["environment", "sqlite"]);
        string[] splitKeys = catalogue.Relevant(split).Select(item => item.Key).ToArray();
        string[] standaloneKeys = catalogue.Relevant(standalone).Select(item => item.Key).ToArray();

        await Assert.That(splitKeys).Contains("DATABASE_PROVIDER");
        await Assert.That(splitKeys).Contains("POSTGRESQL_HOST");
        await Assert.That(splitKeys).Contains("INFISICAL_CLIENT_ID");
        await Assert.That(splitKeys).Contains("KEYCLOAK_ENDPOINT");
        await Assert.That(splitKeys).Contains("FORMBRICKS_HTTP_PORT");
        await Assert.That(splitKeys).Contains("CONTROL_PLANE_URL");
        await Assert.That(standaloneKeys).Contains("DATABASE_PROVIDER");
        await Assert.That(standaloneKeys.Contains("POSTGRESQL_HOST", StringComparer.Ordinal)).IsFalse();
        await Assert.That(standaloneKeys.Contains("INFISICAL_CLIENT_ID", StringComparer.Ordinal)).IsFalse();
        await Assert.That(standaloneKeys.Contains("KEYCLOAK_ENDPOINT", StringComparer.Ordinal)).IsFalse();
        await Assert.That(standaloneKeys.Contains("FORMBRICKS_HTTP_PORT", StringComparer.Ordinal)).IsFalse();
        await Assert.That(standaloneKeys.Contains("CONTROL_PLANE_URL", StringComparer.Ordinal)).IsFalse();
    }

    [Test]
    public async Task RepeatedFeatureReferencesRemainBranchLocalAndDuplicateFeaturesReturnDiagnostics()
    {
        var graph = new EnvironmentActivationGraph(
            ["split"], ["database"], [],
            new Dictionary<string, EnvironmentActivationExpression>(StringComparer.Ordinal)
            {
                ["active"] = EnvironmentActivationExpression.Capability("database"),
            });
        EnvironmentVariableDefinition repeated = Definition(
            "REPEATED_FEATURE",
            EnvironmentActivationExpression.All(
                EnvironmentActivationExpression.Feature("active"),
                EnvironmentActivationExpression.Feature("active")));
        EnvironmentCatalogueResult valid = EnvironmentCatalogue.Create([repeated], graph);
        string[] relevant = valid.Catalogue!.Relevant(new EnvironmentActivationContext(
            "split", ["database"], [])).Select(item => item.Key).ToArray();

        var duplicateGraph = new EnvironmentActivationGraph(
            ["split"], ["database"], [],
            [
                new("duplicate", EnvironmentActivationExpression.Capability("database")),
                new("duplicate", EnvironmentActivationExpression.Capability("database")),
            ]);
        EnvironmentCatalogueResult duplicate = EnvironmentCatalogue.Create([], duplicateGraph);

        await Assert.That(valid.Diagnostics).IsEmpty();
        await Assert.That(relevant).IsEquivalentTo(["REPEATED_FEATURE"]);
        await Assert.That(duplicate.Catalogue).IsNull();
        await Assert.That(duplicate.Diagnostics.Select(item => item.Code))
            .Contains("activation-feature-duplicate");
    }

    [Test]
    public async Task GeneratedMachineCatalogueMatchesDotenvAndComposeNamesOrderAndClassifications()
    {
        if (!_runtime.IsCatalogueComplete(_repositoryRoot)) return;

        string envText = File.ReadAllText(Path.Combine(_repositoryRoot, ".env.example"));
        string composeText = File.ReadAllText(Path.Combine(_repositoryRoot, "docker-compose.yml"));
        string catalogueText = File.ReadAllText(Path.Combine(
            _repositoryRoot, EnvironmentContractExpectedVectors.MachineCatalogueRelativePath));
        MachineEnvironmentFile env = EnvironmentMachineConfiguration.ParseEnvironmentTemplate(envText);
        MachineComposeFile compose = EnvironmentMachineConfiguration.ParseCompose(composeText);
        MachineCatalogue catalogue = EnvironmentMachineConfiguration.ParseMachineCatalogue(catalogueText);
        string[] definitionKeys = catalogue.Definitions.OrderBy(item => item.Order).Select(item => item.Key).ToArray();

        await Assert.That(env.Entries.Select(item => item.Key)).IsEquivalentTo(catalogue.DotenvEnvironmentKeys);
        await Assert.That(env.Entries.Select(item => item.Key).SequenceEqual(
            catalogue.DotenvEnvironmentKeys, StringComparer.Ordinal)).IsTrue();
        await Assert.That(compose.Keys.SequenceEqual(catalogue.ComposeEnvironmentKeys, StringComparer.Ordinal)).IsTrue();
        await Assert.That(compose.RequiredKeys).IsEquivalentTo(catalogue.ComposeRequiredEnvironmentKeys);
        await Assert.That(catalogue.DotenvEnvironmentKeys.All(definitionKeys.Contains)).IsTrue();
        await Assert.That(catalogue.ComposeEnvironmentKeys.All(definitionKeys.Contains)).IsTrue();
        await Assert.That(EnvironmentContractExpectedVectors.SentinelKeys.All(definitionKeys.Contains)).IsTrue();
        await Assert.That(catalogue.Definitions.Any(item => item.Sensitivity == "secret" && item.HasSafeDefault)).IsFalse();
        await Assert.That(catalogue.Definitions.Any(item => item.Sensitivity == "sensitive" && item.HasSafeDefault)).IsFalse();
        await Assert.That(catalogue.Definitions
            .Where(item => item.Requirement == "required" && (item.GenerationSurfaces & 1) != 0)
            .All(item => env.Entries.Any(entry => entry.Key == item.Key))).IsTrue();
        await Assert.That(catalogue.Definitions.All(item =>
            item.ValidatorId.Length > 0
            && item.RestartBehavior.Length > 0
            && item.DocumentationAnchor.StartsWith("environment-", StringComparison.Ordinal)
            && item.GenerationSurfaces > 0)).IsTrue();
        await Assert.That(catalogue.StartupEnvironmentKeys.All(definitionKeys.Contains)).IsTrue();
        await Assert.That(catalogue.StartupEnvironmentKeys.Contains("FORMBRICKS_DATABASE_PASSWORD", StringComparer.Ordinal)).IsFalse();
    }

    [Test]
    public async Task GeneratedMachineCatalogueSecretClassificationMatchesItsRegistryProjection()
    {
        if (!_runtime.IsCatalogueComplete(_repositoryRoot)) return;

        string json = File.ReadAllText(Path.Combine(
            _repositoryRoot, EnvironmentContractExpectedVectors.MachineCatalogueRelativePath));
        MachineCatalogue machine = EnvironmentMachineConfiguration.ParseMachineCatalogue(json);
        CatalogueDefinitionFixture[] definitions = machine.Definitions.Select(item => new CatalogueDefinitionFixture(
            item.Key, item.Category, item.Sensitivity, item.Requirement,
            item.HasSafeDefault ? "redacted-safe-default-present" : null,
            item.Order, ActivationNode.All())).ToArray();

        await Assert.That(EnvironmentInvariantVerifier.VerifySecretParity(
            definitions, machine.SecretBindingEnvironmentKeys)).IsEmpty();
        MachineCatalogueDefinition managed = machine.Definitions.Single(item => item.Key == "CONTROL_PLANE_URL");
        await Assert.That(managed.Activation.Kind).IsEqualTo("all");
        await Assert.That(managed.Activation.Operands.Single(item => item.Kind == "not")
            .Operands.Single().Kind).IsEqualTo("topology");
    }

    private static EnvironmentVariableDefinition Definition(
        string key,
        EnvironmentActivationExpression activation) => new(
            key: key,
            category: EnvironmentVariableCategory.Database,
            sensitivity: EnvironmentVariableSensitivity.Public,
            requirement: EnvironmentVariableRequirement.Optional,
            safeDefault: null,
            order: 0,
            activation: activation,
            validatorId: "database-setting",
            generation: new EnvironmentGenerationPolicy(
                EnvironmentGenerationSurface.Startup, null, null, composeRequired: false),
            restartBehavior: EnvironmentRestartBehavior.Process,
            documentation: new EnvironmentDocumentationMetadata(
                "environment.database.repeated", "environment.help.repeated", "environment-database"));
}

internal sealed record SyntheticLeakingEnvironmentDiagnostic(
    string Code,
    string Path,
    string? Key,
    string Category,
    string SuppliedValue);
