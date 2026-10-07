namespace ISLAMU.ReleaseEngineering;

public static class SecurityChangeSelection
{
    private static readonly string[] Prefixes =
    [
        "cerbos/",
        "docker/keycloak/",
        "src/Explore.Application/Authorization/",
        "src/Explore.API/Extensions/",
        "src/Explore.API/Hosting/",
        "src/Explore.API/Authentication/",
        "src/Explore.API/Middleware/",
        "src/Event.Standalone/",
        "src/Event.Web.BffHosting/",
        "src/Explore.Blazor/Services/",
        "src/Explore.Blazor/Yarp/",
        "tests/Event.API.IntegrationTests/Fixtures/",
        "tests/Event.API.IntegrationTests/Features/Security",
        "tests/Event.API.IntegrationTests/Features/Cerbos",
        "tests/Event.API.IntegrationTests/Features/Token",
        "tests/Event.API.IntegrationTests/Features/Authorization",
        "tests/Event.API.IntegrationTests/Features/Cloud"
    ];

    private static readonly string[] ExactPaths =
    [
        "src/Explore.API/Program.cs",
        "src/Explore.Blazor/Program.cs",
        "src/Explore.Infrastructure/Services/CerbosAuthorizationService.cs",
        "src/Explore.Infrastructure/Services/CerbosPrincipalBuilder.cs",
        "src/Explore.Infrastructure/Services/RuntimeAuthorizationProvider.cs",
        "src/Explore.Infrastructure/Services/LocalAuthorizationProvider.cs",
        "docs/SECURITY-MODEL.md",
        "docs/AUTHORIZATION.md",
        ".github/workflows/security-tests.yml"
    ];

    public static bool RequiresExecution(IEnumerable<string> changedPaths)
    {
        bool anyPath = false;
        foreach (string path in changedPaths)
        {
            if (path.Length == 0)
            {
                continue;
            }

            anyPath = true;
            if (ExactPaths.Contains(path, StringComparer.Ordinal) ||
                Prefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return !anyPath;
    }

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 2)
        {
            output.WriteLine("invalid_arguments: select-security-changes requires a changed-paths file");
            return Program.UsageError;
        }

        bool selected;
        try
        {
            selected = RequiresExecution(File.ReadLines(args[1]));
        }
        catch (IOException)
        {
            selected = true;
        }
        catch (UnauthorizedAccessException)
        {
            selected = true;
        }

        output.WriteLine(selected ? "run-tests=true" : "run-tests=false");
        return Program.Success;
    }
}
