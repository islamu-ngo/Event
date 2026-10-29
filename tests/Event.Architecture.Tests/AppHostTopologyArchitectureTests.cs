using System.Xml.Linq;

namespace Event.Architecture.Tests;

public sealed class AppHostTopologyArchitectureTests
{
    private static readonly string RepoRoot = ContextSystemHelpers.RepoRoot;
    private static readonly string AppHostProjectPath = Path.Combine(RepoRoot, "src", "Explore.AppHost", "Explore.AppHost.csproj");
    private static readonly string StandaloneProjectPath = Path.Combine(RepoRoot, "src", "Event.Standalone", "Event.Standalone.csproj");
    private static readonly string[] RequiredSolutionProjects =
    [
        "src/Explore.API/Explore.API.csproj",
        "src/Explore.Blazor/Explore.Blazor.csproj",
        "src/Explore.AppHost/Explore.AppHost.csproj",
        "src/Event.Standalone/Event.Standalone.csproj"
    ];

    [Test]
    public async Task EventStandalone_CompositionRoot_MustReferenceOnlyHostsAndStartupMigrationsWithoutExpandingBlazorClientDependencies()
    {
        var standaloneReferences = ProjectReferences(StandaloneProjectPath);

        await Assert.That(standaloneReferences).IsEquivalentTo([
            "src/Explore.API/Explore.API.csproj",
            "src/Explore.Blazor/Explore.Blazor.csproj",
            "src/Explore.Persistence.DataProtection.Migrations.MySql/Explore.Persistence.DataProtection.Migrations.MySql.csproj",
            "src/Explore.Persistence.DataProtection.Migrations.Sqlite/Explore.Persistence.DataProtection.Migrations.Sqlite.csproj",
            "src/Explore.Persistence.DataProtection.Migrations.SqlServer/Explore.Persistence.DataProtection.Migrations.SqlServer.csproj",
            "src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj",
            "src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj",
            "src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj",
            "src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite.csproj"
        ]);
        const string standalone = "src/Event.Standalone/Event.Standalone.csproj";
        await Assert.That(ProjectReferences(Path.Combine(RepoRoot, "src", "Explore.Blazor.Client", "Explore.Blazor.Client.csproj")))
            .DoesNotContain(standalone);
        await Assert.That(ProjectReferences(Path.Combine(RepoRoot, "src", "Explore.Blazor", "Explore.Blazor.csproj")))
            .DoesNotContain(standalone);

        var appHostReferences = ProjectReferences(AppHostProjectPath);
        await Assert.That(appHostReferences).Contains(standalone);
        await Assert.That(appHostReferences).Contains("src/Explore.API/Explore.API.csproj");
        await Assert.That(appHostReferences).Contains("src/Explore.Blazor/Explore.Blazor.csproj");
    }

    [Test]
    public async Task CompositionRoots_MustBeInSolutionManifest()
    {
        var solutionProjects = XDocument.Load(Path.Combine(RepoRoot, "Explore.slnx"))
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => NormalizeProjectPath(RepoRoot, element.Attribute("Path")?.Value
                ?? throw new InvalidDataException("Solution project is missing its path.")))
            .ToArray();

        foreach (var requiredProject in RequiredSolutionProjects)
        {
            await Assert.That(solutionProjects.Count(project => project == requiredProject)).IsEqualTo(1);
        }
    }

    private static string[] ProjectReferences(string projectPath) => XDocument.Load(projectPath)
        .Descendants()
        .Where(element => element.Name.LocalName == "ProjectReference")
        .Select(element => NormalizeProjectPath(Path.GetDirectoryName(projectPath)!, element.Attribute("Include")?.Value
            ?? throw new InvalidDataException("Project reference is missing its include path.")))
        .ToArray();

    private static string NormalizeProjectPath(string directory, string path) => Path.GetRelativePath(
        RepoRoot, Path.GetFullPath(Path.Combine(directory, path.Replace('\\', Path.DirectorySeparatorChar))))
        .Replace('\\', '/');
}
