using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Explore.AssuranceAudit;

/// <summary>Indexes evaluated MSBuild inputs; it does not infer assurance or execute data sources.</summary>
public static class PortfolioInventory
{
    public sealed record Report(IReadOnlyList<Project> Projects, IReadOnlyList<Source> Sources);
    public sealed record Project(string Path, string DefineConstants, IReadOnlyList<Asset> Assets);
    public sealed record Asset(string Path, string Kind, string Link, bool External, bool? Exists, string? Sha256);
    public sealed record Owner(string Project, string Link);
    public sealed record Source(string Path, string Sha256, IReadOnlyList<Owner> Owners, IReadOnlyList<Declaration> Declarations);
    public sealed record Declaration(string Type, int Line, bool Partial, IReadOnlyList<string> CompiledOwners, IReadOnlyList<string> Attributes,
        IReadOnlyList<string> BaseTypes, IReadOnlyList<TestMethod> Tests);
    public sealed record TestMethod(string Name, string Parameters, int Line, IReadOnlyList<string> Attributes);

    public static Report Create(string repositoryRoot, IEnumerable<string> metadataDocuments)
    {
        string root = Path.GetFullPath(repositoryRoot);
        var projects = new List<Project>();
        var owners = new SortedDictionary<string, List<Owner>>(StringComparer.Ordinal);
        foreach (string metadata in metadataDocuments)
        {
            using JsonDocument document = JsonDocument.Parse(metadata);
            JsonElement json = document.RootElement;
            if (!json.TryGetProperty("Properties", out JsonElement properties)
                || !properties.TryGetProperty("MSBuildProjectFullPath", out JsonElement projectPath)
                || projectPath.ValueKind != JsonValueKind.String
                || !json.TryGetProperty("Items", out JsonElement items)
                || items.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Expected MSBuild Properties.MSBuildProjectFullPath and Items metadata.");
            }

            string project = RelativePath(root, projectPath.GetString()!);
            if (projects.Any(existing => existing.Path == project))
            {
                throw new InvalidDataException($"Duplicate evaluated project: {project}");
            }

            string constants = properties.TryGetProperty("DefineConstants", out JsonElement defines)
                ? defines.GetString() ?? string.Empty : string.Empty;
            var assets = new List<Asset>();
            foreach (JsonProperty itemKind in items.EnumerateObject())
            {
                foreach (JsonElement item in itemKind.Value.EnumerateArray())
                {
                    if (!item.TryGetProperty("FullPath", out JsonElement fullPath)
                        || fullPath.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidDataException($"Missing FullPath for {project}/{itemKind.Name}.");
                    }

                    string path = RelativePath(root, fullPath.GetString()!, allowExternalAsset: itemKind.Name != "Compile");
                    string link = item.TryGetProperty("Link", out JsonElement linkValue)
                        ? linkValue.GetString() ?? string.Empty : string.Empty;
                    if (itemKind.Name == "Compile")
                    {
                        if (!owners.TryGetValue(path, out List<Owner>? compiledOwners))
                        {
                            compiledOwners = [];
                            owners.Add(path, compiledOwners);
                        }

                        compiledOwners.Add(new Owner(project, link));
                    }
                    else
                    {
                        string absolute = Path.Combine(root, path);
                        bool external = Path.IsPathRooted(path);
                        bool? exists = external ? null : File.Exists(absolute);
                        assets.Add(new Asset(path, itemKind.Name, link, external, exists, exists == true ? Hash(absolute) : null));
                    }
                }
            }

            projects.Add(new Project(project, constants, assets.OrderBy(asset => asset.Path, StringComparer.Ordinal)
                .ThenBy(asset => asset.Kind, StringComparer.Ordinal).ToArray()));
        }

        var sources = new List<Source>();
        foreach ((string path, List<Owner> compiledOwners) in owners)
        {
            string absolute = Path.Combine(root, path);
            string source = File.ReadAllText(absolute);
            Declaration[] declarations = compiledOwners
                .GroupBy(owner => projects.Single(project => project.Path == owner.Project).DefineConstants)
                .SelectMany(profile =>
                {
                    SyntaxTree tree = CSharpSyntaxTree.ParseText(source,
                        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
                            .WithPreprocessorSymbols(profile.Key.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                    if (tree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    {
                        throw new InvalidDataException($"Cannot index malformed C# input: {path}");
                    }

                    return tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .Select(type => new Declaration(
                    TypeName(type),
                    type.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    type.Modifiers.Any(SyntaxKind.PartialKeyword),
                    profile.Select(owner => owner.Project).Order(StringComparer.Ordinal).ToArray(),
                    Attributes(type.AttributeLists),
                    type.BaseList?.Types.Select(baseType => baseType.Type.ToString()).ToArray() ?? [],
                    type.Members.OfType<MethodDeclarationSyntax>()
                        .Where(method => method.AttributeLists.SelectMany(list => list.Attributes)
                            .Any(attribute => attribute.Name.ToString().Split('.').Last() is "Test" or "TestAttribute"))
                        .Select(method => new TestMethod(method.Identifier.ValueText, method.ParameterList.ToString(),
                            method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            Attributes(method.AttributeLists))).ToArray()));
                })
                .ToArray();
            sources.Add(new Source(path, Hash(absolute), compiledOwners
                .OrderBy(owner => owner.Project, StringComparer.Ordinal).ToArray(), declarations));
        }

        return new Report(projects.OrderBy(project => project.Path, StringComparer.Ordinal).ToArray(), sources);
    }

    private static string TypeName(TypeDeclarationSyntax type)
    {
        string[] namespaces = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse().Select(declaration => declaration.Name.ToString()).ToArray();
        string[] types = type.AncestorsAndSelf().OfType<TypeDeclarationSyntax>()
            .Reverse().Select(declaration => declaration.Identifier.ValueText + declaration.TypeParameterList).ToArray();
        return string.Join('.', namespaces.Concat(types));
    }

    private static string[] Attributes(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(list => list.Attributes).Select(attribute => attribute.ToString()).ToArray();

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string RelativePath(string root, string path, bool allowExternalAsset = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("Metadata paths must not be empty.");
        }

        string relative = Path.GetRelativePath(root, Path.GetFullPath(path, root));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)
            || relative.StartsWith("..\\", StringComparison.Ordinal))
        {
            if (allowExternalAsset)
            {
                return Path.GetFullPath(path, root);
            }

            throw new InvalidDataException("Metadata path escapes the selected repository root.");
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
