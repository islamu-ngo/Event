using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Event.Architecture.Tests;

public sealed class BlazorIsolationArchitectureTests
{
    private static readonly Regex TypeDeclaration = new(
        @"\b(?<modifiers>(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|file)\s+)*)(?:class|record(?:\s+(?:class|struct))?|struct|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);

    private static readonly string[] PresentationTypeSuffixes =
    [
        "AdminModel",
        "CommandResult",
        "DialogResult",
        "EditModel",
        "EditorModel",
        "EventArgs",
        "FormModel",
        "FormState",
        "QueryState",
        "Result",
        "Snapshot",
        "State",
        "UpdateModel",
        "ValueModel",
        "ViewModel",
        "ViewState"
    ];

    private static readonly string[] TransportTypeSuffixes =
    [
        "Collection",
        "DefinitionModel",
        "DetailModel",
        "Dto",
        "ListModel",
        "Model",
        "OptionModel",
        "Request",
        "Resource",
        "Response"
    ];

    private static readonly string[] BlazorProjects =
    [
        "Explore.Blazor",
        "Explore.Blazor.Client",
        "Explore.Blazor.IntegrationTests",
        "Explore.Blazor.Client.Tests"
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
    public async Task BlazorProjects_ShouldNotReferenceApiOwnedLayers()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var violations = new List<string>();

        foreach (var projectName in BlazorProjects)
        {
            var projectFile = Path.Combine(ResolveProjectPath(repositoryRoot, projectName), $"{projectName}.csproj");
            violations.AddRange(FindForbiddenProjectReferences(projectFile)
                .Select(forbidden => $"{projectName} -> {forbidden}"));
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Blazor projects communicate with the backend through generated API clients only. Violations: {string.Join(", ", violations)}");
    }

    [Test]
    public async Task BlazorSource_ShouldNotUseApiOwnedNamespaces()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var forbiddenNamespace = new Regex(
            @"\bExplore\.(?:API|Application|Domain|Infrastructure|Persistence)\b",
            RegexOptions.CultureInvariant);
        var violations = new List<string>();

        foreach (var projectName in BlazorProjects)
        {
            var projectRoot = ResolveProjectPath(repositoryRoot, projectName);
            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories)
                         .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                             || file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                         .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                             && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                var source = await File.ReadAllTextAsync(file);
                if (forbiddenNamespace.IsMatch(RemoveComments(source)))
                {
                    violations.Add(Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'));
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Blazor source must use generated API contracts or Blazor-owned types. Violations: {string.Join(", ", violations)}");
    }

    [Test]
    public async Task BlazorProductionBackendContracts_ShouldComeFromGeneratedApiClient()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var generatedClient = Path.Combine(
            ResolveProjectPath(repositoryRoot, "Explore.Blazor.Client"),
            "Clients",
            "EventApiTagClients.g.cs");
        var generatedContractNames = FindDeclaredTypeNames(await File.ReadAllTextAsync(generatedClient))
            .Select(NormalizeContractName)
            .ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var projectName in new[] { "Explore.Blazor", "Explore.Blazor.Client" })
        {
            var projectRoot = ResolveProjectPath(repositoryRoot, projectName);
            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(file => !file.EndsWith("EventApiTagClients.g.cs", StringComparison.OrdinalIgnoreCase))
                         .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                             && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
                var source = RemoveComments(await File.ReadAllTextAsync(file));

                foreach (Match declaration in TypeDeclaration.Matches(source))
                {
                    var typeName = declaration.Groups["name"].Value;
                    var modifiers = declaration.Groups["modifiers"].Value;
                    if (IsStaticType(modifiers)
                        || IsPermittedLocalProtocolShape(relativePath, typeName)
                        || IsPresentationType(typeName))
                    {
                        continue;
                    }

                    if (IsBackendContractShape(relativePath, typeName, generatedContractNames))
                    {
                        violations.Add($"{relativePath}: {typeName}");
                    }
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because($"Backend/domain contracts must be generated by NSwag into per-tag clients; only presentation state and local protocol/provider shapes may be Blazor-owned. Violations: {string.Join(", ", violations)}");
    }

    private static bool IsBackendContractShape(
        string relativePath,
        string typeName,
        HashSet<string> generatedContractNames)
    {
        if (relativePath.Contains("/Contracts/ControlPlane/", StringComparison.Ordinal))
        {
            return true;
        }

        return TransportTypeSuffixes.Any(suffix => typeName.EndsWith(suffix, StringComparison.Ordinal))
            || IsContractDataPath(relativePath)
                && generatedContractNames.Contains(NormalizeContractName(typeName));
    }

    private static bool IsPermittedLocalProtocolShape(string relativePath, string typeName)
    {
        if (relativePath.StartsWith("Explore.Blazor/", StringComparison.Ordinal) ||
            relativePath.StartsWith("src/Explore.Blazor/", StringComparison.Ordinal))
        {
            return relativePath.Contains("/Configuration/Infisical", StringComparison.Ordinal)
                || Path.GetFileName(relativePath).Contains("Bff", StringComparison.Ordinal)
                || typeName.StartsWith("Bff", StringComparison.Ordinal);
        }

        return relativePath.Contains("/Interop/", StringComparison.Ordinal)
            || relativePath.Contains("/Models/Analytics/", StringComparison.Ordinal)
            || Path.GetFileName(relativePath).Contains("Bff", StringComparison.Ordinal)
            || typeName.StartsWith("Bff", StringComparison.Ordinal);
    }

    private static bool IsPresentationType(string typeName) =>
        PresentationTypeSuffixes.Any(suffix => typeName.EndsWith(suffix, StringComparison.Ordinal));

    private static bool IsContractDataPath(string relativePath) =>
        relativePath.Contains("/Contracts/", StringComparison.Ordinal)
        || relativePath.Contains("/Models/", StringComparison.Ordinal);

    private static bool IsStaticType(string modifiers) =>
        modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("static", StringComparer.Ordinal);

    private static IEnumerable<string> FindDeclaredTypeNames(string source) =>
        TypeDeclaration.Matches(RemoveComments(source))
            .Select(match => match.Groups["name"].Value);

    private static string NormalizeContractName(string typeName)
    {
        foreach (var suffix in TransportTypeSuffixes)
        {
            if (typeName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return typeName[..^suffix.Length];
            }
        }

        return typeName;
    }

    private static string RemoveComments(string source)
    {
        var withoutBlocks = Regex.Replace(
            source,
            @"/\*.*?\*/|@\*.*?\*@",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        return string.Join(
            '\n',
            Regex.Replace(
                withoutBlocks,
                @"//.*$",
                string.Empty,
                RegexOptions.Multiline | RegexOptions.CultureInvariant).Split('\n'));
    }

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

            var projectDirectory = Path.GetDirectoryName(currentProject)!;
            var document = XDocument.Load(currentProject);
            foreach (var reference in document.Descendants()
                         .Where(element => element.Name.LocalName is "ProjectReference" or "Reference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                var forbiddenReference = ForbiddenProjects.FirstOrDefault(
                    project => include.Contains(project, StringComparison.OrdinalIgnoreCase));
                if (forbiddenReference is not null)
                {
                    yield return forbiddenReference;
                    continue;
                }

                if (reference.Name.LocalName == "Reference"
                    || include.Contains("$(", StringComparison.Ordinal))
                {
                    continue;
                }

                var referencedProject = Path.GetFullPath(Path.Combine(projectDirectory, include));
                if (File.Exists(referencedProject))
                {
                    pending.Push(referencedProject);
                }
            }
        }
    }

    private static string ResolveProjectPath(string repoRoot, string projectName)
    {
        var srcPath = Path.Combine(repoRoot, "src", projectName);
        if (Directory.Exists(srcPath)) return srcPath;
        var testsPath = Path.Combine(repoRoot, "tests", projectName);
        if (Directory.Exists(testsPath)) return testsPath;
        return Path.Combine(repoRoot, projectName);
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
