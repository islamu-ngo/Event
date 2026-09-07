namespace Event.Architecture.Tests;

internal static class ContextSystemHelpers
{
    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);

    public static string RepoRoot => RepoRootLazy.Value;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var solution = Path.Combine(dir.FullName, "Explore.slnx");
            if (File.Exists(solution) && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Repository root not found while walking up from the test output directory.");
    }

    private static readonly HashSet<string> SrcProjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "Explore.API", "Explore.AppHost", "Explore.Application", "Explore.Domain", "Explore.Persistence",
        "Explore.Infrastructure", "Explore.Blazor", "Explore.Blazor.Client", "Event.Web.BffHosting",
        "Explore.ServiceDefaults", "Explore.Secrets", "Explore.Diagnostic", "Event.MigrationService",
        "Explore.Persistence.Migrations.Sqlite", "Explore.Persistence.Migrations.SqlServer",
        "Explore.Persistence.Migrations.MySql"
    };

    public static string RepoPath(params string[] segments)
    {
        if (segments.Length > 0)
        {
            var first = segments[0];
            if (SrcProjects.Contains(first))
            {
                return Path.Combine(new[] { RepoRoot, "src" }.Concat(segments).ToArray());
            }
        }
        return Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray());
    }
}
