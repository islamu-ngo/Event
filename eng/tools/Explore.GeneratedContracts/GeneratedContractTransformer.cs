using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Explore.GeneratedContracts;

public sealed record TransformResult(
    int RecordCount,
    int InitAccessorCount,
    bool Changed);

public static class GeneratedContractTransformer
{
    private const string PolicyStamp =
        "// <generated-record-policy version=\"1\">";
    private const string RedactedPrintMembersMarker =
        "// Generated record values are intentionally omitted from diagnostic text.";
    private const string RedactedPrintMembers =
        """

        // Generated record values are intentionally omitted from diagnostic text.
        protected virtual bool PrintMembers(System.Text.StringBuilder builder)
        {
            return false;
        }

""";

    public static TransformResult TransformFile(
        string path,
        string mutableStatePolicyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Generated client source was not found.",
                fullPath);
        }

        string input = File.ReadAllText(fullPath);
        HashSet<string> mutableStateTypes =
            GeneratedContractPolicy.LoadMutableStateTypes(
                Path.GetFullPath(mutableStatePolicyPath));
        TransformOutput output = Transform(input, mutableStateTypes);
        if (output.RecordCount < 100)
        {
            throw new InvalidOperationException(
                $"Generated record policy found only {output.RecordCount} eligible contracts; expected at least 100.");
        }

        if (output.Text != input)
        {
            File.WriteAllText(fullPath, output.Text, new UTF8Encoding(false));
        }

        return new TransformResult(
            output.RecordCount,
            output.InitAccessorCount,
            output.Text != input);
    }

    internal static TransformOutput Transform(
        string input,
        IReadOnlySet<string> mutableStateTypes)
    {
        CompilationUnitSyntax root = Parse(input);
        GeneratedContractClassification classification =
            GeneratedContractPolicy.Classify(root, mutableStateTypes);
        TypeDeclarationSyntax[] generatedTypes = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(GeneratedContractPolicy.IsNJsonSchemaGenerated)
            .OrderBy(declaration => declaration.SpanStart)
            .ToArray();
        string[] unknownMutableTypes = mutableStateTypes
            .Except(
                classification.GeneratedTypeNames,
                StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknownMutableTypes.Length != 0)
        {
            throw new InvalidOperationException(
                "Mutable generated-contract policy contains unknown types: "
                + string.Join(", ", unknownMutableTypes));
        }

        var edits = new List<TextEdit>();
        HashSet<string> candidateNames =
            classification.RecordTypeNames.ToHashSet(
                StringComparer.Ordinal);
        int initAccessorCount = 0;
        foreach (TypeDeclarationSyntax declaration in generatedTypes)
        {
            bool eligible = candidateNames.Contains(
                declaration.Identifier.ValueText);
            if (eligible && declaration is ClassDeclarationSyntax classDeclaration)
            {
                edits.Add(new TextEdit(
                    classDeclaration.Keyword.Span,
                    "record class"));
            }
            else if (!eligible && declaration is RecordDeclarationSyntax recordDeclaration)
            {
                edits.Add(new TextEdit(
                    TextSpan.FromBounds(
                        recordDeclaration.Keyword.SpanStart,
                        recordDeclaration.ClassOrStructKeyword.Span.End),
                    "class"));
            }

            MethodDeclarationSyntax? redactedPrintMembers =
                declaration.Members
                    .OfType<MethodDeclarationSyntax>()
                    .SingleOrDefault(IsGeneratedPrintMembers);
            if (eligible && redactedPrintMembers is null)
            {
                edits.Add(new TextEdit(
                    new TextSpan(
                        declaration.CloseBraceToken.SpanStart,
                        0),
                    RedactedPrintMembers.TrimStart('\r', '\n')[4..]));
            }
            else if (!eligible && redactedPrintMembers is not null)
            {
                edits.Add(new TextEdit(
                    redactedPrintMembers.FullSpan,
                    string.Empty));
            }

            foreach (PropertyDeclarationSyntax property in declaration.Members
                .OfType<PropertyDeclarationSyntax>()
                .Where(property => property.AccessorList is not null))
            {
                bool extensionData = property.AttributeLists
                    .SelectMany(list => list.Attributes)
                    .Any(attribute => attribute.Name.ToString()
                        .EndsWith(
                            "JsonExtensionData",
                            StringComparison.Ordinal)
                        || attribute.Name.ToString()
                            .EndsWith(
                                "JsonExtensionDataAttribute",
                                StringComparison.Ordinal));
                foreach (AccessorDeclarationSyntax accessor in
                    property.AccessorList!.Accessors)
                {
                    if (eligible
                        && !extensionData
                        && (accessor.IsKind(
                                SyntaxKind.SetAccessorDeclaration)
                            || accessor.IsKind(
                                SyntaxKind.InitAccessorDeclaration)))
                    {
                        initAccessorCount++;
                        if (accessor.IsKind(
                            SyntaxKind.SetAccessorDeclaration))
                        {
                            edits.Add(new TextEdit(
                                accessor.Keyword.Span,
                                "init"));
                        }
                    }
                    else if ((!eligible || extensionData)
                        && accessor.IsKind(
                            SyntaxKind.InitAccessorDeclaration))
                    {
                        edits.Add(new TextEdit(
                            accessor.Keyword.Span,
                            "set"));
                    }
                }
            }
        }

        string transformed = ApplyEdits(input, edits);
        transformed = AddPolicyStamp(transformed);
        Parse(transformed);

        return new TransformOutput(
            transformed,
            classification.RecordTypeNames.Count,
            initAccessorCount);
    }

    internal static GeneratedContractClassification Classify(
        string input,
        IReadOnlySet<string> mutableStateTypes) =>
        GeneratedContractPolicy.Classify(
            Parse(input),
            mutableStateTypes);

    private static CompilationUnitSyntax Parse(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));
        Diagnostic[] errors = tree.GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(10)
            .ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException(
                "Generated client syntax is invalid: "
                + string.Join(
                    Environment.NewLine,
                    errors.Select(error => error.ToString())));
        }

        return tree.GetCompilationUnitRoot();
    }

    private static string ApplyEdits(
        string source,
        IEnumerable<TextEdit> edits)
    {
        var builder = new StringBuilder(source);
        foreach (TextEdit edit in edits
            .OrderByDescending(edit => edit.Span.Start))
        {
            builder.Remove(edit.Span.Start, edit.Span.Length);
            builder.Insert(edit.Span.Start, edit.Replacement);
        }

        return builder.ToString();
    }

    private static string AddPolicyStamp(string source)
    {
        if (source.Contains(PolicyStamp, StringComparison.Ordinal))
        {
            return source;
        }

        string newline = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        int nullableDirective = source.IndexOf(
            "#nullable enable",
            StringComparison.Ordinal);
        if (nullableDirective < 0)
        {
            throw new InvalidOperationException(
                "Generated client is missing its nullable directive.");
        }

        return source.Insert(
            nullableDirective,
            PolicyStamp + newline + newline);
    }

    private static bool IsGeneratedPrintMembers(
        MethodDeclarationSyntax method) =>
        method.Identifier.ValueText == "PrintMembers"
        && method.ToFullString().Contains(
            RedactedPrintMembersMarker,
            StringComparison.Ordinal);

    private sealed record TextEdit(
        TextSpan Span,
        string Replacement);

    internal sealed record TransformOutput(
        string Text,
        int RecordCount,
        int InitAccessorCount);
}
