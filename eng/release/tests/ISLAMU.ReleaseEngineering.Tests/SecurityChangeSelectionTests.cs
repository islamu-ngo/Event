namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityChangeSelectionTests
{
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
