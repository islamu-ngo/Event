namespace Event.Architecture.Tests;

public sealed class EventWebBffHostingArchitectureTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly string BffHostingRoot = Path.Combine(RepoRoot, "src", "Event.Web.BffHosting");

    [Test]
    public async Task EventWebBffHosting_Project_MustNotReferenceApplicationLayers()
    {
        var projectPath = Path.Combine(BffHostingRoot, "Event.Web.BffHosting.csproj");
        await Assert.That(File.Exists(projectPath)).IsTrue()
            .Because("Event.Web.BffHosting must exist as the shared browser-BFF hosting library.");

        var projectXml = await File.ReadAllTextAsync(projectPath);
        await Assert.That(projectXml.Contains("<ProjectReference", StringComparison.OrdinalIgnoreCase)).IsFalse()
            .Because("Event.Web.BffHosting must stay independent from UI, API, Application, Domain, Persistence, and Infrastructure projects.");
    }

    [Test]
    public async Task EventWebBffHosting_Source_MustNotDependOnForbiddenLayers()
    {
        var forbiddenTokens = new[]
        {
            "Explore.Application",
            "Explore.Domain",
            "Explore.Persistence",
            "Explore.Infrastructure",
            "Explore.API",
            "Explore.Blazor",
            "Explore.Blazor.Client",
            "Event.ControlPlane",
            "EventApiClient"
        };

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(BffHostingRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGeneratedOrBuildOutput(file))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(file);
            var relative = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            foreach (var token in forbiddenTokens)
            {
                if (content.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{relative} contains forbidden dependency token '{token}'");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because(string.Join('\n', violations));
    }

    [Test]
    public async Task ExploreBlazor_YarpProxyExtension_MustDelegateToSharedBffHosting()
    {
        var extensionPath = Path.Combine(RepoRoot, "src", "Explore.Blazor", "Extensions", "YarpProxyExtensions.cs");
        var source = await File.ReadAllTextAsync(extensionPath);

        await Assert.That(source.Contains("AddEventApiProxy", StringComparison.Ordinal)).IsTrue()
            .Because("Explore.Blazor must consume Event.Web.BffHosting for shared YARP proxy registration.");

        await Assert.That(source.Contains("LoadFromMemory", StringComparison.Ordinal)).IsFalse()
            .Because("YARP route/cluster construction must live in Event.Web.BffHosting to prevent proxy drift.");
    }

    private static bool IsGeneratedOrBuildOutput(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx"))
                && (Directory.Exists(Path.Combine(current.FullName, "Event.Web.BffHosting")) ||
                    Directory.Exists(Path.Combine(current.FullName, "src", "Event.Web.BffHosting"))))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Explore.slnx and Event.Web.BffHosting.");
    }
}
