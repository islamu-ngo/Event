using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Explore.GeneratedContracts;

internal static class GeneratedContractPolicy
{
    private static readonly string[] ProtectedPrefixes =
    [
        "Hal",
        "Patch",
        "Update",
    ];

    private static readonly HashSet<string> ProtectedNames =
        new(StringComparer.Ordinal)
        {
            "FileContentResult",
        };

    public static HashSet<string> DiscoverProtocolInputTypes(
        CompilationUnitSyntax root)
    {
        InterfaceDeclarationSyntax[] apiClients = root.DescendantNodes()
            .OfType<InterfaceDeclarationSyntax>()
            .Where(IsNSwagGenerated)
            .ToArray();
        if (apiClients.Length == 0)
        {
            throw new InvalidOperationException(
                "Generated contract source declares no NSwag client interface.");
        }

        HashSet<string> generatedNames = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(IsNJsonSchemaGenerated)
            .Select(declaration => declaration.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string[]> references = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(IsNJsonSchemaGenerated)
            .ToDictionary(
                declaration => declaration.Identifier.ValueText,
                declaration => declaration.Members
                    .OfType<PropertyDeclarationSyntax>()
                    .SelectMany(property => property.Type
                        .DescendantNodesAndSelf()
                        .OfType<IdentifierNameSyntax>())
                    .Select(identifier => identifier.Identifier.ValueText)
                    .Where(generatedNames.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        HashSet<string> protocolInputs = apiClients
            .SelectMany(apiClient => apiClient.Members
                .OfType<MethodDeclarationSyntax>())
            .SelectMany(method => method.ParameterList.Parameters)
            .Where(parameter => parameter.Type is not null)
            .SelectMany(parameter => parameter.Type!
                .DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>())
            .Select(identifier => identifier.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        var pending = new Queue<string>(protocolInputs);
        while (pending.TryDequeue(out string? input))
        {
            if (!references.TryGetValue(input, out string[]? nestedInputs))
            {
                continue;
            }

            foreach (string nestedInput in nestedInputs)
            {
                if (protocolInputs.Add(nestedInput))
                {
                    pending.Enqueue(nestedInput);
                }
            }
        }

        return protocolInputs;
    }

    public static GeneratedContractClassification Classify(
        CompilationUnitSyntax root,
        IReadOnlySet<string> mutableStateTypes)
    {
        TypeDeclarationSyntax[] generatedTypes = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(IsNJsonSchemaGenerated)
            .OrderBy(declaration => declaration.SpanStart)
            .ToArray();
        HashSet<string> protocolInputTypes =
            DiscoverProtocolInputTypes(root);
        HashSet<string> generatedTypeNameSet = generatedTypes
            .Select(declaration => declaration.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> inheritanceTypeNames = generatedTypes
            .Where(declaration => declaration.BaseList is not null)
            .SelectMany(declaration => declaration.BaseList!.Types
                .SelectMany(baseType => baseType.Type
                    .DescendantNodesAndSelf()
                    .OfType<IdentifierNameSyntax>())
                .Select(identifier => identifier.Identifier.ValueText)
                .Where(generatedTypeNameSet.Contains)
                .Append(declaration.Identifier.ValueText))
            .ToHashSet(StringComparer.Ordinal);
        string[] generatedTypeNames = generatedTypes
            .Select(declaration => declaration.Identifier.ValueText)
            .ToArray();
        string[] recordTypeNames = generatedTypes
            .Where(declaration => IsEligible(
                declaration,
                protocolInputTypes,
                mutableStateTypes,
                inheritanceTypeNames))
            .Select(declaration => declaration.Identifier.ValueText)
            .ToArray();

        return new GeneratedContractClassification(
            generatedTypeNames,
            protocolInputTypes.Order(StringComparer.Ordinal).ToArray(),
            recordTypeNames);
    }

    public static bool IsEligible(
        TypeDeclarationSyntax declaration,
        IReadOnlySet<string> protocolInputTypes,
        IReadOnlySet<string> mutableStateTypes,
        IReadOnlySet<string> inheritanceTypeNames)
    {
        string name = declaration.Identifier.ValueText;
        return IsNJsonSchemaGenerated(declaration)
            && declaration.BaseList is null
            && !protocolInputTypes.Contains(name)
            && !mutableStateTypes.Contains(name)
            && !inheritanceTypeNames.Contains(name)
            && !ProtectedNames.Contains(name)
            && !ProtectedPrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static HashSet<string> LoadMutableStateTypes(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Mutable generated-contract policy was not found.",
                path);
        }

        string[] names = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length != 0
                && !line.StartsWith('#'))
            .ToArray();
        HashSet<string> result = names.ToHashSet(StringComparer.Ordinal);
        if (result.Count != names.Length)
        {
            throw new InvalidOperationException(
                "Mutable generated-contract policy contains duplicate names.");
        }

        return result;
    }

    public static bool IsNJsonSchemaGenerated(
        TypeDeclarationSyntax declaration) =>
        IsGeneratedByTool(declaration, "NJsonSchema");

    public static bool IsNSwagGenerated(
        TypeDeclarationSyntax declaration) =>
        IsGeneratedByTool(declaration, "NSwag");

    private static bool IsGeneratedByTool(
        TypeDeclarationSyntax declaration,
        string tool) =>
        declaration.AttributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute =>
                attribute.Name.ToString()
                    .EndsWith("GeneratedCode", StringComparison.Ordinal)
                || attribute.Name.ToString()
                    .EndsWith(
                        "GeneratedCodeAttribute",
                        StringComparison.Ordinal))
            .Select(attribute => attribute.ArgumentList?.Arguments
                .FirstOrDefault()
                ?.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Any(literal =>
                string.Equals(
                    literal.Token.ValueText,
                    tool,
                    StringComparison.Ordinal));
}

internal sealed record GeneratedContractClassification(
    IReadOnlyList<string> GeneratedTypeNames,
    IReadOnlyList<string> ProtocolInputTypeNames,
    IReadOnlyList<string> RecordTypeNames);
