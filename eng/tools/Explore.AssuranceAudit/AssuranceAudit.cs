using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;

namespace Explore.AssuranceAudit;

public static class AssuranceAudit
{
    public const string ReflectiveBehaviorDispatch = "reflective-behavior-dispatch";
    public const string StringSelectedProductionType = "string-selected-production-type";
    public const string RawProductSourceAssurance = "raw-product-source-assurance";
    public const string CommentedOutTest = "commented-out-test";
    public const string UngovernedUnconditionalSkip = "ungoverned-unconditional-skip";

    private static readonly HashSet<string> RawProductExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cs", ".razor", ".css", ".md" };

    private static readonly Lazy<IReadOnlyList<MetadataReference>> PlatformReferences = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray());

    public static IReadOnlyList<AssuranceDiagnostic> AnalyzeFiles(
        string repositoryRoot,
        IEnumerable<string> relativePaths)
    {
        var diagnostics = new List<AssuranceDiagnostic>();
        foreach (string relativePath in relativePaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            string fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
            IEnumerable<string> files = File.Exists(fullPath)
                ? [fullPath]
                : Directory.Exists(fullPath)
                    ? Directory.EnumerateFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                    : [];
            foreach (string file in files.Where(path => !IsBuildOutput(path)).Order(StringComparer.Ordinal))
            {
                string diagnosticPath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
                diagnostics.AddRange(AnalyzeSource(File.ReadAllText(file), diagnosticPath));
            }
        }

        return Sort(diagnostics);
    }

    public static IReadOnlyList<AssuranceDiagnostic> AnalyzeChangedFiles(string repositoryRoot, string baseRevision)
    {
        string[] changed = RunGit(repositoryRoot, "diff", "--name-only", "--diff-filter=ACMR", baseRevision)
            .Concat(RunGit(repositoryRoot, "ls-files", "--others", "--exclude-standard"))
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal)
                           && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return AnalyzeFiles(repositoryRoot, changed);
    }

    public static IReadOnlyList<AssuranceDiagnostic> AnalyzeSource(string source, string path)
        => AnalyzeSource(source, path, PlatformReferences.Value);

    internal static IReadOnlyList<AssuranceDiagnostic> AnalyzeSource(
        string source, string path, IReadOnlyList<string> referencePaths)
        => AnalyzeSource(source, path, referencePaths.Select(assembly => MetadataReference.CreateFromFile(assembly)).ToArray());

    private static IReadOnlyList<AssuranceDiagnostic> AnalyzeSource(
        string source, string path, IReadOnlyList<MetadataReference> references)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: path);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AssuranceAudit",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        SemanticModel semanticModel = compilation.GetSemanticModel(tree);
        var diagnostics = new List<AssuranceDiagnostic>();

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            ClassifyInvocation(invocation, semanticModel, diagnostics, path);
        }

        ClassifyTestGovernance(root, semanticModel, diagnostics, path);
        return Sort(diagnostics);
    }

    private static void ClassifyTestGovernance(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        ICollection<AssuranceDiagnostic> diagnostics,
        string path)
    {
        foreach (AttributeSyntax attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            if (FrameworkAttribute(attribute, attribute, semanticModel) != "Skip")
                continue;

            AttributeArgumentSyntax? reason = attribute.ArgumentList?.Arguments
                .FirstOrDefault(argument => argument.NameEquals is null);
            if (reason is null || semanticModel.GetConstantValue(reason.Expression).Value is not string value
                || !HasSkipGovernanceFields(value))
            {
                AddDiagnostic(diagnostics, attribute, path, UngovernedUnconditionalSkip,
                    "An unconditional TUnit Skip needs nonempty Category: and Removal: fields in its constant reason.");
            }
        }

        foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.AttributeLists.SelectMany(list => list.Attributes)
                .Any(attribute => FrameworkAttribute(attribute, attribute, semanticModel) == "Test"))
                continue;

            int headerEnd = method.Body?.OpenBraceToken.SpanStart
                ?? method.ExpressionBody?.ArrowToken.SpanStart
                ?? method.SemicolonToken.SpanStart;
            foreach (SyntaxTrivia trivia in method.DescendantTrivia().Where(trivia => trivia.SpanStart < headerEnd))
            {
                string? payload = trivia.Kind() switch
                {
                    SyntaxKind.SingleLineCommentTrivia => trivia.ToString()[2..],
                    SyntaxKind.MultiLineCommentTrivia => trivia.ToString()[2..^2],
                    _ => null
                };
                if (payload is null)
                    continue;

                var parsed = SyntaxFactory.ParseCompilationUnit(
                    "class CommentedMarker {\n" + payload + "\nvoid Marker() {}\n}");
                if (parsed.ContainsDiagnostics || !parsed.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .SelectMany(member => member.AttributeLists.SelectMany(list => list.Attributes))
                    .Any(attribute => FrameworkAttribute(attribute, method, semanticModel) == "Test"))
                    continue;

                AddDiagnostic(diagnostics, method, path, CommentedOutTest,
                    "A commented TUnit Test marker hides this method; use a governed Skip or remove the obsolete test.");
                break;
            }
        }
    }

    private static bool HasSkipGovernanceFields(string reason)
    {
        const string categoryLabel = "Category:";
        const string removalLabel = "Removal:";
        string fields = reason.Trim();
        if (!fields.StartsWith(categoryLabel, StringComparison.Ordinal))
            return false;

        fields = fields[categoryLabel.Length..];
        int removal = fields.IndexOf(removalLabel, StringComparison.Ordinal);
        return removal > 0
            && (char.IsWhiteSpace(fields[removal - 1]) || fields[removal - 1] == ';')
            && fields[..removal].Trim().TrimEnd(';').Trim().Length > 0
            && fields[(removal + removalLabel.Length)..].Trim().Length > 0;
    }

    private static string? FrameworkAttribute(
        AttributeSyntax attribute, SyntaxNode context, SemanticModel semanticModel)
    {
        if (attribute.SyntaxTree == semanticModel.SyntaxTree)
        {
            SymbolInfo binding = semanticModel.GetSymbolInfo(attribute);
            if (binding.CandidateReason == CandidateReason.Ambiguous || binding.CandidateSymbols.Length != 0)
                return null;
            if (binding.Symbol is IMethodSymbol constructor && constructor.ContainingType.TypeKind != TypeKind.Error)
                return FrameworkAttributeName(constructor.ContainingType.ToDisplayString());
        }

        var resolvedTypes = new HashSet<string>(StringComparer.Ordinal);
        string writtenName = attribute.Name.ToString();
        foreach (string candidate in writtenName.EndsWith("Attribute", StringComparison.Ordinal)
                     ? new[] { writtenName } : new[] { writtenName, writtenName + "Attribute" })
        {
            SymbolInfo binding = semanticModel.GetSpeculativeSymbolInfo(context.SpanStart,
                SyntaxFactory.ParseName(candidate), SpeculativeBindingOption.BindAsTypeOrNamespace);
            if (binding.CandidateReason == CandidateReason.Ambiguous || binding.CandidateSymbols.Length != 0)
                return null;
            ISymbol? symbol = binding.Symbol;
            if (symbol is IAliasSymbol alias)
                symbol = alias.Target;
            if (symbol is INamedTypeSymbol { TypeKind: not TypeKind.Error } type)
                resolvedTypes.Add(type.ToDisplayString());
        }
        if (resolvedTypes.Count != 0)
            return resolvedTypes.Count == 1 ? FrameworkAttributeName(resolvedTypes.Single()) : null;

        // The standalone audit has no TUnit reference. Its fallback recognizes the repository's
        // implicit Test/Skip convention and scoped aliases, not arbitrary unresolved attribute types.
        string name = writtenName.Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace("::", ".", StringComparison.Ordinal);
        var usings = context.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>()
            .SelectMany(declaration => declaration.Usings)
            .Concat(((CompilationUnitSyntax)semanticModel.SyntaxTree.GetRoot()).Usings)
            .ToArray();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(name))
        {
            int separator = name.IndexOf('.', StringComparison.Ordinal);
            string first = separator < 0 ? name : name[..separator];
            UsingDirectiveSyntax? alias = usings.FirstOrDefault(directive =>
                directive.Alias?.Name.Identifier.ValueText == first);
            if (alias?.Name is null)
                return FrameworkAttributeName(name.Contains('.', StringComparison.Ordinal) ? name : "TUnit.Core." + name);
            name = alias.Name.ToString().Replace("global::", string.Empty, StringComparison.Ordinal)
                + (separator < 0 ? string.Empty : name[separator..]);
        }
        return null;
    }

    private static string? FrameworkAttributeName(string qualifiedName) => qualifiedName switch
    {
        "TUnit.Core.Test" or "TUnit.Core.TestAttribute" => "Test",
        "TUnit.Core.Skip" or "TUnit.Core.SkipAttribute" => "Skip",
        _ => null
    };

    private static void ClassifyInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ICollection<AssuranceDiagnostic> diagnostics,
        string path)
    {
        if (IsProhibitedActivatorConstruction(invocation, semanticModel)
            || IsDispatchProxyConstruction(invocation, semanticModel)
            || IsReflectionDispatch(invocation, semanticModel))
        {
            AddDiagnostic(diagnostics, invocation, path, ReflectiveBehaviorDispatch,
                "Invoke behavior through a public typed contract.");
            return;
        }

        if (IsRuntimeTypeLookup(invocation))
        {
            AddDiagnostic(diagnostics, invocation, path, StringSelectedProductionType,
                "Reference compile-time production types with typeof or typed APIs.");
            return;
        }

        if (IsRawFileRead(invocation, semanticModel) && ResolvesRawProductPath(invocation, invocation))
        {
            AddDiagnostic(diagnostics, invocation, path, RawProductSourceAssurance,
                "Replace source or prose token checks with an executable seam or analyzer.");
        }
    }

    private static bool IsProhibitedActivatorConstruction(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        IMethodSymbol? method = ResolveMethod(invocation, semanticModel);
        return method?.Name == "CreateInstance"
            && method.ContainingType.ToDisplayString() == "System.Activator";
    }

    private static bool IsDispatchProxyConstruction(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        IMethodSymbol? method = ResolveMethod(invocation, semanticModel);
        return method?.Name == "Create"
            && method.ContainingType.ToDisplayString() == "System.Reflection.DispatchProxy";
    }

    private static bool IsReflectionDispatch(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        IMethodSymbol? method = ResolveMethod(invocation, semanticModel);
        if (method is null)
        {
            return false;
        }

        string containingType = method.ContainingType.ToDisplayString();
        return method.Name is "Invoke" or "GetValue" or "SetValue"
                   && method.ContainingNamespace.ToDisplayString() == "System.Reflection"
                   && method.ContainingType.Name is "MethodBase" or "MethodInfo" or "ConstructorInfo" or "FieldInfo" or "PropertyInfo"
            || method.Name == "InvokeMember" && containingType == "System.Type"
            || method.Name == "DynamicInvoke" && containingType == "System.Delegate";
    }

    private static bool IsRuntimeTypeLookup(InvocationExpressionSyntax invocation) =>
        GetInvokedMemberName(invocation) == "GetType"
        && invocation.ArgumentList.Arguments
            .SelectMany(argument => ResolveStringLiterals(argument.Expression, invocation))
            .Any();

    private static bool IsRawFileRead(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        IMethodSymbol? method = ResolveMethod(invocation, semanticModel);
        return method?.ContainingType.ToDisplayString() == "System.IO.File"
            && (method.Name.StartsWith("Read", StringComparison.Ordinal)
                || method.Name is "Open" or "OpenRead" or "OpenText" or "OpenHandle");
    }

    private static IMethodSymbol? ResolveMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        SymbolInfo symbol = semanticModel.GetSymbolInfo(invocation);
        return symbol.Symbol as IMethodSymbol
            ?? symbol.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static bool ResolvesRawProductPath(
        InvocationExpressionSyntax invocation,
        SyntaxNode scope)
    {
        string[] literals = invocation.ArgumentList.Arguments
            .SelectMany(argument => ResolveStringLiterals(argument.Expression, scope))
            .ToArray();
        bool rawExtension = literals.Any(value => RawProductExtensions.Contains(Path.GetExtension(value)));
        bool productPath = literals.Any(value => value.Equals("src", StringComparison.OrdinalIgnoreCase)
                                                 || value.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
                                                 || value.Equals("docs", StringComparison.OrdinalIgnoreCase)
                                                 || value.StartsWith("docs/", StringComparison.OrdinalIgnoreCase));
        return rawExtension && productPath;
    }

    private static IEnumerable<string> ResolveStringLiterals(ExpressionSyntax expression, SyntaxNode scope)
    {
        foreach (LiteralExpressionSyntax literal in expression.DescendantNodesAndSelf()
                     .OfType<LiteralExpressionSyntax>()
                     .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression)))
        {
            yield return literal.Token.ValueText;
        }

        foreach (IdentifierNameSyntax identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            VariableDeclaratorSyntax? variable = scope.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .LastOrDefault(candidate => candidate.SpanStart < expression.SpanStart
                                            && candidate.Identifier.ValueText == identifier.Identifier.ValueText);
            if (variable?.Initializer?.Value is { } initializer)
            {
                foreach (string value in ResolveStringLiterals(initializer, variable))
                {
                    yield return value;
                }
            }
        }
    }

    private static string GetInvokedMemberName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => string.Empty
        };

    private static void AddDiagnostic(
        ICollection<AssuranceDiagnostic> diagnostics,
        SyntaxNode node,
        string path,
        string category,
        string message)
    {
        FileLinePositionSpan span = node.GetLocation().GetLineSpan();
        diagnostics.Add(new AssuranceDiagnostic(
            category,
            path,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            message));
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private static string[] RunGit(string repositoryRoot, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git assurance inventory failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static AssuranceDiagnostic[] Sort(IEnumerable<AssuranceDiagnostic> diagnostics) =>
        diagnostics
            .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Column)
            .ToArray();
}
