namespace Event.Architecture.Tests;

using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Importing;
using YamlDotNet.RepresentationModel;

public sealed class SetupAssistantArchitectureTests
{
    private const string ManifestSchemaId =
        "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json";
    private const string TenantPackageSchemaId =
        "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json";
    private const string ApiVersion = "configuration.islamu.org/v1alpha2";

    private static readonly string[] RegistryKeys =
    [
        "instance.settings", "instance.documents", "instance.legal_documents", "instance.moderation",
        "tenant.settings", "tenant.documents", "tenant.legal_documents", "tenant.moderation",
        "tenant.footer", "tenant.navigation", "tenant.templates", "tenant.lookups",
        "tenant.custom_property_definitions", "tenant.localization",
        "tenant.registration_policy", "tenant.modules", "extensions",
        "excluded.secrets", "excluded.pii", "excluded.application_data",
        "excluded.operational_state", "excluded.provider_bindings",
        "excluded.deployment_topology"
    ];

    private static readonly string[] ExcludedRegistryKeys =
    [
        "excluded.secrets", "excluded.pii", "excluded.application_data",
        "excluded.operational_state", "excluded.provider_bindings",
        "excluded.deployment_topology"
    ];

    private static readonly string[] BindingProperties =
    [
        "ApplyMode", "ArtifactDigest", "ExpiresAt", "MappingDigest", "ModerationReservationDigest",
        "RequiredApprovalDigest", "SelectedSectionsDigest", "Target",
        "TargetRevisionDigest"
    ];

    [Test]
    public async Task EventPortabilitySchemasRegistryAndImportBindingsRemainAuthoritative()
    {
        ValidateSchema(
            "schemas/configuration-manifest-v1alpha2.schema.json",
            ManifestSchemaId, "ConfigurationManifest");
        ValidateSchema(
            "schemas/tenant-configuration-package-v1alpha2.schema.json",
            TenantPackageSchemaId, "TenantConfigurationPackage");

        await Assert.That(ConfigurationPortabilityRegistry.Sections.Count).IsEqualTo(23);
        await Assert.That(ConfigurationPortabilityRegistry.Sections.Keys)
            .IsEquivalentTo(RegistryKeys);
        foreach (string key in ExcludedRegistryKeys)
        {
            ConfigurationPortabilitySectionDescriptor descriptor =
                ConfigurationPortabilityRegistry.Sections[key];
            await Assert.That(descriptor.Scope).IsEqualTo(ConfigurationPortabilityScope.Excluded);
            await Assert.That(descriptor.Authority).IsEqualTo(ConfigurationPortabilityAuthority.None);
            await Assert.That(descriptor.ArtifactKinds).IsEmpty();
            await Assert.That(new[]
            {
                descriptor.SupportsExport, descriptor.SupportsPreview,
                descriptor.SupportsDiff, descriptor.SupportsApply,
                descriptor.SupportsVerify, descriptor.SupportsRollback,
                descriptor.SupportsDeletion
            }.Any(value => value)).IsFalse();
            await Assert.That(descriptor.OmissionReasonCode).IsNotEmpty();
        }

        string[] artifactProperties = PublicPropertyNames(typeof(ConfigurationImportArtifactReference));
        string[] bindingProperties = PublicPropertyNames(typeof(ConfigurationImportPreviewBinding));
        string[] sessionProperties = PublicPropertyNames(typeof(ConfigurationImportSession));
        await Assert.That(PublicPropertyNames(typeof(ConfigurationImportTarget)))
            .IsEquivalentTo(["AuthorityKey", "Scope", "TenantId"]);
        await Assert.That(artifactProperties)
            .IsEquivalentTo(["ByteLength", "ExpiresAt", "Handle", "Sha256Digest"]);
        await Assert.That(bindingProperties).IsEquivalentTo(BindingProperties);
        await Assert.That(typeof(ConfigurationImportPreviewBinding).GetMethod(
            "Matches", BindingFlags.Public | BindingFlags.Instance)).IsNotNull();
        await Assert.That(typeof(ConfigurationImportSession).GetMethod(
            "MatchesTarget", BindingFlags.Public | BindingFlags.Instance)).IsNotNull();
        await Assert.That(sessionProperties.Concat(artifactProperties)
            .Concat(bindingProperties).Any(name => new[]
            {
                "AccessToken", "ArtifactBytes", "ConnectionString", "Content",
                "FilePath", "Payload", "SecretValue", "SourceTenantId"
            }.Contains(name, StringComparer.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    [Arguments("instance.moderation", ConfigurationPortabilityScope.Instance,
        ConfigurationPortabilityAuthority.InstanceAdministrator)]
    [Arguments("tenant.moderation", ConfigurationPortabilityScope.Tenant,
        ConfigurationPortabilityAuthority.TenantAdministrator)]
    public async Task ModerationPolicySectionsPreserveScopeAndSupportedOperations(
        string key, ConfigurationPortabilityScope scope, ConfigurationPortabilityAuthority authority)
    {
        ConfigurationPortabilitySectionDescriptor descriptor = ConfigurationPortabilityRegistry.Sections[key];
        await Assert.That(descriptor.Scope).IsEqualTo(scope);
        await Assert.That(descriptor.Authority).IsEqualTo(authority);
        await Assert.That(descriptor.PortabilityClass).IsEqualTo(ConfigurationPortabilityClass.Portable);
        ConfigurationPortabilityArtifactKind[] artifacts = scope == ConfigurationPortabilityScope.Instance
            ? [ConfigurationPortabilityArtifactKind.ConfigurationManifest]
            : [ConfigurationPortabilityArtifactKind.ConfigurationManifest,
                ConfigurationPortabilityArtifactKind.TenantConfigurationPackage];
        await Assert.That(descriptor.ArtifactKinds).IsEquivalentTo(artifacts);
        await Assert.That(new[]
        {
            descriptor.SupportsExport, descriptor.SupportsPreview, descriptor.SupportsDiff,
            descriptor.SupportsApply, descriptor.SupportsVerify
        }.All(value => value)).IsTrue();
        await Assert.That(descriptor.SupportsRollback).IsFalse();
        await Assert.That(descriptor.SupportsDeletion).IsFalse();
    }

    [Test]
    public async Task EventSolutionExcludesIntegrationToolingAndExtractedEngineProjects()
    {
        XDocument solution = XDocument.Load(ContextSystemHelpers.RepoPath("Explore.slnx"));
        string[] paths = solution.Descendants("Project")
            .Select(element => (element.Attribute("Path")?.Value ?? string.Empty).Replace('\\', '/'))
            .ToArray();
        await Assert.That(paths.Where(path =>
            path.StartsWith("src/Event.Setup", StringComparison.Ordinal)
            || path.StartsWith("tests/Event.Setup", StringComparison.Ordinal)
            || path.StartsWith("eng/setup-assistant/", StringComparison.Ordinal)
            || path.StartsWith("eng/configurator/", StringComparison.Ordinal)
            || path.Contains("Configurator.Core", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task EventCiRetainsReadOnlyAuthorityAndSerialBackendSecurityLane()
    {
        string caller = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            ".github", "workflows", "test.yml"));
        string reusable = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            ".github", "workflows", "_build-test.yml"));
        string[] violations = ValidateCiGovernance(caller, reusable);
        foreach (string violation in violations)
            Console.Error.WriteLine(violation);
        await Assert.That(violations).IsEmpty();
        await Assert.That(ValidateCiGovernance(
            caller.Replace(
                "src/Explore.Application/Contracts/Secrets/ISetupSecretBindingWriter.cs|",
                string.Empty, StringComparison.Ordinal),
            reusable)).Contains(
                "missing SetupLive route input: src/Explore.Application/Contracts/Secrets/ISetupSecretBindingWriter.cs");
        await Assert.That(ValidateCiGovernance(caller,
            reusable.Replace("--maximum-parallel-tests 1", "--maximum-parallel-tests 8",
                StringComparison.Ordinal))).Contains("serial 35-case SetupLive gate is missing");
        await Assert.That(ValidateCiGovernance(
            caller.Replace("contents: read", "contents: write", StringComparison.Ordinal),
            reusable)).Contains("caller workflow has write authority");
        await Assert.That(ValidateCiGovernance(
            caller.Replace(ForkSafeCoverageToken, "${{ secrets.CODECOV_TOKEN }}",
                StringComparison.Ordinal),
            reusable)).Contains("caller workflow reads secrets");
        await Assert.That(ValidateCiGovernance(caller,
            reusable.Replace("CODECOV_TOKEN:", "PUBLICATION_TOKEN:",
                StringComparison.Ordinal))).Contains("reusable workflow receives secrets");
        await Assert.That(ValidateCiGovernance(
            caller.Replace(ForkSafeCoverageToken, Guid.NewGuid().ToString("N"),
                StringComparison.Ordinal),
            reusable)).Contains("caller workflow receives secrets");
    }

    private const string ForkSafeCoverageToken =
        "${{ github.event_name != 'pull_request' && secrets.CODECOV_TOKEN || github.event.pull_request.head.repo.full_name == github.repository && secrets.CODECOV_TOKEN || '' }}";

    private static string[] ValidateCiGovernance(string caller, string reusable)
    {
        var callerYaml = new YamlStream();
        callerYaml.Load(new StringReader(caller));
        var reusableYaml = new YamlStream();
        reusableYaml.Load(new StringReader(reusable));
        var callerRoot = (YamlMappingNode)callerYaml.Documents.Single().RootNode;
        var reusableRoot = (YamlMappingNode)reusableYaml.Documents.Single().RootNode;
        var violations = new List<string>();
        foreach ((YamlMappingNode root, string name) in new[]
        {
            (callerRoot, "caller"), (reusableRoot, "reusable")
        })
        {
            if (!root.Children.TryGetValue(new YamlScalarNode("permissions"), out YamlNode? permissionNode)
                || permissionNode is not YamlMappingNode permissions
                || !permissions.Children.TryGetValue(new YamlScalarNode("contents"), out YamlNode? contents)
                || contents is not YamlScalarNode { Value: "read" })
                violations.Add($"{name} workflow lacks read-only contents authority");
        }
        ValidateWorkflowAuthority(callerRoot, "caller", violations);
        ValidateWorkflowAuthority(reusableRoot, "reusable", violations);

        YamlMappingNode callerJobs = (YamlMappingNode)callerRoot.Children[new YamlScalarNode("jobs")];
        var detection = (YamlMappingNode)callerJobs.Children[new YamlScalarNode("detect-build-test-changes")];
        var detectionSteps = (YamlSequenceNode)detection.Children[new YamlScalarNode("steps")];
        YamlMappingNode routing = detectionSteps.Children.OfType<YamlMappingNode>().Single(step =>
            step.Children.TryGetValue(new YamlScalarNode("id"), out YamlNode? id)
            && ((YamlScalarNode)id).Value == "changes");
        string routingScript = ((YamlScalarNode)routing.Children[new YamlScalarNode("run")]).Value!;
        string[] routeInputs =
        [
            "configurator/definitions/*", "tests/Event.Wire.Contracts.UnitTests/Fixtures/Configurator/*",
            "src/Explore.Blazor.Client/Services/SetupLive/*",
            "tests/Explore.Blazor.Client.Tests/SetupLive/*",
            "src/Event.Wire.Contracts/SetupLive/*",
            "src/Explore.Domain/SetupLive/*",
            "src/Explore.Application/ApplicationServicesRegistration.cs",
            "src/Explore.Application/Contracts/SetupLive/*",
            "src/Explore.Application/Contracts/Persistence/ISetupLiveRepository.cs",
            "src/Explore.Application/Contracts/Infrastructure/ISetupSecretBindingReadinessReader.cs",
            "src/Explore.Application/Contracts/Secrets/ISetupSecretBindingWriter.cs",
            "src/Explore.Application/Contracts/Secrets/ISetupSecretBindingCommitBarrier.cs",
            "src/Explore.Application/Features/SetupLive/*",
            "src/Explore.Application/Telemetry/SetupLiveTelemetry.cs",
            "src/Explore.Persistence/PersistenceServicesRegistration.cs",
            "src/Explore.Persistence/ExploreDbContext.DbSets.cs",
            "src/Explore.Persistence/ExploreDbContext.QueryFilters.cs",
            "src/Explore.Persistence/Configurations/Entities/SetupLiveConfigurations.cs",
            "src/Explore.Persistence/Repositories/SetupLiveRepository.cs",
            "src/Explore.Persistence/RelationalSetupSecretBindingOperationCoordinator.cs",
            "src/Explore.Persistence/Migrations/*AddSetupLivePersistence*",
            "src/Explore.Persistence/Migrations/ExploreDbContextModelSnapshot.cs",
            "src/Explore.Persistence.Migrations.MySql/Migrations/*AddSetupLivePersistence*",
            "src/Explore.Persistence.Migrations.MySql/Migrations/ExploreDbContextModelSnapshot.cs",
            "src/Explore.Persistence.Migrations.SqlServer/Migrations/*AddSetupLivePersistence*",
            "src/Explore.Persistence.Migrations.SqlServer/Migrations/ExploreDbContextModelSnapshot.cs",
            "src/Explore.Persistence.Migrations.Sqlite/Migrations/*AddSetupLivePersistence*",
            "src/Explore.Persistence.Migrations.Sqlite/Migrations/ExploreDbContextModelSnapshot.cs",
            "src/Explore.Secrets/Services/SetupSecretBindingAuthority.cs",
            "src/Explore.Secrets/Extensions/SecretResolutionServiceCollectionExtensions.cs",
            "src/Explore.Infrastructure/InfrastructureServicesRegistration.cs",
            "src/Explore.Infrastructure/Services/SetupSecretProvider.cs",
            "src/Explore.API/Controllers/SetupTargetEnrollmentsController.cs",
            "src/Explore.API/OpenApi/SetupLiveRequestBodyTransformer.cs",
            "src/Explore.Blazor.Client/Clients/EventApiClients.cs",
            "src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs",
            "tests/Event.API.IntegrationTests/Features/SetupLive*"
        ];
        foreach (string input in routeInputs)
            if (!routingScript.Contains(input, StringComparison.Ordinal))
                violations.Add($"missing SetupLive route input: {input}");

        var jobs = (YamlMappingNode)reusableRoot.Children[new YamlScalarNode("jobs")];
        var fastJob = (YamlMappingNode)jobs.Children[new YamlScalarNode("build-and-test")];
        var steps = (YamlSequenceNode)fastJob.Children[new YamlScalarNode("steps")];
        YamlMappingNode liveStep = steps.Children.OfType<YamlMappingNode>().Single(step =>
            step.Children.TryGetValue(new YamlScalarNode("id"), out YamlNode? id)
            && ((YamlScalarNode)id).Value == "test_setup");
        string liveCommand = ((YamlScalarNode)liveStep.Children[new YamlScalarNode("run")]).Value!;
        string[] liveTokens =
        [
            "tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj",
            "/*/*/*SetupLiveAuthoritySecurityTests/*",
            "--minimum-expected-tests 35 --maximum-parallel-tests 1",
            "--report-trx-filename SetupLiveAuthoritySecurityTests.trx",
            "--coverage-settings eng/coverage/product.config",
            "fast-SetupLiveAuthoritySecurityTests.cobertura.xml",
            "--coverage-output-format cobertura",
            "started=true"
        ];
        if (liveTokens.Any(token => !liveCommand.Contains(token, StringComparison.Ordinal)))
            violations.Add("serial 35-case SetupLive gate is missing");
        if (!((YamlScalarNode)liveStep.Children[new YamlScalarNode("if")]).Value!
            .Contains("inputs.run-setup-tests", StringComparison.Ordinal))
            violations.Add("SetupLive gate is not routed");

        string[] runCommands = steps.Children.OfType<YamlMappingNode>()
            .Where(step => step.Children.ContainsKey(new YamlScalarNode("run")))
            .Select(step => ((YamlScalarNode)step.Children[new YamlScalarNode("run")]).Value!)
            .ToArray();
        if (runCommands.Any(command => command.Contains("eng/setup-assistant/", StringComparison.Ordinal)))
            violations.Add("CI still owns extracted tools");
        YamlMappingNode coverage = steps.Children.OfType<YamlMappingNode>().Single(step =>
            step.Children.TryGetValue(new YamlScalarNode("id"), out YamlNode? id)
            && ((YamlScalarNode)id).Value == "coverage_reports");
        if (!((YamlScalarNode)coverage.Children[new YamlScalarNode("if")]).Value!
            .Contains("steps.test_setup.outputs.started", StringComparison.Ordinal))
            violations.Add("SetupLive coverage owner is missing");
        return [.. violations];
    }

    private static void ValidateWorkflowAuthority(
        YamlNode node, string name, ICollection<string> violations)
    {
        if (node is YamlSequenceNode sequence)
        {
            foreach (YamlNode child in sequence.Children)
                ValidateWorkflowAuthority(child, name, violations);
        }
        else if (node is YamlMappingNode mapping)
        {
            foreach ((YamlNode key, YamlNode value) in mapping.Children)
            {
                string? property = ((YamlScalarNode)key).Value;
                if (property == "permissions" && value is YamlMappingNode permissions
                    && permissions.Children.Values.OfType<YamlScalarNode>()
                        .Any(permission => permission.Value == "write"))
                    violations.Add($"{name} workflow has write authority");
                if (property == "persist-credentials"
                    && value is YamlScalarNode { Value: "true" })
                    violations.Add($"{name} workflow persists credentials");
                if (property == "secrets" && (value is not YamlMappingNode secretMap
                    || secretMap.Children.Count != 1
                    || !secretMap.Children.ContainsKey(new YamlScalarNode("CODECOV_TOKEN"))))
                    violations.Add($"{name} workflow receives secrets");
                if (property == "CODECOV_TOKEN" && value is YamlMappingNode declaration
                    && (declaration.Children.Count != 1
                        || !declaration.Children.TryGetValue(
                            new YamlScalarNode("required"), out YamlNode? required)
                        || required is not YamlScalarNode { Value: "false" }))
                    violations.Add($"{name} workflow requires coverage credentials");
                if (property == "CODECOV_TOKEN" && value is YamlScalarNode token
                    && token.Value != ForkSafeCoverageToken)
                    violations.Add($"{name} workflow receives secrets");
                ValidateWorkflowAuthority(value, name, violations);
            }
        }
        else if (node is YamlScalarNode scalar
            && scalar.Value is string expression
            && expression.Contains("${{", StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(
                expression, @"(?<![\w.])secrets\s*(?:\.|\[)")
            && scalar.Value != ForkSafeCoverageToken)
            violations.Add($"{name} workflow reads secrets");
    }

    private static void ValidateSchema(string repositoryPath, string schemaId, string kind)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(
            ContextSystemHelpers.RepoPath(repositoryPath.Split('/'))));
        JsonElement root = document.RootElement;
        if (root.GetProperty("$schema").GetString() != "https://json-schema.org/draft/2020-12/schema"
            || root.GetProperty("$id").GetString() != schemaId
            || root.GetProperty("type").GetString() != "object"
            || root.GetProperty("additionalProperties").ValueKind != JsonValueKind.False
            || root.GetProperty("properties").GetProperty("$schema").GetProperty("const").GetString() != schemaId
            || root.GetProperty("properties").GetProperty("apiVersion").GetProperty("const").GetString() != ApiVersion
            || root.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() != kind
            || !AllTypedObjectsAreClosed(root))
            throw new InvalidOperationException($"Event portability schema drifted: {repositoryPath}");
    }

    private static bool AllTypedObjectsAreClosed(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String && type.GetString() == "object"
                && (!element.TryGetProperty("additionalProperties", out JsonElement additional)
                    || additional.ValueKind != JsonValueKind.False))
                return false;
            return element.EnumerateObject().All(property => AllTypedObjectsAreClosed(property.Value));
        }
        return element.ValueKind != JsonValueKind.Array
            || element.EnumerateArray().All(AllTypedObjectsAreClosed);
    }

    private static string[] PublicPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}
