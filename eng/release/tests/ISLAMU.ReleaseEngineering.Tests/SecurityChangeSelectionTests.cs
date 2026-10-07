using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityChangeSelectionTests
{
    [Test]
    [Arguments("src/Explore.API/Hosting/ApiHostApplicationExtensions.cs")]
    [Arguments("src/Explore.API/Authentication/DynamicJwtBearerPostConfigureOptions.cs")]
    [Arguments("src/Event.Standalone/Hosting/StandaloneHostApplicationExtensions.cs")]
    [Arguments("src/Event.Web.BffHosting/Security/BffProxyHeaderSanitizer.cs")]
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
    [Arguments("src/Explore.API/HostingNotes.md")]
    public async Task UnrelatedChangeRemainsAnHonestNoOp(string path)
    {
        await Assert.That(SecurityChangeSelection.RequiresExecution([path])).IsFalse();
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
}
