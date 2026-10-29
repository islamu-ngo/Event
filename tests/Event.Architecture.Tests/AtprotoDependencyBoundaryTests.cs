using System.Xml.Linq;
using Mono.Cecil;

namespace Event.Architecture.Tests;

public sealed class AtprotoDependencyBoundaryTests
{
    private const string ExpectedVersion = "1.0.3";

    private static readonly string[] CarpaNetPackages =
    [
        "CarpaNet",
        "CarpaNet.OAuth",
        "CarpaNet.Jetstream"
    ];

    private static readonly string[] ExpectedLexicons =
    [
        "com.atproto.server.getSession.json",
        "com.atproto.repo.strongRef.json",
        "community.lexicon.location.address.json",
        "community.lexicon.location.fsq.json",
        "community.lexicon.location.geo.json",
        "community.lexicon.location.hthree.json",
        "lexicon-community-calendar-events.json",
        "lexicon-community-calendar-rsvp.json"
    ];

    private static readonly string[] ForbiddenProjects =
    [
        "Explore.Domain",
        "Explore.Application",
        "Explore.API",
        "Explore.Blazor.Client"
    ];

    [Test]
    public async Task CarpaNetPackages_MustUseExactStableCentralVersions()
    {
        var document = XDocument.Load(ContextSystemHelpers.RepoPath("Directory.Packages.props"));
        var versions = document.Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .Where(element => CarpaNetPackages.Contains(element.Attribute("Include")?.Value, StringComparer.Ordinal))
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")?.Value,
                StringComparer.Ordinal);

        await Assert.That(versions.Keys).IsEquivalentTo(CarpaNetPackages);
        await Assert.That(versions.Values.All(version => version == ExpectedVersion)).IsTrue();
        await Assert.That(versions.Values.All(version => !version!.Contains('-', StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task CarpaNetReferences_MustStayInBffAndInfrastructure()
    {
        var repoRoot = ContextSystemHelpers.RepoRoot;
        var projectFiles = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories);
        var actualPackageOwners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var projectFile in projectFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile);
            var packageReferences = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(package => package is not null && CarpaNetPackages.Contains(package, StringComparer.Ordinal))
                .Cast<string>()
                .ToArray();

            foreach (var package in packageReferences)
            {
                actualPackageOwners.TryAdd(package, []);
                actualPackageOwners[package].Add(projectName);
            }

            if (document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Any(element => element.Attribute("Include")?.Value.Contains("CarpaNet", StringComparison.OrdinalIgnoreCase) == true))
            {
                violations.Add($"{projectName}: project reference to CarpaNet source");
            }
        }

        actualPackageOwners.TryGetValue("CarpaNet", out var coreOwners);
        actualPackageOwners.TryGetValue("CarpaNet.OAuth", out var oauthOwners);
        actualPackageOwners.TryGetValue("CarpaNet.Jetstream", out var jetstreamOwners);

        await Assert.That(coreOwners ?? []).IsEquivalentTo(["Explore.Infrastructure"]);
        await Assert.That(oauthOwners ?? []).IsEquivalentTo(["Explore.Blazor", "Explore.Infrastructure"]);
        await Assert.That(jetstreamOwners ?? []).IsEquivalentTo(["Explore.Infrastructure"]);

        foreach (var projectName in ForbiddenProjects)
        {
            using var assembly = CompiledDependencyGraph.ReadProject(projectName);
            violations.AddRange(assembly.MainModule.Types
                .Where(HasCarpaNetDependency)
                .Select(type => $"{projectName}:{type.FullName}"));
        }

        await Assert.That(violations).IsEmpty()
            .Because($"CarpaNet belongs only to the BFF and Infrastructure. Violations: {string.Join(", ", violations)}");
    }

    [Test]
    public async Task CarpaNetBoundary_RejectsCompiledDeclarationAndBodyOnlyDependencies()
    {
        using var assembly = CompiledDependencyGraph.ReadAssembly(typeof(AtprotoDependencyBoundaryTests).Assembly.Location);
        var probes = CompiledDependencyGraph.Types(assembly.MainModule)
            .Where(type => type.DeclaringType?.FullName == typeof(AtprotoDependencyBoundaryTests).FullName)
            .ToDictionary(type => type.Name, StringComparer.Ordinal);
        await Assert.That(HasCarpaNetDependency(probes[nameof(DeclarationProbe)])).IsTrue();
        await Assert.That(HasCarpaNetDependency(probes[nameof(BodyOnlyProbe)])).IsTrue();
        await Assert.That(HasCarpaNetDependency(probes[nameof(NestedBodyProbe)])).IsTrue();
        await Assert.That(HasCarpaNetDependency(probes[nameof(UnrelatedProbe)])).IsFalse();
        await Assert.That(CompiledDependencyGraph.Members(probes[nameof(NestedBodyProbe)])
            .OfType<MethodReference>().Any(IsJetstreamConstructor)).IsTrue();
    }

    [Test]
    public async Task CarpaNetLexicons_MustUseOnlyRepositoryLocalFiles()
    {
        var infrastructureProject = XDocument.Load(
            ContextSystemHelpers.RepoPath("Explore.Infrastructure", "Explore.Infrastructure.csproj"));
        var violations = ValidateProjectConfiguration(infrastructureProject, "Explore.Infrastructure");
        var lexiconFiles = infrastructureProject.Descendants()
            .Where(element => element.Name.LocalName == "LexiconFiles")
            .Select(element => Path.GetFileName(element.Attribute("Include")?.Value) ?? string.Empty)
            .ToArray();

        await Assert.That(violations).IsEmpty()
            .Because($"Lexicon generation must be local-only. Violations: {string.Join(", ", violations)}");
        await Assert.That(lexiconFiles).IsEquivalentTo(ExpectedLexicons);

        foreach (var fileName in ExpectedLexicons)
        {
            await Assert.That(File.Exists(ContextSystemHelpers.RepoPath("schemas", "lexicons", fileName))).IsTrue();
        }
    }

    [Test]
    public async Task CarpaNetGeneratedBindings_MustCompileIntoInfrastructure()
    {
        var infrastructureAssembly = System.Reflection.Assembly.Load("Explore.Infrastructure");
        var generatedTypes = infrastructureAssembly.GetTypes()
            .Where(type => type.Namespace is not null)
            .Select(type => $"{type.Namespace}.{type.Name}")
            .ToArray();

        await Assert.That(generatedTypes.Any(type => type.Contains("ComAtproto.Server", StringComparison.Ordinal))).IsTrue();
        await Assert.That(generatedTypes.Any(type => type.Contains("CommunityLexicon.Calendar", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task BoundaryVerifier_MustRejectNetworkResolutionAndForbiddenReferences()
    {
        var fixture = XDocument.Parse(
            """
            <Project>
              <PropertyGroup>
                <CarpaNet_LexiconAutoResolve>true</CarpaNet_LexiconAutoResolve>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="CarpaNet" />
                <LexiconResolve Include="com.atproto.server.getSession" />
              </ItemGroup>
            </Project>
            """);

        var violations = ValidateProjectConfiguration(fixture, "Explore.Application");

        await Assert.That(violations).Contains("CarpaNet_LexiconAutoResolve=true");
        await Assert.That(violations).Contains("LexiconResolve");
        await Assert.That(violations).Contains("PackageReference:CarpaNet");
    }

    [Test]
    public async Task JetstreamSocketMustStayContainedBehindExactCollectionAdapter()
    {
        using var assembly = CompiledDependencyGraph.ReadProject("Explore.Infrastructure");
        var types = assembly.MainModule.Types;
        var socketOwners = types.Where(type => CompiledDependencyGraph.Members(type)
                .OfType<MethodReference>().Any(IsJetstreamConstructor))
            .Select(type => type.FullName).ToArray();
        await Assert.That(socketOwners).IsEquivalentTo([
            "Explore.Infrastructure.Services.Federation.CarpaNetJetstreamEventSource",
            "Explore.Infrastructure.Services.Federation.CarpaNetJetstreamArchiveClient"]);
        var federation = types.Where(type => type.Namespace == "Explore.Infrastructure.Services.Federation").ToArray();
        var legacyTypes = new[]
        {
            "CarpaNet.Jetstream.JetstreamClient",
            "CarpaNet.Jetstream.JetstreamSubscribeOptions",
            "CarpaNet.Jetstream.JetstreamOptionsUpdate",
            "System.Net.WebSockets.ClientWebSocket"
        };
        await Assert.That(federation.SelectMany(CompiledDependencyGraph.Dependencies)
            .Where(type => legacyTypes.Contains(type.FullName, StringComparer.Ordinal))
            .Select(type => type.FullName).ToArray()).IsEmpty();
    }

    private static string[] ValidateProjectConfiguration(XDocument document, string projectName)
    {
        var violations = new List<string>();
        var forbiddenItems = new[] { "LexiconResolve", "LexiconResolveAuthority", "LexiconResolveHandle" };

        violations.AddRange(document.Descendants()
            .Where(element => forbiddenItems.Contains(element.Name.LocalName, StringComparer.Ordinal))
            .Select(element => element.Name.LocalName));

        if (document.Descendants().Any(element =>
                element.Name.LocalName == "CarpaNet_LexiconAutoResolve"
                && string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add("CarpaNet_LexiconAutoResolve=true");
        }

        if (ForbiddenProjects.Contains(projectName, StringComparer.Ordinal))
        {
            violations.AddRange(document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(package => package is not null && CarpaNetPackages.Contains(package, StringComparer.Ordinal))
                .Select(package => $"PackageReference:{package}"));
        }

        return [.. violations];
    }

    private static bool HasCarpaNetDependency(TypeDefinition type) =>
        CompiledDependencyGraph.Dependencies(type).Any(dependency =>
            dependency.Namespace == "CarpaNet" || dependency.Namespace.StartsWith("CarpaNet.", StringComparison.Ordinal));

    private static bool IsJetstreamConstructor(MethodReference method) =>
        method.Name == ".ctor" && method.DeclaringType.FullName == "CarpaNet.Jetstream.JetstreamV2Client";

    private sealed class DeclarationProbe
    {
        public CarpaNet.Jetstream.JetstreamV2Client? Client { get; set; }
    }

    private static class BodyOnlyProbe
    {
        public static Type Dependency() => typeof(CarpaNet.Jetstream.JetstreamV2Client);
    }

    private static class NestedBodyProbe
    {
        public static async Task<object> Dependency()
        {
            await Task.Yield();
            return new CarpaNet.Jetstream.JetstreamV2Client(
                new Uri("https://jetstream.example.test"), new CarpaNet.Jetstream.JetstreamV2ClientOptions());
        }
    }

    private static class UnrelatedProbe
    {
        public static Type Dependency() => typeof(string);
    }
}
