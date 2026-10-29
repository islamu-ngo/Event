namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityChangeSelectionTests
{
    [Test]
    [Arguments("README.md\ndocs/public/guide.md\n", "")]
    [Arguments("schemas/configuration-manifest-v1alpha2.schema.json", "architecture")]
    [Arguments("schemas/tenant-configuration-package-v1alpha2.schema.json", "architecture")]
    [Arguments("eng/configuration-manifest-schema/Program.cs", "architecture")]
    [Arguments("src/Explore.Domain/Entities/Event.cs", "app,domain,architecture,setup,infrastructure")]
    [Arguments("tests/Event.Domain.UnitTests/Entities/EventTests.cs", "app,domain,architecture,setup,infrastructure")]
    [Arguments("src/Explore.Application/Features/EventQuery.cs", "app,architecture,setup,infrastructure")]
    [Arguments("tests/Event.API.IntegrationTests/Features/EventTests.cs", "app,architecture,setup,infrastructure")]
    [Arguments("src/Explore.Persistence/ExploreDbContext.cs", "architecture,setup,infrastructure")]
    [Arguments("src/Explore.Infrastructure/Services/EventService.cs", "architecture,setup,infrastructure")]
    [Arguments("src/Explore.ServiceDefaults/Secrets/SecretAuthorityAcquisition.cs", "all")]
    [Arguments("src/Explore.ServiceDefaults/DataProtection/SecretDataProtectionMaterial.cs", "all")]
    [Arguments("src/Explore.Infrastructure/Secrets/InfisicalSecretProvider.cs", "all")]
    [Arguments("src/Explore.Persistence/Database/DatabaseSecretAcquisition.cs", "all")]
    [Arguments("src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs", "all")]
    [Arguments("src/Explore.API/Configuration/Settings.cs", "all")]
    [Arguments("src/Explore.API/Program.cs", "all")]
    [Arguments("src/Explore.Blazor/Hosting/BlazorHostServiceCollectionExtensions.cs", "all")]
    [Arguments("src/Explore.Blazor/Extensions/ConfigurationExtension.cs", "all")]
    [Arguments("src/Explore.Blazor/Program.cs", "all")]
    [Arguments("eng/security/authority.json", "all")]
    [Arguments("eng/tools/EmergencyAdminProvisioner.cs", "all")]
    [Arguments("src/Explore.Blazor.Client/Pages/Event.razor", "architecture,setup,blazor")]
    [Arguments("src/Event.Web.BffHosting/Hosting.cs", "all")]
    [Arguments("src/Event.Wire.Contracts/SetupLive/SetupRequest.cs", "architecture,setup")]
    [Arguments("tests/Event.Wire.Contracts.UnitTests/Fixtures/Configurator/settings.json", "architecture,setup")]
    [Arguments("tests/Explore.GeneratedContracts.Tests/ContractTests.cs", "architecture")]
    [Arguments("src/Explore.Diagnostic/Program.cs", "architecture")]
    [Arguments("src/Event.Standalone/Program.cs", "all")]
    [Arguments("tests/Event.Standalone.IntegrationTests/StartupTests.cs", "architecture,setup,infrastructure,blazor")]
    [Arguments("tests/Explore.Blazor.IntegrationTests/Configuration/SecretKitBffPreparationTests.cs", "architecture,setup,infrastructure,blazor")]
    [Arguments("tests/Explore.Infrastructure.Tests/Secrets/SecretKitAuthorityAcquisitionTests.cs", "architecture,setup,infrastructure")]
    [Arguments("tests/Event.Architecture.Tests/ConfigurationManifestSchemaArtifactTests.cs", "architecture")]
    [Arguments("src/Explore.AppHost/AppHost.cs", "all")]
    [Arguments("tests/Event.Benchmarks/Program.cs", "all")]
    [Arguments("tests/Event.Distributed.IntegrationTests/OwnedAppHostFixture.cs", "all")]
    [Arguments("tests/Shared/ExternalDataProtectionTestAuthority.cs", "all")]
    [Arguments("tests/Event.Persistence.IntegrationTests/Fixtures/SharedContract.cs\nsrc/Event.Wire.Contracts/SharedContract.cs", "architecture,setup,infrastructure")]
    [Arguments("Directory.Build.targets", "all")]
    [Arguments("src/Explore.Application/Explore.Application.csproj", "all")]
    [Arguments("src/Explore.Application/packages.lock.json", "all")]
    [Arguments("eng/configuration-manifest-schema/ISLAMU.ConfigurationManifestSchema.csproj", "all")]
    [Arguments("tests/Event.Domain.UnitTests/Directory.Build.props", "all")]
    [Arguments("Directory.Packages.props", "all")]
    [Arguments("global.json", "all")]
    [Arguments("NuGet.Config", "all")]
    [Arguments("Explore.slnx", "all")]
    [Arguments(".github/workflows/test.yml", "all")]
    [Arguments(".ci/scripts/check.cs", "all")]
    [Arguments("cerbos/policies/event.yaml", "all")]
    [Arguments("docker/keycloak/realm.json", "all")]
    [Arguments("unknown/component", "all")]
    [Arguments("docs/public/../../src/Contract.md", "all")]
    [Arguments("docs/public//guide.md", "all")]
    [Arguments("docs/public/guide\\example.md", "all")]
    [Arguments("docs/public/guide.md ", "all")]
    [Arguments("README.md\nunknown/component", "all")]
    [Arguments("README.md\n\nschemas/configuration-manifest-v1alpha2.schema.json", "all")]
    [Arguments("", "all")]
    [Arguments("\n", "all")]
    public async Task BuildCommandEmitsExactIndependentLaneDecisions(string paths, string expectedLanes)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"build-selection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "changed-paths.txt");
            await File.WriteAllTextAsync(file, paths);
            await AssertBuildCommand(["select-build-test-changes", file], expectedLanes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [Arguments("missing")]
    [Arguments("directory")]
    [Arguments("invalid-utf8")]
    [Arguments("invalid-path")]
    [Arguments("no-arguments")]
    [Arguments("extra-arguments")]
    public async Task UncertainBuildInputsSelectEveryLane(string variant)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"build-selection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "changed-paths.txt");
            if (variant == "invalid-utf8")
            {
                await File.WriteAllBytesAsync(file, [.. System.Text.Encoding.UTF8.GetBytes("docs/public/"), 0xFF, 0x0A]);
            }

            string[] arguments = variant switch
            {
                "directory" => ["select-build-test-changes", directory],
                "invalid-path" => ["select-build-test-changes", "invalid\0path"],
                "no-arguments" => ["select-build-test-changes"],
                "extra-arguments" => ["select-build-test-changes", file, "extra"],
                _ => ["select-build-test-changes", file]
            };
            await AssertBuildCommand(arguments, "all");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertBuildCommand(string[] arguments, string expectedLanes)
    {
        using var output = new StringWriter();
        int code = Program.Run(arguments, output);
        await Assert.That(code).IsEqualTo(Program.Success);

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines.Length).IsEqualTo(7);
        Dictionary<string, string> decisions = lines.Select(line => line.Split('=')).ToDictionary(parts => parts[0], parts => parts[1]);
        await Assert.That(decisions["run-tests"]).IsEqualTo(expectedLanes.Length > 0 ? "true" : "false");
        foreach (string lane in new[] { "app", "domain", "architecture", "setup", "infrastructure", "blazor" })
        {
            bool expected = expectedLanes == "all" || expectedLanes.Split(',').Contains(lane, StringComparer.Ordinal);
            await Assert.That(decisions[$"run-{lane}-tests"]).IsEqualTo(expected ? "true" : "false");
        }
    }

    [Test]
    [Arguments("src/Explore.API/Hosting/ApiHostApplicationExtensions.cs")]
    [Arguments("src/Explore.API/Authentication/DynamicJwtBearerPostConfigureOptions.cs")]
    [Arguments("src/Event.Standalone/Hosting/StandaloneHostApplicationExtensions.cs")]
    [Arguments("src/Event.Web.BffHosting/Security/BffProxyHeaderSanitizer.cs")]
    [Arguments("eng/security/security-control-register.json")]
    [Arguments("eng/release/src/ISLAMU.ReleaseEngineering/SecurityExecutionEvidence.cs")]
    [Arguments("src/Explore.ServiceDefaults/Diagnostics/EndpointSecurityInventory.cs")]
    [Arguments("tests/Event.API.IntegrationTests/Hosting/ApiEndpointApplicabilityTests.cs")]
    [Arguments("tests/Event.Standalone.IntegrationTests/HostedWorkerActivationTests.cs")]
    [Arguments("tests/Explore.Blazor.IntegrationTests/Hosting/BffEndpointApplicabilityTests.cs")]
    public async Task AuthorityOnlyChangeSelectsRealExecution(string path)
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution([path])).IsTrue();
    }

    [Test]
    [Arguments("cerbos/policies/event.yaml")]
    [Arguments("docker/keycloak/ISLAMU-realm.test.json")]
    [Arguments("src/Explore.Application/Authorization/RequestAuthorization.cs")]
    [Arguments("src/Explore.API/Extensions/AuthenticationExtensions.cs")]
    [Arguments("src/Explore.API/Middleware/ApiTenantResolutionMiddleware.cs")]
    [Arguments("src/Explore.Blazor/Services/DynamicAuthSchemeManager.cs")]
    [Arguments("tests/Event.API.IntegrationTests/Features/SecurityIntegrationTests.cs")]
    [Arguments(".github/workflows/security-tests.yml")]
    public async Task ExistingSecurityPathRemainsSelected(string path)
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution([path])).IsTrue();
    }

    [Test]
    [Arguments("README.md")]
    [Arguments("docs/public/changelog/README.md")]
    [Arguments("docs/public/operations/deployment.md")]
    public async Task KnownPublicDocumentationRemainsAnHonestNoOp(string path)
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution([path])).IsFalse();
    }

    [Test]
    [Arguments("Directory.Packages.props")]
    [Arguments("Directory.Build.props")]
    [Arguments("Directory.Build.targets")]
    [Arguments("global.json")]
    [Arguments("NuGet.Config")]
    [Arguments("Explore.slnx")]
    [Arguments("src/Explore.Application/packages.lock.json")]
    [Arguments("tests/Shared/Telemetry/EventLocationPrivacyMetricsFactory.cs")]
    [Arguments("tests/Shared/DataProtection/ExternalDataProtectionTestAuthority.cs")]
    [Arguments("tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj")]
    public async Task SharedBuildAndLinkedFixtureChangesSelectExecution(string path)
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution([path])).IsTrue();
    }

    [Test]
    [Arguments("src/Explore.AppHost/AppHost.cs")]
    [Arguments("src/Explore.AppHost/Resources/DatabaseResource.cs")]
    [Arguments("src/Explore.AppHost/appsettings.json")]
    [Arguments(".env.example")]
    [Arguments("docker-compose.yml")]
    [Arguments("src/Explore.Persistence/Database/PrimaryDatabaseConnectionOptions.cs")]
    [Arguments("src/Explore.Persistence.PostgreSQL/Migrations/Initial.cs")]
    [Arguments("src/Explore.Infrastructure/Services/NativeProviderAdapter.cs")]
    [Arguments("schemas/openapi_islamu-event.json")]
    [Arguments("src/Explore.Blazor.Client/Generated/EventApiTagClients.g.cs")]
    [Arguments("tests/Event.Distributed.Tests/Browser/OnboardingTests.cs")]
    [Arguments("tests/Event.Distributed.Tests/Load/Fixtures/scenarios.json")]
    [Arguments("eng/load/scenarios/setup.js")]
    [Arguments(".github/workflows/test.yml")]
    public async Task RuntimeProviderContractAndBrowserChangesSelectExecution(string path)
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution([path])).IsTrue();
    }

    [Test]
    [Arguments("future-component/behavior.cs")]
    [Arguments("src/Explore.API/HostingNotes.md")]
    [Arguments("docs/internal/SECURITY-MODEL.md")]
    [Arguments("docs/public/setup.json")]
    [Arguments("docs/public/browser.js")]
    [Arguments("docs/public/../../src/Contract.md")]
    [Arguments("docs/public/./guide.md")]
    [Arguments("docs/public//guide.md")]
    [Arguments("docs/public/guide\n.md")]
    [Arguments("docs/public/guide\\example.md")]
    [Arguments(" docs/public/guide.md")]
    [Arguments("docs/public/guide.md ")]
    [Arguments("fatal: changed-file detection failed")]
    [Arguments("")]
    [Arguments(" \t")]
    public async Task UnknownOrMalformedChangeSelectsExecution(string path)
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution([path])).IsTrue();
    }

    [Test]
    [Arguments("Directory.Packages.props")]
    [Arguments("unknown/path")]
    [Arguments("")]
    public async Task DocumentationCannotHideAnUncertainOrSharedChange(string path)
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution(["README.md", path])).IsTrue();
        await Assert.That(SecurityChangeSelection.RequiresExecution([path, "docs/public/guide.md"])).IsTrue();
    }

    [Test]
    public async Task EmptyDetectionSelectsExecutionFailSafe()
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution([])).IsTrue();
    }

    [Test]
    public async Task MissingChangedPathsFileSelectsExecutionFailSafe()
    {
        using var output = new StringWriter();
        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "changed-paths.txt");

        int code = SecurityChangeSelection.Run(["select-security-changes", missingPath], output);

        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(output.ToString()).IsEqualTo("run-tests=true" + Environment.NewLine);
    }

    [Test]
    [Arguments("README.md\ndocs/public/guide.md\n", false)]
    [Arguments("README.md\nDirectory.Packages.props\n", true)]
    [Arguments("docs/public/guide.md\nunknown/path\n", true)]
    [Arguments("", true)]
    [Arguments("\n", true)]
    public async Task CommandEmitsOneConservativeDecision(string paths, bool expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"security-selection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "changed-paths.txt");
            await File.WriteAllTextAsync(file, paths);
            using var output = new StringWriter();

            int code = SecurityChangeSelection.Run(["select-security-changes", file], output);

            await Assert.That(code).IsEqualTo(Program.Success);
            await Assert.That(output.ToString()).IsEqualTo(expected
                ? "run-tests=true" + Environment.NewLine
                : "run-tests=false" + Environment.NewLine);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task UnreadableChangedPathsSelectsExecutionFailSafe()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"security-selection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var output = new StringWriter();

            int code = SecurityChangeSelection.Run(["select-security-changes", directory], output);

            await Assert.That(code).IsEqualTo(Program.Success);
            await Assert.That(output.ToString()).IsEqualTo("run-tests=true" + Environment.NewLine);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Test]
    public async Task InvalidUtf8CannotMasqueradeAsKnownPublicDocumentation()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"security-selection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "changed-paths.txt");
            await File.WriteAllBytesAsync(file,
                [.. System.Text.Encoding.UTF8.GetBytes("docs/public/"), 0xFF, .. System.Text.Encoding.UTF8.GetBytes(".md")]);
            using var output = new StringWriter();

            int code = SecurityChangeSelection.Run(["select-security-changes", file], output);

            await Assert.That(code).IsEqualTo(Program.Success);
            await Assert.That(output.ToString()).IsEqualTo("run-tests=true" + Environment.NewLine);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(3)]
    public async Task InvalidArgumentsCannotSuppressExecution(int argumentCount)
    {
        using var output = new StringWriter();

        int code = SecurityChangeSelection.Run(Enumerable.Repeat("select-security-changes", argumentCount).ToArray(), output);

        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(output.ToString()).IsEqualTo("run-tests=true" + Environment.NewLine);
    }

    [Test]
    public async Task InvalidChangedPathsFileNameSelectsExecutionFailSafe()
    {
        using var output = new StringWriter();

        int code = SecurityChangeSelection.Run(["select-security-changes", "invalid\0path"], output);

        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(output.ToString()).IsEqualTo("run-tests=true" + Environment.NewLine);
    }
}
