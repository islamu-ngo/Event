namespace Event.Architecture.Tests;

using System.Reflection;
using NetArchTest.Rules;

public sealed class McpArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Explore.Domain.Event).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Explore.Application.ApplicationServicesRegistration).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    private static readonly string[] ProductSourceRoots =
    [
        "Explore.Domain",
        "Explore.Application",
        "Explore.Persistence",
        "Explore.Infrastructure",
        "Explore.API",
        "Explore.Blazor",
        "Explore.Blazor.Client"
    ];

    [Test]
    public async Task DomainAndApplication_ShouldNotReference_ModelContextProtocol()
    {
        var violations = new List<string>();
        AddModelContextProtocolDependencyViolations("Domain", DomainAssembly, violations);
        AddModelContextProtocolDependencyViolations("Application", ApplicationAssembly, violations);

        await Assert.That(violations).IsEmpty()
            .Because("MCP is a presentation adapter; Domain and Application must stay independent of the MCP SDK.");
    }

    [Test]
    public async Task ModelContextProtocolSourceReferences_ShouldStayInApiHost()
    {
        var violations = new List<string>();

        foreach (var sourceFile in EnumerateProductSourceFiles())
        {
            var relativePath = Path.GetRelativePath(ContextSystemHelpers.RepoRoot, sourceFile)
                .Replace(Path.DirectorySeparatorChar, '/');
            var source = await File.ReadAllTextAsync(sourceFile);
            if (!ContainsMcpSdkReference(source) || relativePath.StartsWith("Explore.API/", StringComparison.Ordinal))
            {
                continue;
            }

            violations.Add(relativePath);
        }

        await Assert.That(violations).IsEmpty()
            .Because("MCP SDK references must stay in the API composition/presentation host, not lower layers or clients.");
    }

    [Test]
    public async Task McpAdapterTypes_ShouldNotInject_RepositoriesDirectly()
    {
        var mcpTypes = ApiAssembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.API.Mcp", StringComparison.Ordinal) == true)
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var violations = new List<string>();

        foreach (var type in mcpTypes)
        {
            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    if (IsRepositoryContract(parameter.ParameterType))
                    {
                        violations.Add($"{type.FullName} constructor parameter '{parameter.Name}' uses {parameter.ParameterType.FullName}");
                    }
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("MCP tools/resources must delegate through MediatR or API services; direct repository access would bypass Application authorization, tenancy, and proposal boundaries.");
    }

    private static void AddModelContextProtocolDependencyViolations(
        string layerName,
        Assembly assembly,
        List<string> violations)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn("ModelContextProtocol")
            .GetResult();

        if (result.IsSuccessful)
        {
            return;
        }

        violations.AddRange((result.FailingTypes ?? [])
            .Select(type => $"{layerName}: {type.FullName ?? type.Name}"));
    }

    private static bool ContainsMcpSdkReference(string source)
        => source.Contains("ModelContextProtocol", StringComparison.Ordinal)
            || source.Contains("McpServerTool", StringComparison.Ordinal)
            || source.Contains("McpServerResource", StringComparison.Ordinal)
            || source.Contains("McpServerPrompt", StringComparison.Ordinal);

    private static IEnumerable<string> EnumerateProductSourceFiles()
        => ProductSourceRoots
            .Select(root => Path.Combine(ContextSystemHelpers.RepoRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal);

    private static bool IsGeneratedOrBuildOutput(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');

        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.EndsWith(".g.cs", StringComparison.Ordinal)
            || normalized.EndsWith(".Designer.cs", StringComparison.Ordinal);
    }

    private static bool IsRepositoryContract(Type type)
    {
        var namespaceName = type.Namespace ?? string.Empty;
        return type.Name.EndsWith("Repository", StringComparison.Ordinal)
            || type.Name.EndsWith("Repository`1", StringComparison.Ordinal)
            || namespaceName.Contains(".Repositories", StringComparison.Ordinal)
            || namespaceName.Contains(".Persistence", StringComparison.Ordinal);
    }
}
