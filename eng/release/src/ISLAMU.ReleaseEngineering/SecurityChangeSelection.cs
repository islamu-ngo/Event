using System.Text;

namespace ISLAMU.ReleaseEngineering;

public static class SecurityChangeSelection
{
    [Flags]
    private enum BuildLanes
    {
        None = 0, App = 1, Domain = 2, Architecture = 4, Setup = 8,
        Infrastructure = 16, Blazor = 32, Secrets = 64,
        All = App | Domain | Architecture | Setup | Infrastructure | Blazor | Secrets
    }

    private static bool IsValidPath(string path) => !string.IsNullOrWhiteSpace(path)
        && path == path.Trim()
        && !path.Any(char.IsControl)
        && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Split('/').Any(segment => segment is "" or "." or "..");

    private static bool IsPublicDocumentation(string path) => path == "README.md"
        || (path.StartsWith("docs/public/", StringComparison.Ordinal)
            && path.EndsWith(".md", StringComparison.Ordinal));

    public static bool RequiresExecution(IEnumerable<string> changedPaths)
    {
        bool anyPath = false;
        foreach (string path in changedPaths)
        {
            anyPath = true;
            // Only known public prose can skip; new or ambiguous paths must run.
            if (!IsValidPath(path) || !IsPublicDocumentation(path))
            {
                return true;
            }
        }

        return !anyPath;
    }

    public static int RunBuildTest(string[] args, TextWriter output)
    {
        BuildLanes lanes = BuildLanes.None;
        string[] paths = ReadPaths(args);
        if (paths.Length == 0)
        {
            lanes = BuildLanes.All;
        }

        foreach (string path in paths)
        {
            if (!IsValidPath(path))
            {
                lanes = BuildLanes.All;
                break;
            }
            if (IsPublicDocumentation(path))
            {
                continue;
            }

            lanes |= path switch
            {
                _ when path.EndsWith(".csproj", StringComparison.Ordinal)
                    || path.EndsWith(".props", StringComparison.Ordinal)
                    || path.EndsWith(".targets", StringComparison.Ordinal)
                    || path.EndsWith("/packages.lock.json", StringComparison.Ordinal) => BuildLanes.All,
                "schemas/configuration-manifest-v1alpha2.schema.json"
                    or "schemas/tenant-configuration-package-v1alpha2.schema.json" => BuildLanes.Architecture,
                _ when In(path, "eng/configuration-manifest-schema", "tests/Event.Architecture.Tests",
                    "eng/tools/Explore.GeneratedContracts", "tests/Explore.GeneratedContracts.Tests",
                    "src/Explore.Diagnostic", "tests/Explore.Diagnostic.UnitTests") => BuildLanes.Architecture,
                _ when In(path, "src/Explore.Domain", "tests/Event.Domain.UnitTests") =>
                    BuildLanes.Domain | BuildLanes.App | BuildLanes.Architecture | BuildLanes.Setup | BuildLanes.Infrastructure,
                _ when In(path, "src/Explore.Application", "tests/Event.Application.UnitTests",
                    "src/Explore.API", "tests/Event.API.IntegrationTests") =>
                    BuildLanes.App | BuildLanes.Architecture | BuildLanes.Setup | BuildLanes.Infrastructure,
                _ when In(path, "src/Explore.Secrets", "tests/Explore.Secrets.UnitTests") =>
                    BuildLanes.Secrets | BuildLanes.Architecture | BuildLanes.Setup | BuildLanes.Infrastructure,
                _ when In(path, "src/Explore.Infrastructure", "src/Explore.Persistence", "src/Explore.Atproto.Transport",
                    "src/Event.MigrationService", "tests/Explore.Infrastructure.Tests", "tests/Event.Persistence.IntegrationTests") =>
                    BuildLanes.Architecture | BuildLanes.Setup | BuildLanes.Infrastructure,
                _ when In(path, "src/Explore.Blazor", "src/Explore.Blazor.Client", "src/Event.Web.BffHosting",
                    "tests/Explore.Blazor.Client.Tests", "tests/Explore.Blazor.IntegrationTests") =>
                    BuildLanes.Architecture | BuildLanes.Setup | BuildLanes.Blazor,
                _ when In(path, "src/Event.Wire.Contracts", "tests/Event.Wire.Contracts.UnitTests") =>
                    BuildLanes.Architecture | BuildLanes.Setup,
                _ when In(path, "src/Event.Standalone", "tests/Event.Standalone.IntegrationTests") =>
                    BuildLanes.Architecture | BuildLanes.Setup | BuildLanes.Infrastructure | BuildLanes.Blazor,
                _ => BuildLanes.All
            };
        }

        output.WriteLine($"run-tests={(lanes != BuildLanes.None ? "true" : "false")}");
        foreach ((string name, BuildLanes flag) in new[]
        {
            ("app", BuildLanes.App), ("domain", BuildLanes.Domain), ("architecture", BuildLanes.Architecture),
            ("setup", BuildLanes.Setup), ("infrastructure", BuildLanes.Infrastructure),
            ("blazor", BuildLanes.Blazor), ("secrets", BuildLanes.Secrets)
        })
        {
            output.WriteLine($"run-{name}-tests={(lanes.HasFlag(flag) ? "true" : "false")}");
        }
        return Program.Success;
    }

    private static bool In(string path, params string[] directories) =>
        directories.Any(directory => path.StartsWith(directory + "/", StringComparison.Ordinal));

    private static string[] ReadPaths(string[] args)
    {
        if (args.Length != 2)
        {
            return [];
        }
        try
        {
            return File.ReadAllLines(args[1], new UTF8Encoding(false, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 2)
        {
            output.WriteLine("run-tests=true");
            return Program.Success;
        }

        bool selected;
        try
        {
            selected = RequiresExecution(File.ReadLines(args[1], new UTF8Encoding(false, true)));
        }
        catch (IOException)
        {
            selected = true;
        }
        catch (UnauthorizedAccessException)
        {
            selected = true;
        }
        catch (ArgumentException)
        {
            selected = true;
        }

        output.WriteLine(selected ? "run-tests=true" : "run-tests=false");
        return Program.Success;
    }
}
