using System.Xml.Linq;
using Explore.Infrastructure.Services.Federation;

namespace Event.Architecture.Tests;

public sealed class AtprotoOAuthTransportArchitectureTests
{
    [Test]
    public async Task BffAndInfrastructureMustReferenceOnePackageFreeTransportProject()
    {
        var projectPaths = new[]
        {
            ContextSystemHelpers.RepoPath("Explore.Blazor", "Explore.Blazor.csproj"),
            ContextSystemHelpers.RepoPath("Explore.Infrastructure", "Explore.Infrastructure.csproj")
        };
        foreach (var projectPath in projectPaths)
        {
            var references = XDocument.Load(projectPath).Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? string.Empty);
            await Assert.That(references.Any(reference =>
                    reference.Contains("Explore.Atproto.Transport", StringComparison.Ordinal)))
                .IsTrue();
        }

        var transportProject = XDocument.Load(Path.Combine(
            ContextSystemHelpers.RepoRoot,
            "src",
            "Explore.Atproto.Transport",
            "Explore.Atproto.Transport.csproj"));
        await Assert.That(transportProject.Descendants()
                .Any(element => element.Name.LocalName == "PackageReference"))
            .IsFalse();
    }

    [Test]
    public async Task CorePdsClientMustUseInjectedHardenedTransportAndNeverImplicitOauthClient()
    {
        var source = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            "Explore.Infrastructure",
            "Services",
            "Federation",
            "AtprotoCoreClientFactory.cs"));

        await Assert.That(source).Contains("new ATProtoClient(new ATProtoClientOptions");
        await Assert.That(source).Contains("HttpClient = coreHttpClient");
        await Assert.That(source).Contains("CreateIdentityResolver = false");
        await Assert.That(source).Contains("AutoRetryOnAuthFailure = false");
        await Assert.That(source).DoesNotContain("ATProtoOAuthClient");
        await Assert.That(source).DoesNotContain("new OAuthSession");
    }

    [Test]
    public async Task ConfidentialFactoriesMustKeepCarpaLoggingDisabledAndStoresOutOfSingletonConstructor()
    {
        var sources = new[]
        {
            ContextSystemHelpers.RepoPath("Explore.Blazor", "Services", "Auth", "AtprotoOAuthClientFactory.cs"),
            ContextSystemHelpers.RepoPath("Explore.Infrastructure", "Services", "Federation", "AtprotoOAuthClientFactory.cs")
        };
        foreach (var path in sources)
        {
            var source = await File.ReadAllTextAsync(path);
            await Assert.That(source).DoesNotContain("LoggerFactory =");
        }

        var bffFactory = await File.ReadAllTextAsync(sources[0]);
        await Assert.That(bffFactory).DoesNotContain("IOAuthStateStore? stateStore");
        await Assert.That(bffFactory).DoesNotContain("IOAuthSessionStore? sessionStore");
        await Assert.That(bffFactory).Contains("IServiceProviderIsService serviceAvailability");
        await Assert.That(typeof(AtprotoCoreClientFactory)).IsNotNull();
    }
}
