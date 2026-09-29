// ABOUTME: Defines Setup Assistant project, dependency, capability, ratchet, and CI governance boundaries.
// ABOUTME: Enforces package-free scaffolds, read-only routing, and source-versus-output tracking semantics.

namespace Event.Architecture.Tests;

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Importing;

public sealed class SetupAssistantArchitectureTests
{
    private const string CommunityToolkitContentHash =
        "WadCzGEc2U+3e20avRLng4qNtt4zoOGWrdUISqJWrHe3/FSnrYjuM5Sb4yQb09LhkBXrrI4Zt3dLKgRMbItsrg==";
    private const string BrowserCapabilityPath =
        "eng/setup-assistant/generated/browser-release-capabilities.json";
    private const string SetupLiveCapabilityPath =
        "eng/setup-assistant/generated/setup-live-release-capabilities.json";
    private const string FrozenContractBaselinePath =
        "eng/setup-assistant/generated/frozen-contract-baseline.json";
    private const string ManifestSchemaId =
        "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json";
    private const string TenantPackageSchemaId =
        "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json";
    private const string ApiVersion = "configuration.islamu.org/v1alpha2";

    private static readonly string[] RegistryKeys =
    [
        "instance.settings", "instance.documents", "instance.legal_documents",
        "tenant.settings", "tenant.documents", "tenant.legal_documents",
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

    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Event.Setup.Core"] = ["Event.Wire.Contracts"],
            ["Event.SetupAssistant"] = ["Event.Setup.Core"],
            ["Event.SetupAssistant.SetupLive"] = ["Event.Setup.Core", "Explore.Blazor.Client"],
            ["Event.SetupAssistant.Browser"] = ["Event.SetupAssistant"],
            ["Event.SetupAssistant.Desktop"] = ["Event.SetupAssistant"],
            ["Event.SetupAssistant.Terminal"] = ["Event.SetupAssistant", "Event.Setup.Core"],
            ["Event.SetupAssistant.Cli"] = ["Event.Setup.Core"],
            ["SetupCliCommandSchemaGenerator"] = ["Event.SetupAssistant.Cli"],
            ["Event.Setup.Core.Tests"] = ["Event.Setup.Core"],
            ["Event.SetupAssistant.Tests"] = ["Event.SetupAssistant", "Event.SetupAssistant.SetupLive"],
            ["Event.SetupAssistant.Browser.Tests"] = ["Event.SetupAssistant.Browser"],
            ["Event.SetupAssistant.Desktop.Tests"] = ["Event.SetupAssistant.Desktop"],
            ["Event.SetupAssistant.Terminal.Tests"] = ["Event.SetupAssistant.Terminal"],
            ["Event.SetupAssistant.Cli.Tests"] = ["Event.SetupAssistant.Cli"]
        };

    private static readonly IReadOnlyDictionary<string, string> RequiredSolutionProjects =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Event.Setup.Core"] = "src/Event.Setup.Core/Event.Setup.Core.csproj",
            ["Event.SetupAssistant"] = "src/Event.SetupAssistant/Event.SetupAssistant.csproj",
            ["Event.SetupAssistant.SetupLive"] = "src/Event.SetupAssistant/SetupLive/Event.SetupAssistant.SetupLive.csproj",
            ["Event.SetupAssistant.Browser"] = "src/Event.SetupAssistant.Browser/Event.SetupAssistant.Browser.csproj",
            ["Event.SetupAssistant.Desktop"] = "src/Event.SetupAssistant.Desktop/Event.SetupAssistant.Desktop.csproj",
            ["Event.SetupAssistant.Terminal"] = "src/Event.SetupAssistant.Terminal/Event.SetupAssistant.Terminal.csproj",
            ["Event.SetupAssistant.Cli"] = "src/Event.SetupAssistant.Cli/Event.SetupAssistant.Cli.csproj",
            ["SetupCliCommandSchemaGenerator"] = "eng/setup-assistant/SetupCliCommandSchemaGenerator/SetupCliCommandSchemaGenerator.csproj",
            ["Event.Setup.Core.Tests"] = "tests/Event.Setup.Core.Tests/Event.Setup.Core.Tests.csproj",
            ["Event.SetupAssistant.Tests"] = "tests/Event.SetupAssistant.Tests/Event.SetupAssistant.Tests.csproj",
            ["Event.SetupAssistant.Browser.Tests"] = "tests/Event.SetupAssistant.Browser.Tests/Event.SetupAssistant.Browser.Tests.csproj",
            ["Event.SetupAssistant.Desktop.Tests"] = "tests/Event.SetupAssistant.Desktop.Tests/Event.SetupAssistant.Desktop.Tests.csproj",
            ["Event.SetupAssistant.Terminal.Tests"] = "tests/Event.SetupAssistant.Terminal.Tests/Event.SetupAssistant.Terminal.Tests.csproj",
            ["Event.SetupAssistant.Cli.Tests"] = "tests/Event.SetupAssistant.Cli.Tests/Event.SetupAssistant.Cli.Tests.csproj"
        };

    private static readonly string[] ApprovedTestPackages =
        ["TUnit", "bunit", "NSubstitute", "Verify.TUnit"];

    private static readonly string[] BlockedPackageTerms =
        ["TextMateSharp", "Avalonia", "Sharprompt"];

    private static readonly string[] ForbiddenPresentationClosureTerms =
    [
        "DependencyInjection", "Microsoft.Extensions.Hosting", "Avalonia", "Terminal.Gui",
        "System.IO", "System.Net", "HttpClient", "Socket", "Telemetry", "OpenTelemetry",
        "ApplicationInsights", "Persistence", "EntityFramework", "Serializer",
        "System.Text.Json", "Newtonsoft", "ServiceProvider", "ServiceLocator",
        "Infisical", "HashiCorp", "AWSSDK", "Amazon.", "Azure.", "Google.Cloud",
        "Stripe", "PayPal", "Braintree", "Adyen"
    ];

    private static readonly string[] ForbiddenSourcePackageTerms =
    [
        "EntityFrameworkCore", "MediatR", "Infisical", "KeyVault",
        "SecretsManager", "HashiCorp.Vault", "AWSSDK", "Amazon.", "Azure.",
        "Google.Cloud", "Stripe", "PayPal", "Braintree", "Adyen", "HttpClient",
        "Refit", "RestSharp", "Flurl", "Grpc.Net.Client", "GraphQL.Client",
        "WebSocket", "OpenTelemetry", "ApplicationInsights", "Sentry", "NewRelic",
        "Datadog", "Analytics", "Telemetry", "Microsoft.Extensions.AI", "OpenAI",
        "Anthropic", "SemanticKernel", "ML.NET", "Onnx", "ModelContextProtocol",
        "Avalonia.Diagnostics", "Avalonia.Designer", "Avalonia.Professional",
        "Avalonia.Commercial"
    ];

    [Test]
    public async Task PlannedProjects_MustExistInSolutionWithExactInwardDependencies()
    {
        XDocument solution = XDocument.Load(ContextSystemHelpers.RepoPath("Explore.slnx"));
        string[] solutionPaths = solution.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => NormalizePath(element.Attribute("Path")?.Value ?? string.Empty))
            .ToArray();
        string[] missing = RequiredSolutionProjects.Values
            .Where(required => solutionPaths.Count(actual =>
                string.Equals(actual, required, StringComparison.Ordinal)) != 1)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "Missing planned Setup source/test projects in Explore.slnx: "
                + string.Join(", ", missing));
        }

        var projects = RequiredSolutionProjects.ToDictionary(
            pair => pair.Key,
            pair => XDocument.Load(ContextSystemHelpers.RepoPath(
                pair.Value.Split('/', StringSplitOptions.RemoveEmptyEntries))),
            StringComparer.Ordinal);
        string[] violations =
        [
            .. ValidateProjectMetadata(projects),
            .. ValidateSetupLiveProjectSplit(projects)
        ];

        await Assert.That(violations).IsEmpty()
            .Because("Setup projects must preserve the exact offline inward graph and approved package boundary: "
                + string.Join("; ", violations));
    }

    [Test]
    public async Task SetupLocks_MustExistAndExcludeBlockedUiPackages()
    {
        var violations = new List<string>();
        foreach (string projectPath in RequiredSolutionProjects.Values)
        {
            string relativeLockPath =
                $"{NormalizePath(Path.GetDirectoryName(projectPath)!)}/packages.lock.json";
            string lockPath = ContextSystemHelpers.RepoPath(
                relativeLockPath.Split('/', StringSplitOptions.RemoveEmptyEntries));
            if (!File.Exists(lockPath))
            {
                violations.Add($"missing lock: {relativeLockPath}");
                continue;
            }

            string lockContent = await File.ReadAllTextAsync(lockPath);
            violations.AddRange(BlockedPackageTerms
                .Where(term => lockContent.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(term => $"blocked package term {term} in {relativeLockPath}"));
            using JsonDocument lockDocument = JsonDocument.Parse(lockContent);
            if (FindJsonPropertyNames(lockDocument.RootElement).Contains(
                    "Terminal.Gui", StringComparer.OrdinalIgnoreCase))
                violations.Add($"official Terminal.Gui package in {relativeLockPath}");
        }

        await Assert.That(violations).IsEmpty()
            .Because("all Setup locks must remain discoverable and package-free of blocked UI graphs: "
                + string.Join("; ", violations));
    }

    [Test]
    public async Task DisabledPresentationTargetsMustRemainMachineDisabledAndGraphAbsent()
    {
        string[] disabledShells =
        [
            "src/Event.SetupAssistant.Browser/Event.SetupAssistant.Browser.csproj",
            "src/Event.SetupAssistant.Desktop/Event.SetupAssistant.Desktop.csproj"
        ];
        var violations = new List<string>();
        foreach (string relativePath in disabledShells)
        {
            string path = ContextSystemHelpers.RepoPath(
                relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
            XDocument project = XDocument.Load(path);
            string? declared = project.Descendants()
                .SingleOrDefault(element => element.Name.LocalName == "SetupTargetEnabled")?.Value;
            if (!string.Equals(declared, "false", StringComparison.OrdinalIgnoreCase))
                violations.Add($"SetupTargetEnabled is not false: {relativePath}");

            ProcessResult evaluated = RunProcess(
                "dotnet",
                ["msbuild", path, "-getProperty:SetupTargetEnabled", "-nologo"],
                ContextSystemHelpers.RepoPath());
            if (evaluated.ExitCode != 0
                || !string.Equals(evaluated.Output.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                violations.Add($"evaluated SetupTargetEnabled is not false: {relativePath}");
        }

        string sourceRoot = ContextSystemHelpers.RepoPath("src");
        string[] targetProjectCanaries = ["Event.SetupAssistant.Avalonia"];
        foreach (string target in targetProjectCanaries)
        {
            if (Directory.Exists(Path.Combine(sourceRoot, target)))
                violations.Add($"disabled target project exists: {target}");
        }

        foreach (string projectPath in Directory.GetFiles(
            sourceRoot,
            "*.csproj",
            SearchOption.AllDirectories).Where(path =>
                Path.GetFileNameWithoutExtension(path).StartsWith(
                    "Event.SetupAssistant",
                    StringComparison.Ordinal)))
        {
            XDocument project = XDocument.Load(projectPath);
            IEnumerable<string> graphIdentities = project.Descendants()
                .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? string.Empty);
            violations.AddRange(graphIdentities
                .Where(identity => BlockedPackageTerms.Skip(1).Any(term =>
                    identity.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .Select(identity => $"disabled target reference exists: {identity}"));

            string lockPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "packages.lock.json");
            using JsonDocument lockDocument = JsonDocument.Parse(File.ReadAllBytes(lockPath));
            violations.AddRange(FindJsonPropertyNames(lockDocument.RootElement)
                .Where(identity => BlockedPackageTerms.Skip(1).Any(term =>
                    identity.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .Select(identity => $"disabled target lock node exists: {identity}"));
        }

        await Assert.That(violations).IsEmpty()
            .Because("SA518-DISABLED-TARGET-BOUNDARY: Avalonia shared/browser/desktop targets must remain disabled, absent, and non-resolvable: "
                + string.Join("; ", violations));
    }

    [Test]
    public async Task SetupAssistantPresentationGraphMustMatchExactProjectLockPinAndAssemblyClosure()
    {
        XDocument project = XDocument.Load(ContextSystemHelpers.RepoPath(
            "src", "Event.SetupAssistant", "Event.SetupAssistant.csproj"));
        XDocument central = XDocument.Load(ContextSystemHelpers.RepoPath(
            "Directory.Packages.props"));
        using JsonDocument lockDocument = JsonDocument.Parse(File.ReadAllBytes(
            ContextSystemHelpers.RepoPath("src", "Event.SetupAssistant", "packages.lock.json")));
        string assemblyPath = ContextSystemHelpers.RepoPath(
            "src", "Event.SetupAssistant", "bin", "Release", "net10.0",
            "Event.SetupAssistant.dll");
        var violations = new List<string>();
        violations.AddRange(ValidatePresentationProject(project, central));
        violations.AddRange(ValidatePresentationLock(lockDocument.RootElement));
        violations.AddRange(ValidatePresentationAssembly(assemblyPath));

        await Assert.That(violations).IsEmpty()
            .Because("SA518-GRAPH-RATCHET: Event.SetupAssistant must close over exactly Core, YamlDotNet 18.1.0, and CommunityToolkit.Mvvm 8.4.2: "
                + string.Join("; ", violations));
    }

    [Test]
    public async Task SetupLiveOuterAdapterMustRemainTransportOnlyAndPersistenceFree()
    {
        string assemblyPath = ContextSystemHelpers.RepoPath(
            "src", "Event.SetupAssistant", "SetupLive", "bin", "Release", "net10.0",
            "Event.SetupAssistant.SetupLive.dll");
        if (!File.Exists(assemblyPath))
            throw new InvalidOperationException("compiled Setup live outer adapter is missing");

        using FileStream stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        string[] references = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .ToArray();
        string[] forbiddenReferences = references.Where(reference =>
            reference.Contains("Persistence", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Secrets", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("Logging", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] forbiddenTypes = FindForbiddenSetupLiveOuterTypes(metadata.TypeReferences
            .Select(handle => metadata.GetTypeReference(handle))
            .Select(reference =>
                $"{metadata.GetString(reference.Namespace)}.{metadata.GetString(reference.Name)}"));

        await Assert.That(forbiddenReferences.Concat(forbiddenTypes)).IsEmpty()
            .Because("the live outer adapter may transport generated Setup calls but cannot own persistence, credentials, logs, process state, or telemetry");
    }

    [Test]
    public async Task SetupLiveOuterAdapterRatchetRejectsPersistenceAndConsoleCanaries()
    {
        string[] violations = FindForbiddenSetupLiveOuterTypes(
            [
                "System.Console",
                "System.IO.StreamWriter",
                "System.Data.Common.DbConnection",
                "System.Data.Common.DbCommand"
            ]);

        await Assert.That(violations).IsEquivalentTo(
            [
                "System.Console",
                "System.IO.StreamWriter",
                "System.Data.Common.DbConnection",
                "System.Data.Common.DbCommand"
            ]);
    }

    [Test]
    public async Task PresentationGraphVerifiersMustRejectStructuredXmlAndJsonCanaries()
    {
        XDocument safeProject = XDocument.Parse(
            """
            <Project><ItemGroup>
              <ProjectReference Include="../Event.Setup.Core/Event.Setup.Core.csproj" />
              <PackageReference Include="CommunityToolkit.Mvvm" />
            </ItemGroup></Project>
            """);
        XDocument unsafeProject = XDocument.Parse(
            """
            <Project><ItemGroup>
              <ProjectReference Include="../Event.Setup.Core/Event.Setup.Core.csproj" />
              <PackageReference Include="CommunityToolkit.Mvvm" />
              <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
            </ItemGroup></Project>
            """);
        XDocument central = XDocument.Parse(
            """<Project><ItemGroup><PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" /></ItemGroup></Project>""");
        using JsonDocument safeLock = JsonDocument.Parse(
            """
            {"dependencies":{"net10.0":{"event.setup.core":{"type":"Project","dependencies":{"Event.Wire.Contracts":"[1.0.0, )","YamlDotNet":"[18.1.0, )"}},"event.wire.contracts":{"type":"Project"},"CommunityToolkit.Mvvm":{"type":"Direct","requested":"[8.4.2, )","resolved":"8.4.2","contentHash":"WadCzGEc2U+3e20avRLng4qNtt4zoOGWrdUISqJWrHe3/FSnrYjuM5Sb4yQb09LhkBXrrI4Zt3dLKgRMbItsrg=="},"YamlDotNet":{"type":"CentralTransitive","requested":"[18.1.0, )","resolved":"18.1.0","contentHash":"5K+9KFg2TdTl7VXv88Qzi/0lqK6JFoNP3lRuImPYGRV7K/QYklDyTrj4+A+KAki1JsQi6qKY+hDyY7d6WRqjrw=="}}}}
            """);
        using JsonDocument unsafeLock = JsonDocument.Parse(
            """
            {"dependencies":{"net10.0":{"event.setup.core":{"type":"Project","dependencies":{"Event.Wire.Contracts":"[1.0.0, )"}},"event.wire.contracts":{"type":"Project"},"CommunityToolkit.Mvvm":{"type":"Direct","requested":"[8.4.2, )","resolved":"8.4.2","dependencies":{"Microsoft.Extensions.DependencyInjection":"10.0.10"}},"Microsoft.Extensions.DependencyInjection":{"type":"Transitive","resolved":"10.0.10"}}}}
            """);

        await Assert.That(ValidatePresentationProject(safeProject, central)).IsEmpty();
        await Assert.That(ValidatePresentationProject(unsafeProject, central)).Contains(
            "PackageReferences must be exactly CommunityToolkit.Mvvm");
        await Assert.That(ValidatePresentationLock(safeLock.RootElement)).IsEmpty();
        await Assert.That(ValidatePresentationLock(unsafeLock.RootElement)).IsNotEmpty();
    }

    [Test]
    public async Task BrowserReleaseCapability_MustExistAndDisableSecretsByDefault()
    {
        string path = ContextSystemHelpers.RepoPath(
            BrowserCapabilityPath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Missing planned Setup browser release-capability artifact: {BrowserCapabilityPath}");
        }

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        string[] violations = ValidateBrowserCapability(document.RootElement);
        await Assert.That(violations).IsEmpty()
            .Because("The generated browser release capability must fail closed: "
                + string.Join("; ", violations));
    }

    [Test]
    public async Task SetupLiveReleaseCapability_MustExistAndRemainDisabledThroughD2Closure()
    {
        string path = ContextSystemHelpers.RepoPath(
            SetupLiveCapabilityPath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Missing Setup live release-capability artifact: {SetupLiveCapabilityPath}");
        }

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        string[] violations = ValidateSetupLiveCapability(document.RootElement);
        await Assert.That(violations).IsEmpty()
            .Because("D2-11 must close without activating an unfinished live-control target: "
                + string.Join("; ", violations));
    }

    [Test]
    public async Task FrozenContractBaseline_MustExistAndIdentifyTheExtractionBoundary()
    {
        string path = ContextSystemHelpers.RepoPath(
            FrozenContractBaselinePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Missing planned Setup frozen-contract baseline artifact: {FrozenContractBaselinePath}");
        }

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        string[] violations = ValidateFrozenContractBaseline(document.RootElement);
        await Assert.That(violations).IsEmpty()
            .Because("The generated Setup extraction baseline must identify the frozen public contract: "
                + string.Join("; ", violations));
    }

    [Test]
    public async Task CurrentFrozenBaseline_MustRemainHealthyBeforeExtraction()
    {
        ValidateSchema(
            "schemas/configuration-manifest-v1alpha2.schema.json",
            ManifestSchemaId,
            "ConfigurationManifest");
        ValidateSchema(
            "schemas/tenant-configuration-package-v1alpha2.schema.json",
            TenantPackageSchemaId,
            "TenantConfigurationPackage");

        await Assert.That(ConfigurationPortabilityRegistry.Sections.Count).IsEqualTo(21);
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

        string[] targetProperties = PublicPropertyNames(typeof(ConfigurationImportTarget));
        string[] artifactProperties = PublicPropertyNames(
            typeof(ConfigurationImportArtifactReference));
        string[] bindingProperties = PublicPropertyNames(
            typeof(ConfigurationImportPreviewBinding));
        string[] sessionProperties = PublicPropertyNames(typeof(ConfigurationImportSession));

        await Assert.That(targetProperties)
            .IsEquivalentTo(["AuthorityKey", "Scope", "TenantId"]);
        await Assert.That(artifactProperties)
            .IsEquivalentTo(["ByteLength", "ExpiresAt", "Handle", "Sha256Digest"]);
        await Assert.That(bindingProperties).IsEquivalentTo(BindingProperties);
        await Assert.That(typeof(ConfigurationImportPreviewBinding).GetMethod(
            "Matches", BindingFlags.Public | BindingFlags.Instance)).IsNotNull();
        await Assert.That(typeof(ConfigurationImportSession).GetMethod(
            "MatchesTarget", BindingFlags.Public | BindingFlags.Instance)).IsNotNull();
        await Assert.That(sessionProperties.Concat(artifactProperties)
            .Concat(bindingProperties).Any(IsValueBearingPropertyName)).IsFalse();
    }

    [Test]
    public async Task ProjectBoundaryVerifier_MustAcceptCompliantAndRejectForbiddenMetadata()
    {
        IReadOnlyDictionary<string, XDocument> compliant = CreateCompliantFixture();
        var bad = compliant.ToDictionary(
            pair => pair.Key,
            pair => XDocument.Parse(pair.Value.ToString()),
            StringComparer.Ordinal);
        bad["Event.Setup.Core"] = XDocument.Parse(
            """
            <Project><ItemGroup>
              <ProjectReference Include="../Explore.API/Explore.API.csproj" />
              <PackageReference Include="OpenTelemetry" />
            </ItemGroup></Project>
            """);
        bad["Event.Setup.Core.Tests"] = XDocument.Parse(
            """
            <Project><ItemGroup>
              <ProjectReference Include="../../src/Event.SetupAssistant/Event.SetupAssistant.csproj" />
              <PackageReference Include="TUnit" />
            </ItemGroup></Project>
            """);

        string[] compliantViolations = ValidateProjectMetadata(compliant);
        string[] badViolations = ValidateProjectMetadata(bad);

        await Assert.That(compliantViolations).IsEmpty();
        await Assert.That(badViolations.Any(value =>
            value.Contains("Event.Setup.Core -> Explore.API", StringComparison.Ordinal))).IsTrue();
        await Assert.That(badViolations.Any(value =>
            value.Contains("PackageReference Event.Setup.Core: OpenTelemetry", StringComparison.Ordinal))).IsTrue();
        await Assert.That(badViolations.Any(value =>
            value.Contains("Event.Setup.Core.Tests -> Event.SetupAssistant", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task StructuredRatchetVerifiers_MustAcceptClosedAndRejectUnsafeFixtures()
    {
        const string metadata =
            """
            "_metadata":{"about":["ABOUTME: Generated Setup Assistant architecture ratchet; do not edit by hand.","ABOUTME: Owned by eng/setup-assistant/GenerateSetupAssistantRatchets.cs."],"generatedBy":"eng/setup-assistant/GenerateSetupAssistantRatchets.cs"}
            """;
        using JsonDocument safeCapability = JsonDocument.Parse(
            "{" + metadata + ""","schemaVersion":1,"target":"browser","targetEnabled":false,"capabilities":{"secretEntry":false}}""");
        using JsonDocument unsafeCapability = JsonDocument.Parse(
            """{"schemaVersion":1,"target":"browser","targetEnabled":true,"capabilities":{"secretEntry":true}}""");
        using JsonDocument safeLiveCapability = JsonDocument.Parse(
            "{" + metadata + ""","schemaVersion":1,"target":"setup-live","targetEnabled":false,"capabilities":{"targetEnrollment":false,"secretBindingReadiness":false,"secretBindingWrite":false,"savedProfiles":false}}""");
        using JsonDocument unsafeLiveCapability = JsonDocument.Parse(
            "{" + metadata + ""","schemaVersion":1,"target":"setup-live","targetEnabled":false,"activationAuthorized":true,"capabilities":{"targetEnrollment":false,"secretBindingReadiness":false,"secretBindingWrite":true,"savedProfiles":false}}""");
        using JsonDocument safeBaseline = JsonDocument.Parse(CreateBaselineFixture(21, true));
        using JsonDocument unsafeBaseline = JsonDocument.Parse(CreateBaselineFixture(22, false));

        await Assert.That(ValidateBrowserCapability(safeCapability.RootElement)).IsEmpty();
        await Assert.That(ValidateBrowserCapability(unsafeCapability.RootElement))
            .Contains("capabilities.secretEntry must be false");
        await Assert.That(ValidateSetupLiveCapability(safeLiveCapability.RootElement)).IsEmpty();
        await Assert.That(ValidateSetupLiveCapability(unsafeLiveCapability.RootElement))
            .Contains("capabilities.secretBindingWrite must be false");
        await Assert.That(ValidateSetupLiveCapability(unsafeLiveCapability.RootElement))
            .Contains("root properties must match the exact generated set");
        await Assert.That(ValidateFrozenContractBaseline(safeBaseline.RootElement)).IsEmpty();
        await Assert.That(ValidateFrozenContractBaseline(unsafeBaseline.RootElement)).IsNotEmpty();
    }

    [Test]
    public async Task SetupCiGovernance_MustRouteReadOnlyChecksAndRejectUnsafeFixtures()
    {
        string caller = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            ".github", "workflows", "test.yml"));
        string reusable = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            ".github", "workflows", "_build-test.yml"));
        string[] actual = ValidateCiGovernance(caller, reusable);
        string[] unsafeFixture = ValidateCiGovernance(
            "permissions:\n  contents: write\nsecrets: inherit\n",
            "permissions:\n  id-token: write\npersist-credentials: true\n--write\n");
        string[] missingOwnerFixture = ValidateCiGovernance(
            caller.Replace(
                "src/Explore.Application/Contracts/Secrets/ISetupSecretBindingWriter.cs|",
                string.Empty,
                StringComparison.Ordinal),
            reusable);

        await Assert.That(actual).IsEmpty()
            .Because("Setup CI must be read-only, always routed, and non-mutating: "
                + string.Join("; ", actual));
        await Assert.That(unsafeFixture).IsNotEmpty();
        await Assert.That(missingOwnerFixture)
            .Contains("missing SetupLive route input: src/Explore.Application/Contracts/Secrets/ISetupSecretBindingWriter.cs");
    }

    [Test]
    public async Task PatchedTerminalGuiPackage_MustRemainAuditedAndGrammarFree()
    {
        ProcessResult result = RunProcess(
            "dotnet",
            ["run", "eng/release/dependencies/terminal-gui/VerifyTerminalGuiPackage.cs", "--", "--check"],
            ContextSystemHelpers.RepoPath());

        await Assert.That(result.ExitCode).IsEqualTo(0)
            .Because(result.Output);
    }

    [Test]
    public async Task SetupRepositoryPaths_MustTrackInputsAndIgnoreOnlyGeneratedOutputs()
    {
        string[] trackablePaths =
        [
            "src/Event.Setup.Core/Event.Setup.Core.csproj",
            "src/Event.SetupAssistant/Event.SetupAssistant.csproj",
            "src/Event.SetupAssistant.Browser/Event.SetupAssistant.Browser.csproj",
            "src/Event.SetupAssistant.Desktop/Event.SetupAssistant.Desktop.csproj",
            "src/Event.SetupAssistant.Terminal/Event.SetupAssistant.Terminal.csproj",
            "src/Event.SetupAssistant.Cli/Program.cs",
            "tests/Event.Setup.Core.Tests/Program.cs",
            .. RequiredSolutionProjects.Values.Select(path =>
                $"{NormalizePath(Path.GetDirectoryName(path)!)}/packages.lock.json"),
            "eng/setup-assistant/GenerateSetupAssistantRatchets.cs",
            "eng/setup-assistant/SetupCliCommandSchemaGenerator/Program.cs",
            BrowserCapabilityPath,
            FrozenContractBaselinePath,
            "eng/release/dependencies/terminal-gui/source.json",
            "eng/release/dependencies/terminal-gui/approval.json",
            "eng/release/dependencies/terminal-gui/patches/0001-remove-textmate-grammars.patch",
            "eng/release/dependencies/terminal-gui/feed/ISLAMU.Terminal.Gui.2.4.17-islamu.1.nupkg",
            "src/Event.SetupAssistant.Browser/wwwroot/index.html"
        ];
        string[] ignoredPaths =
        [
            "src/Event.SetupAssistant/bin/Release/net10.0/output.dll",
            "src/Event.SetupAssistant/obj/project.assets.json",
            "src/Event.SetupAssistant.Browser/bin/Release/net10.0/wwwroot/index.html",
            "src/Event.SetupAssistant.Browser/publish/site.zip",
            "src/Event.SetupAssistant/packages/staging/package.nupkg",
            "releases/setup-assistant.zip",
            "artifacts/setup-assistant/report.json"
        ];

        string[] unexpectedlyIgnored = trackablePaths.Where(IsIgnoredByGit).ToArray();
        string[] unexpectedlyTrackable = ignoredPaths.Where(path => !IsIgnoredByGit(path)).ToArray();

        await Assert.That(unexpectedlyIgnored).IsEmpty()
            .Because("Setup source, locks, generator inputs, ratchets, and future browser source must remain trackable: "
                + string.Join(", ", unexpectedlyIgnored));
        await Assert.That(unexpectedlyTrackable).IsEmpty()
            .Because("Setup build, publish, staging, release, and artifact outputs must stay ignored: "
                + string.Join(", ", unexpectedlyTrackable));
    }

    [Test]
    public async Task DependencyLicenseCommand_MustDiscoverNestedSetupLocks()
    {
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(), $"event-sa130-license-{Guid.NewGuid():N}");
        string nested = Path.Combine(temporaryRoot, "src", "Event.Setup.Future");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(
            Path.Combine(nested, "packages.lock.json"),
            """
            {"version":1,"dependencies":{"net10.0":{"SA130.LockDiscovery.Probe":{"type":"Direct","requested":"[1.0.0, )","resolved":"1.0.0","contentHash":""}}}}
            """);

        try
        {
            ProcessResult result = RunProcess(
                "dotnet",
                ["run", ContextSystemHelpers.RepoPath(".ci", "scripts",
                    "validate-dependency-license-policy.cs"), "--", temporaryRoot],
                ContextSystemHelpers.RepoPath());

            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("SA130.LockDiscovery.Probe 1.0.0");
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public async Task SetupTerminalBoundary_MustHaveOneFrameworkTargetAndNoCliFallback()
    {
        var violations = new List<string>();
        string cliTuiPath = ContextSystemHelpers.RepoPath("src", "Event.SetupAssistant.Cli", "Tui");
        if (Directory.Exists(cliTuiPath)
            && Directory.EnumerateFiles(cliTuiPath, "*.cs", SearchOption.AllDirectories).Any())
            violations.Add("CLI Tui fallback directory exists");

        XDocument cli = XDocument.Load(ContextSystemHelpers.RepoPath(
            "src", "Event.SetupAssistant.Cli", "Event.SetupAssistant.Cli.csproj"));
        string[] cliPackages = cli.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        if (cliPackages.Any(package => package.Contains("Terminal", StringComparison.OrdinalIgnoreCase)))
            violations.Add("machine CLI references a terminal package");
        string cliAssemblyPath = ContextSystemHelpers.RepoPath(
            "src", "Event.SetupAssistant.Cli", "bin", "Release", "net10.0",
            "Event.SetupAssistant.Cli.dll");
        using (FileStream stream = File.OpenRead(cliAssemblyPath))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader metadata = peReader.GetMetadataReader();
            string[] forbiddenConsoleMembers =
            [
                "Read", "ReadKey", "ReadLine", "get_KeyAvailable",
                "SetCursorPosition", "set_CursorVisible",
                "set_BufferHeight", "set_BufferWidth", "set_WindowHeight",
                "set_WindowWidth"
            ];
            foreach (MemberReferenceHandle handle in metadata.MemberReferences)
            {
                MemberReference member = metadata.GetMemberReference(handle);
                if (member.Parent.Kind != HandleKind.TypeReference)
                    continue;
                TypeReference owner = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                if (metadata.GetString(owner.Namespace) != "System"
                    || metadata.GetString(owner.Name) != "Console")
                    continue;
                string name = metadata.GetString(member.Name);
                if (forbiddenConsoleMembers.Contains(name, StringComparer.Ordinal))
                    violations.Add($"machine CLI references interactive Console.{name}");
            }
        }

        XDocument terminal = XDocument.Load(ContextSystemHelpers.RepoPath(
            "src", "Event.SetupAssistant.Terminal", "Event.SetupAssistant.Terminal.csproj"));
        string[] terminalPackages = terminal.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        if (terminalPackages.Count(package => string.Equals(
                package, "ISLAMU.Terminal.Gui", StringComparison.Ordinal)) != 1)
            violations.Add("Terminal target must reference exactly one audited ISLAMU.Terminal.Gui package");
        if (terminalPackages.Any(package => string.Equals(
                package, "Terminal.Gui", StringComparison.OrdinalIgnoreCase)))
            violations.Add("Terminal target references the official package identity");

        string[] presentationProjects = Directory.GetFiles(
            ContextSystemHelpers.RepoPath("src"),
            "Event.SetupAssistant*.csproj",
            SearchOption.AllDirectories).Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)).ToArray();
        string[] enabledTargets = presentationProjects.Where(path =>
        {
            XDocument project = XDocument.Load(path);
            return string.Equals(project.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "SetupTargetEnabled")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        if (enabledTargets.Length != 1
            || !string.Equals(
                Path.GetFileNameWithoutExtension(enabledTargets.SingleOrDefault()),
                "Event.SetupAssistant.Terminal",
                StringComparison.Ordinal))
            violations.Add("exactly one human Setup target must be enabled: Event.SetupAssistant.Terminal");
        string? role = terminal.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "SetupTargetRole")?.Value;
        if (!string.Equals(role, "Terminal", StringComparison.Ordinal))
            violations.Add("Terminal target role must be Terminal");

        XDocument solution = XDocument.Load(ContextSystemHelpers.RepoPath("Explore.slnx"));
        string[] unexpected = solution.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => NormalizePath(element.Attribute("Path")?.Value ?? string.Empty))
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(
                    "Event.SetupAssistant",
                    StringComparison.Ordinal)
                && !RequiredSolutionProjects.Values.Contains(path, StringComparer.Ordinal))
            .ToArray();
        violations.AddRange(unexpected.Select(path => $"unapproved Setup presentation project: {path}"));

        await Assert.That(violations).IsEmpty().Because(string.Join("; ", violations));
    }

    private static readonly string[] BindingProperties =
    [
        "ApplyMode", "ArtifactDigest", "ExpiresAt", "MappingDigest",
        "RequiredApprovalDigest", "SelectedSectionsDigest", "Target",
        "TargetRevisionDigest"
    ];

    private static string[] ValidateProjectMetadata(
        IReadOnlyDictionary<string, XDocument> projects)
    {
        var violations = new List<string>();
        foreach ((string projectName, string[] expectedReferences) in AllowedProjectReferences)
        {
            if (!projects.TryGetValue(projectName, out XDocument? project))
            {
                violations.Add($"missing project metadata: {projectName}");
                continue;
            }

            string[] actualReferences = project.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(
                    NormalizePath(element.Attribute("Include")?.Value ?? string.Empty)))
                .ToArray();
            violations.AddRange(actualReferences.Except(expectedReferences, StringComparer.Ordinal)
                .Select(reference => $"forbidden ProjectReference {projectName} -> {reference}"));
            violations.AddRange(expectedReferences.Except(actualReferences, StringComparer.Ordinal)
                .Select(reference => $"missing ProjectReference {projectName} -> {reference}"));

            string[] packages = project.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
                .ToArray();
            violations.AddRange(packages
                .Where(package => BlockedPackageTerms.Any(term =>
                    package.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .Select(package => $"blocked PackageReference {projectName}: {package}"));
            violations.AddRange(packages
                .Where(package => string.Equals(
                    package, "Terminal.Gui", StringComparison.OrdinalIgnoreCase))
                .Select(package => $"official PackageReference {projectName}: {package}"));
            if (projectName.EndsWith(".Tests", StringComparison.Ordinal))
            {
                violations.AddRange(packages
                    .Where(package => !ApprovedTestPackages.Contains(package, StringComparer.Ordinal))
                    .Select(package =>
                        $"unapproved test PackageReference {projectName}: {package}"));
            }
            else
            {
                violations.AddRange(packages
                    .Where(package => ForbiddenSourcePackageTerms.Any(term =>
                        package.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    .Select(package =>
                        $"forbidden source PackageReference {projectName}: {package}"));
            }
        }
        return [.. violations];
    }

    private static string[] ValidateSetupLiveProjectSplit(
        IReadOnlyDictionary<string, XDocument> projects)
    {
        var violations = new List<string>();
        XDocument presentation = projects["Event.SetupAssistant"];
        bool excludesLiveSources = presentation.Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Any(element => string.Equals(
                NormalizePath(element.Attribute("Remove")?.Value ?? string.Empty),
                "SetupLive/**/*.cs",
                StringComparison.Ordinal));
        if (!excludesLiveSources)
            violations.Add("Event.SetupAssistant must exclude SetupLive/**/*.cs");

        XDocument live = projects["Event.SetupAssistant.SetupLive"];
        if (live.Descendants().Any(element =>
            element.Name.LocalName == "PackageReference"))
        {
            violations.Add("Event.SetupAssistant.SetupLive must not declare packages");
        }

        return [.. violations];
    }

    private static string[] ValidatePresentationProject(
        XDocument project,
        XDocument central)
    {
        var violations = new List<string>();
        string[] projectReferences = project.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                NormalizePath(element.Attribute("Include")?.Value ?? string.Empty)))
            .ToArray();
        if (!projectReferences.SequenceEqual(["Event.Setup.Core"], StringComparer.Ordinal))
            violations.Add("ProjectReferences must be exactly Event.Setup.Core");

        string[] packages = project.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        if (!packages.SequenceEqual(["CommunityToolkit.Mvvm"], StringComparer.Ordinal))
            violations.Add("PackageReferences must be exactly CommunityToolkit.Mvvm");

        XElement[] pins = central.Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion"
                && string.Equals(
                    element.Attribute("Include")?.Value,
                    "CommunityToolkit.Mvvm",
                    StringComparison.Ordinal))
            .ToArray();
        if (pins.Length != 1
            || !string.Equals(pins[0].Attribute("Version")?.Value, "8.4.2", StringComparison.Ordinal))
            violations.Add("central CommunityToolkit.Mvvm pin must be exactly 8.4.2");
        return [.. violations];
    }

    private static string[] ValidatePresentationLock(JsonElement root)
    {
        var violations = new List<string>();
        if (!TryGetNested(root, out JsonElement framework, "dependencies", "net10.0")
            || framework.ValueKind != JsonValueKind.Object)
            return ["net10.0 lock graph is missing"];
        string[] nodes = framework.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();
        string[] expected =
            ["CommunityToolkit.Mvvm", "YamlDotNet", "event.setup.core", "event.wire.contracts"];
        if (!nodes.SequenceEqual(expected, StringComparer.Ordinal))
            violations.Add("lock nodes must be exactly Core, Wire, YamlDotNet, and CommunityToolkit.Mvvm");
        if (!framework.TryGetProperty("CommunityToolkit.Mvvm", out JsonElement toolkit))
            violations.Add("CommunityToolkit.Mvvm lock node is missing");
        else
        {
            if (!TryGetString(toolkit, "type", out string? type) || type != "Direct")
                violations.Add("CommunityToolkit.Mvvm lock node must be Direct");
            if (!TryGetString(toolkit, "resolved", out string? resolved) || resolved != "8.4.2")
                violations.Add("CommunityToolkit.Mvvm resolved version must be 8.4.2");
            if (!TryGetString(toolkit, "requested", out string? requested)
                || requested != "[8.4.2, )")
                violations.Add("CommunityToolkit.Mvvm requested range must be [8.4.2, )");
            if (!TryGetString(toolkit, "contentHash", out string? contentHash)
                || contentHash != CommunityToolkitContentHash)
                violations.Add("CommunityToolkit.Mvvm contentHash must match approved graph");
            if (toolkit.TryGetProperty("dependencies", out JsonElement dependencies)
                && dependencies.ValueKind == JsonValueKind.Object
                && dependencies.EnumerateObject().Any())
                violations.Add("CommunityToolkit.Mvvm must have no transitive package dependency");
        }
        if (!framework.TryGetProperty("YamlDotNet", out JsonElement yaml))
            violations.Add("YamlDotNet lock node is missing");
        else if (!TryGetString(yaml, "type", out string? yamlType)
            || yamlType != "CentralTransitive"
            || !TryGetString(yaml, "resolved", out string? yamlVersion)
            || yamlVersion != "18.1.0"
            || !TryGetString(yaml, "contentHash", out string? yamlHash)
            || yamlHash != "5K+9KFg2TdTl7VXv88Qzi/0lqK6JFoNP3lRuImPYGRV7K/QYklDyTrj4+A+KAki1JsQi6qKY+hDyY7d6WRqjrw==")
        {
            violations.Add("YamlDotNet must be the approved central transitive 18.1.0 graph");
        }
        return [.. violations];
    }

    private static string[] ValidatePresentationAssembly(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
            return ["compiled Event.SetupAssistant assembly is missing"];
        using FileStream stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        string[] references = metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .ToArray();
        var violations = new List<string>();
        string[] required = ["CommunityToolkit.Mvvm"];
        violations.AddRange(required.Where(requiredName =>
            !references.Contains(requiredName, StringComparer.Ordinal))
            .Select(requiredName => $"compiled reference is missing: {requiredName}"));
        violations.AddRange(references.Where(reference =>
            reference is not "Event.Setup.Core" and not "CommunityToolkit.Mvvm"
            && reference is not "System" and not "netstandard" and not "mscorlib"
            && !reference.StartsWith("System.", StringComparison.Ordinal))
            .Select(reference => $"forbidden compiled assembly reference: {reference}"));

        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            TypeReference reference = metadata.GetTypeReference(handle);
            string identity = $"{metadata.GetString(reference.Namespace)}.{metadata.GetString(reference.Name)}";
            if (ForbiddenPresentationClosureTerms.Any(term =>
                identity.Contains(term, StringComparison.OrdinalIgnoreCase)))
                violations.Add($"forbidden compiled type closure: {identity}");
        }
        foreach (MemberReferenceHandle handle in metadata.MemberReferences)
        {
            MemberReference member = metadata.GetMemberReference(handle);
            if (metadata.GetString(member.Name) != "Default"
                || member.Parent.Kind != HandleKind.TypeReference)
                continue;
            TypeReference owner = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            string ownerName = metadata.GetString(owner.Name);
            if (ownerName is "WeakReferenceMessenger" or "Ioc")
                violations.Add($"forbidden service location singleton: {ownerName}.Default");
        }
        return [.. violations];
    }

    private static IEnumerable<string> FindJsonPropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (string nested in FindJsonPropertyNames(property.Value))
                    yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                foreach (string nested in FindJsonPropertyNames(item))
                    yield return nested;
        }
    }

    private static string[] ValidateBrowserCapability(JsonElement root) =>
        ValidateDisabledCapability(root, "browser", ["secretEntry"]);

    private static string[] ValidateSetupLiveCapability(JsonElement root) =>
        ValidateDisabledCapability(
            root,
            "setup-live",
            ["targetEnrollment", "secretBindingReadiness", "secretBindingWrite", "savedProfiles"]);

    private static string[] ValidateDisabledCapability(
        JsonElement root,
        string expectedTarget,
        string[] expectedCapabilities)
    {
        var violations = new List<string>();
        string[] actualRootProperties = root.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedRootProperties =
            ["_metadata", "schemaVersion", "target", "targetEnabled", "capabilities"];
        if (!actualRootProperties.SequenceEqual(
                expectedRootProperties.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            violations.Add("root properties must match the exact generated set");
        if (!root.TryGetProperty("_metadata", out JsonElement metadata)
            || metadata.ValueKind != JsonValueKind.Object)
        {
            violations.Add("_metadata must be an object");
        }
        else
        {
            string[] actualMetadataProperties = metadata.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!actualMetadataProperties.SequenceEqual(
                    new[] { "about", "generatedBy" }, StringComparer.Ordinal))
                violations.Add("_metadata properties must match the exact generated set");
            if (!TryGetString(metadata, "generatedBy", out string? generatedBy)
                || generatedBy != "eng/setup-assistant/GenerateSetupAssistantRatchets.cs")
                violations.Add("_metadata.generatedBy must name the authoritative generator");
            if (!metadata.TryGetProperty("about", out JsonElement about)
                || about.ValueKind != JsonValueKind.Array
                || !about.EnumerateArray().Select(item => item.GetString()).SequenceEqual(
                    new[]
                    {
                        "ABOUTME: Generated Setup Assistant architecture ratchet; do not edit by hand.",
                        "ABOUTME: Owned by eng/setup-assistant/GenerateSetupAssistantRatchets.cs."
                    }, StringComparer.Ordinal))
                violations.Add("_metadata.about must match the exact generated ownership summary");
        }
        if (!TryGetInt32(root, "schemaVersion", out int version) || version != 1)
            violations.Add("schemaVersion must be 1");
        if (!TryGetString(root, "target", out string? target)
            || !string.Equals(target, expectedTarget, StringComparison.Ordinal))
        {
            violations.Add($"target must be {expectedTarget}");
        }
        if (!root.TryGetProperty("targetEnabled", out JsonElement targetEnabled)
            || targetEnabled.ValueKind != JsonValueKind.False)
            violations.Add("targetEnabled must be false");
        if (!root.TryGetProperty("capabilities", out JsonElement capabilities)
            || capabilities.ValueKind != JsonValueKind.Object)
        {
            violations.Add("capabilities must be an object");
            return [.. violations];
        }
        string[] actualCapabilities = capabilities.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualCapabilities.SequenceEqual(
                expectedCapabilities.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            violations.Add("capabilities must match the exact generated set");
        foreach (string capability in expectedCapabilities)
        {
            if (!capabilities.TryGetProperty(capability, out JsonElement value)
                || value.ValueKind != JsonValueKind.False)
                violations.Add($"capabilities.{capability} must be false");
        }
        return [.. violations];
    }

    private static string[] ValidateFrozenContractBaseline(JsonElement root)
    {
        var violations = new List<string>();
        if (!TryGetInt32(root, "schemaVersion", out int version) || version != 1)
            violations.Add("schemaVersion must be 1");
        ValidateBaselineSchema(root, "configurationManifest", ManifestSchemaId,
            "ConfigurationManifest", violations);
        ValidateBaselineSchema(root, "tenantConfigurationPackage", TenantPackageSchemaId,
            "TenantConfigurationPackage", violations);

        if (!TryGetNested(root, out JsonElement registry, "portabilityRegistry"))
        {
            violations.Add("portabilityRegistry is missing");
        }
        else
        {
            if (!TryGetInt32(registry, "cardinality", out int cardinality) || cardinality != 21)
                violations.Add("portabilityRegistry.cardinality must be 21");
            ValidateExactArray(registry, "keys", RegistryKeys, violations);
            ValidateExactArray(registry, "excludedAuthorityKeys", ExcludedRegistryKeys, violations);
        }

        ValidateContractFacts(root, "importSession", "targetProperties",
            ["AuthorityKey", "Scope", "TenantId"], violations);
        ValidateContractFacts(root, "importPreview", "bindingProperties",
            BindingProperties, violations);
        return [.. violations];
    }

    private static void ValidateBaselineSchema(
        JsonElement root,
        string name,
        string schemaId,
        string kind,
        ICollection<string> violations)
    {
        if (!TryGetNested(root, out JsonElement schema, "schemas", name))
        {
            violations.Add($"schemas.{name} is missing");
            return;
        }
        if (!TryGetString(schema, "schemaId", out string? actualSchemaId)
            || !string.Equals(actualSchemaId, schemaId, StringComparison.Ordinal))
            violations.Add($"schemas.{name}.schemaId is incorrect");
        if (!TryGetString(schema, "apiVersion", out string? actualApiVersion)
            || !string.Equals(actualApiVersion, ApiVersion, StringComparison.Ordinal))
            violations.Add($"schemas.{name}.apiVersion is incorrect");
        if (!TryGetString(schema, "kind", out string? actualKind)
            || !string.Equals(actualKind, kind, StringComparison.Ordinal))
            violations.Add($"schemas.{name}.kind is incorrect");
        if (!schema.TryGetProperty("closedObjects", out JsonElement closed)
            || closed.ValueKind != JsonValueKind.True)
            violations.Add($"schemas.{name}.closedObjects must be true");
    }

    private static void ValidateContractFacts(
        JsonElement root,
        string contractName,
        string propertySetName,
        string[] expectedProperties,
        ICollection<string> violations)
    {
        if (!TryGetNested(root, out JsonElement contract, contractName))
        {
            violations.Add($"{contractName} is missing");
            return;
        }
        if (!contract.TryGetProperty("targetBound", out JsonElement targetBound)
            || targetBound.ValueKind != JsonValueKind.True)
            violations.Add($"{contractName}.targetBound must be true");
        if (!contract.TryGetProperty("valueFree", out JsonElement valueFree)
            || valueFree.ValueKind != JsonValueKind.True)
            violations.Add($"{contractName}.valueFree must be true");
        ValidateExactArray(contract, propertySetName, expectedProperties, violations);
    }

    private static void ValidateExactArray(
        JsonElement owner,
        string propertyName,
        string[] expected,
        ICollection<string> violations)
    {
        if (!owner.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            violations.Add($"{propertyName} must be an array");
            return;
        }
        string[] actual = array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
        if (actual.Length != array.GetArrayLength()
            || !actual.Order(StringComparer.Ordinal).SequenceEqual(
                expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            violations.Add($"{propertyName} does not match the frozen set");
    }

    private static void ValidateSchema(string repositoryPath, string schemaId, string kind)
    {
        string path = ContextSystemHelpers.RepoPath(
            repositoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (!File.Exists(path))
            throw new InvalidOperationException($"Frozen schema is missing: {repositoryPath}");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = document.RootElement;
        if (!string.Equals(root.GetProperty("$schema").GetString(),
                "https://json-schema.org/draft/2020-12/schema", StringComparison.Ordinal)
            || !string.Equals(root.GetProperty("$id").GetString(), schemaId,
                StringComparison.Ordinal)
            || root.GetProperty("type").GetString() != "object"
            || root.GetProperty("additionalProperties").ValueKind != JsonValueKind.False
            || !string.Equals(root.GetProperty("properties").GetProperty("$schema")
                .GetProperty("const").GetString(), schemaId, StringComparison.Ordinal)
            || !string.Equals(root.GetProperty("properties").GetProperty("apiVersion")
                .GetProperty("const").GetString(), ApiVersion, StringComparison.Ordinal)
            || !string.Equals(root.GetProperty("properties").GetProperty("kind")
                .GetProperty("const").GetString(), kind, StringComparison.Ordinal)
            || !AllTypedObjectsAreClosed(root))
        {
            throw new InvalidOperationException(
                $"Frozen schema identity or closed-object behavior drifted: {repositoryPath}");
        }
    }

    private static bool AllTypedObjectsAreClosed(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "object", StringComparison.Ordinal)
                && (!element.TryGetProperty("additionalProperties", out JsonElement additional)
                    || additional.ValueKind != JsonValueKind.False))
                return false;
            return element.EnumerateObject()
                .All(property => AllTypedObjectsAreClosed(property.Value));
        }
        return element.ValueKind != JsonValueKind.Array
            || element.EnumerateArray().All(AllTypedObjectsAreClosed);
    }

    private static IReadOnlyDictionary<string, XDocument> CreateCompliantFixture() =>
        AllowedProjectReferences.ToDictionary(
            pair => pair.Key,
            pair => XDocument.Parse(
                $"""
                <Project><ItemGroup>
                  {string.Join(Environment.NewLine, pair.Value.Select(reference => $"<ProjectReference Include=\"../{reference}/{reference}.csproj\" />"))}
                  {(pair.Key.EndsWith(".Tests", StringComparison.Ordinal) ? "<PackageReference Include=\"TUnit\" />" : string.Empty)}
                </ItemGroup></Project>
                """),
            StringComparer.Ordinal);

    private static string CreateBaselineFixture(int cardinality, bool valueFree) =>
        $$"""
        {
          "schemaVersion": 1,
          "schemas": {
            "configurationManifest": {"schemaId":"{{ManifestSchemaId}}","apiVersion":"{{ApiVersion}}","kind":"ConfigurationManifest","closedObjects":true},
            "tenantConfigurationPackage": {"schemaId":"{{TenantPackageSchemaId}}","apiVersion":"{{ApiVersion}}","kind":"TenantConfigurationPackage","closedObjects":true}
          },
          "portabilityRegistry": {
            "cardinality": {{cardinality}},
            "keys": [{{string.Join(",", RegistryKeys.Select(value => JsonSerializer.Serialize(value)))}}],
            "excludedAuthorityKeys": [{{string.Join(",", ExcludedRegistryKeys.Select(value => JsonSerializer.Serialize(value)))}}]
          },
          "importSession": {"targetBound":true,"valueFree":{{valueFree.ToString().ToLowerInvariant()}},"targetProperties":["AuthorityKey","Scope","TenantId"]},
          "importPreview": {"targetBound":true,"valueFree":true,"bindingProperties":[{{string.Join(",", BindingProperties.Select(value => JsonSerializer.Serialize(value)))}}]}
        }
        """;

    private static string[] ValidateCiGovernance(string caller, string reusable)
    {
        var violations = new List<string>();
        string[] setupRouteInputs =
        [
            "NuGet.Config", "src/Event.Setup.Core/*", "src/Event.SetupAssistant/*",
            "src/Event.SetupAssistant.Browser/*", "src/Event.SetupAssistant.Desktop/*",
            "src/Event.SetupAssistant.Terminal/*", "src/Event.SetupAssistant.Cli/*",
            "tests/Event.Setup*.Tests/*", "eng/setup-assistant/*",
            "eng/release/dependencies/terminal-gui/*", "browser-release-capabilities.json",
            "setup-live-release-capabilities.json",
            "frozen-contract-baseline.json"
        ];
        foreach (string routeInput in setupRouteInputs)
        {
            if (!caller.Contains(routeInput, StringComparison.Ordinal))
                violations.Add($"missing Setup route input: {routeInput}");
        }

        string[] requiredCallerValues =
        [
            "run-setup-tests: ${{ steps.changes.outputs.run-setup-tests }}",
            "run-setup-tests: ${{ needs.detect-build-test-changes.outputs.run-setup-tests == 'true' }}",
            "run_setup=\"true\"", "run_arch=\"true\"",
            "run-integration-tests: false"
        ];
        foreach (string required in requiredCallerValues)
        {
            if (!caller.Contains(required, StringComparison.Ordinal))
                violations.Add($"missing caller control: {required}");
        }

        string[] setupProjects = RequiredSolutionProjects
            .Where(pair => pair.Key.EndsWith(".Tests", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToArray();
        foreach (string project in setupProjects)
        {
            if (!reusable.Contains(project, StringComparison.Ordinal))
                violations.Add($"missing Setup project gate: {project}");
        }
        if (!reusable.Contains("dotnet test --project \"$project\"", StringComparison.Ordinal)
            || !reusable.Contains("--minimum-expected-tests 1", StringComparison.Ordinal))
            violations.Add("Setup project gates must execute every focused test project and reject empty runs");

        string[] setupLiveRouteInputs =
        [
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
        foreach (string routeInput in setupLiveRouteInputs)
        {
            if (!caller.Contains(routeInput, StringComparison.Ordinal))
                violations.Add($"missing SetupLive route input: {routeInput}");
        }

        if (!reusable.Contains("run-setup-tests:", StringComparison.Ordinal)
            || !reusable.Contains("inputs.run-setup-tests", StringComparison.Ordinal))
            violations.Add("reusable Setup lane input is missing");
        if (!reusable.Contains(
                "/*/*/*SetupLiveAuthoritySecurityTests/*",
                StringComparison.Ordinal)
            || !reusable.Contains(
                "--minimum-expected-tests 35 --maximum-parallel-tests 1",
                StringComparison.Ordinal)
            || !reusable.Contains(
                "--report-trx-filename SetupLiveAuthoritySecurityTests.trx",
                StringComparison.Ordinal))
            violations.Add("SetupLive Tier 1 API gate is missing or not retained");
        if (!reusable.Contains(
                "dotnet run eng/setup-assistant/GenerateSetupAssistantRatchets.cs -- --check",
                StringComparison.Ordinal)
            || !reusable.Contains(
                "eng/setup-assistant/SetupCliCommandSchemaGenerator/SetupCliCommandSchemaGenerator.csproj",
                StringComparison.Ordinal)
            || !reusable.Contains("SetupCliCommandSchemaGenerator.csproj --configuration \"$DOTNET_CONFIGURATION\" --no-restore -- --check",
                StringComparison.Ordinal)
            || reusable.Contains("GenerateSetupAssistantRatchets.cs -- --write",
                StringComparison.Ordinal)
            || reusable.Contains("SetupCliCommandSchemaGenerator.csproj --configuration \"$DOTNET_CONFIGURATION\" --no-restore -- --write",
                StringComparison.Ordinal))
            violations.Add("ratchet or command-schema check is missing or mutating");
        if (!reusable.Contains(
                "bash eng/release/dependencies/terminal-gui/BuildTerminalGuiPackage.sh --check",
                StringComparison.Ordinal))
            violations.Add("audited Terminal.Gui source rebuild check is missing");

        ValidateWorkflowAuthority(caller, "caller", violations);
        ValidateWorkflowAuthority(reusable, "reusable", violations);
        return [.. violations];
    }

    private static void ValidateWorkflowAuthority(
        string workflow,
        string name,
        List<string> violations)
    {
        if (!workflow.Contains("permissions:\n  contents: read", StringComparison.Ordinal))
            violations.Add($"{name} workflow lacks top-level contents: read");
        string[] forbidden =
        [
            "contents: write", "id-token: write", "packages: write",
            "attestations: write", "secrets: inherit", "${{ secrets.",
            "persist-credentials: true"
        ];
        foreach (string value in forbidden)
        {
            if (workflow.Contains(value, StringComparison.Ordinal))
                violations.Add($"{name} workflow contains forbidden authority: {value}");
        }

        int checkoutCount = CountOccurrences(workflow, "uses: actions/checkout@");
        int safeCheckoutCount = CountOccurrences(workflow, "persist-credentials: false");
        if (checkoutCount != safeCheckoutCount)
            violations.Add($"{name} workflow has a checkout without disabled credential persistence");
    }

    private static int CountOccurrences(string value, string token)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }
        return count;
    }

    private static bool IsIgnoredByGit(string repositoryPath)
    {
        ProcessResult result = RunProcess(
            "git", ["check-ignore", "--no-index", "--quiet", repositoryPath],
            ContextSystemHelpers.RepoPath());
        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new InvalidOperationException(
                $"git check-ignore failed for {repositoryPath}: {result.Output}")
        };
    }

    private static ProcessResult RunProcess(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(milliseconds: 30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not finish within 30 seconds.");
        }

        return new ProcessResult(
            process.ExitCode,
            string.Concat(standardOutput, Environment.NewLine, standardError));
    }

    private readonly record struct ProcessResult(int ExitCode, string Output);

    private static bool TryGetNested(
        JsonElement root,
        out JsonElement value,
        params string[] path)
    {
        value = root;
        foreach (string segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty(segment, out value))
                return false;
        }
        return true;
    }

    private static bool TryGetInt32(
        JsonElement owner,
        string propertyName,
        out int value)
    {
        value = 0;
        return owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetInt32(out value);
    }

    private static bool TryGetString(
        JsonElement owner,
        string propertyName,
        out string? value)
    {
        value = null;
        return owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && (value = property.GetString()) is not null;
    }

    private static string[] PublicPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsValueBearingPropertyName(string name) =>
        new[]
        {
            "AccessToken", "ArtifactBytes", "ConnectionString", "Content",
            "FilePath", "Payload", "SecretValue", "SourceTenantId"
        }.Any(forbidden => string.Equals(
            name, forbidden, StringComparison.OrdinalIgnoreCase));

    private static string[] FindForbiddenSetupLiveOuterTypes(
        IEnumerable<string> identities)
    {
        string[] forbiddenTypeTerms =
        [
            "System.Console", "System.IO.File", "System.IO.Directory",
            "System.IO.StreamWriter", "System.Environment",
            "System.Data", "DbConnection", "DbCommand", "DbProviderFactory",
            "System.Diagnostics.Process", "System.Diagnostics.Activity",
            "System.Diagnostics.Metrics", "Microsoft.Extensions.Logging",
            "ProtectedData", "CredentialStore", "Keychain", "Keyring"
        ];
        return identities.Where(identity => forbiddenTypeTerms.Any(term =>
                identity.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
