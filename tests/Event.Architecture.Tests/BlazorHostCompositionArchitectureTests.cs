using System.Xml.Linq;

namespace Event.Architecture.Tests;

public sealed class BlazorHostCompositionArchitectureTests
{
    private static readonly string[] BlazorProjects =
    [
        "Explore.Blazor",
        "Explore.Blazor.Client"
    ];

    private static readonly string[] ForbiddenProjects =
    [
        "Explore.API",
        "Explore.Application",
        "Explore.Domain",
        "Explore.Infrastructure",
        "Explore.Persistence"
    ];

    [Test]
    public async Task PublicBlazorHostModules_AreCallableFromTheSplitCompositionRoot()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var profile = await ReadRepositoryFileAsync(repositoryRoot, "src", "Explore.Blazor", "Hosting", "BlazorHostProfile.cs");
        var services = await ReadRepositoryFileAsync(repositoryRoot, "src", "Explore.Blazor", "Hosting", "BlazorHostServiceCollectionExtensions.cs");
        var application = await ReadRepositoryFileAsync(repositoryRoot, "src", "Explore.Blazor", "Hosting", "BlazorHostApplicationExtensions.cs");
        var program = await ReadRepositoryFileAsync(repositoryRoot, "src", "Explore.Blazor", "Program.cs");

        await Assert.That(profile).Contains("public enum BlazorHostProfile");
        await Assert.That(profile).Contains("Split,");
        await Assert.That(profile).Contains("Combined");
        await Assert.That(services).Contains("public static class BlazorHostServiceCollectionExtensions");
        await Assert.That(services).Contains("public static WebApplicationBuilder AddBlazorHostServices(");
        await Assert.That(application).Contains("public static class BlazorHostApplicationExtensions");
        await Assert.That(application).Contains("public static async Task<WebApplication> InitializeBlazorHostAsync(");
        await Assert.That(application).Contains("public static WebApplication UseBlazorHostMiddleware(");
        await Assert.That(application).Contains("public static WebApplication MapBlazorHostEndpoints(");
        await Assert.That(program).Contains("const BlazorHostProfile hostProfile = BlazorHostProfile.Split;");
        await Assert.That(program).Contains("builder.AddBlazorHostServices(hostProfile, shutdownState);");
        await Assert.That(program).Contains("await app.InitializeBlazorHostAsync(hostProfile);");
        await Assert.That(program).Contains("app.UseBlazorHostMiddleware(hostProfile, shutdownState);");
        await Assert.That(program).Contains("app.MapBlazorHostEndpoints(hostProfile);");
    }

    [Test]
    public async Task BlazorProjects_RemainIsolatedFromBackendLayersAndCircularReferences()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var violations = new List<string>();

        foreach (var projectName in BlazorProjects)
        {
            var projectFile = Path.Combine(repositoryRoot, "src", projectName, $"{projectName}.csproj");
            violations.AddRange(FindForbiddenProjectReferences(projectFile)
                .Select(reference => $"{projectName} -> {reference}"));
            violations.AddRange(FindCircularProjectReferences(projectFile)
                .Select(cycle => $"{projectName}: {cycle}"));
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Blazor composition must use generated API contracts and reusable BFF modules without backend-layer or circular references. Violations: {string.Join(", ", violations)}");
    }

    private static async Task<string> ReadRepositoryFileAsync(string repositoryRoot, params string[] pathSegments) =>
        await File.ReadAllTextAsync(Path.Combine([repositoryRoot, .. pathSegments]));

    private static IEnumerable<string> FindForbiddenProjectReferences(string projectFile)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(projectFile));

        while (pending.TryPop(out var currentProject))
        {
            if (!visited.Add(currentProject))
            {
                continue;
            }

            foreach (var reference in GetProjectReferences(currentProject))
            {
                var forbiddenProject = ForbiddenProjects.FirstOrDefault(project =>
                    reference.Include.Contains(project, StringComparison.OrdinalIgnoreCase));
                if (forbiddenProject is not null)
                {
                    yield return forbiddenProject;
                    continue;
                }

                if (reference.Path is not null)
                {
                    pending.Push(reference.Path);
                }
            }
        }
    }

    private static IEnumerable<string> FindCircularProjectReferences(string projectFile) =>
        FindCircularProjectReferences(Path.GetFullPath(projectFile), []);

    private static IEnumerable<string> FindCircularProjectReferences(string projectFile, List<string> ancestry)
    {
        ancestry.Add(projectFile);

        foreach (var reference in GetProjectReferences(projectFile))
        {
            if (reference.Path is null)
            {
                continue;
            }

            var cycleStart = ancestry.FindIndex(path =>
                string.Equals(path, reference.Path, StringComparison.OrdinalIgnoreCase));
            if (cycleStart >= 0)
            {
                yield return string.Join(" -> ", ancestry[cycleStart..]
                    .Append(reference.Path)
                    .Select(Path.GetFileNameWithoutExtension));
                continue;
            }

            foreach (var cycle in FindCircularProjectReferences(reference.Path, ancestry))
            {
                yield return cycle;
            }
        }

        ancestry.RemoveAt(ancestry.Count - 1);
    }

    private static IEnumerable<(string Include, string? Path)> GetProjectReferences(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var document = XDocument.Load(projectFile);

        foreach (var reference in document.Descendants()
                     .Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            var path = include.Contains("$(", StringComparison.Ordinal)
                ? null
                : Path.GetFullPath(Path.Combine(projectDirectory, include));
            yield return (include, path is not null && File.Exists(path) ? path : null);
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the architecture test output directory.");
    }
}
