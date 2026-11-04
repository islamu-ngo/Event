using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Explore.AssuranceAudit;

public static class PersistenceCapabilityAudit
{
    public sealed record CompilationInput(string AssemblyName, IReadOnlyDictionary<string, string> Sources,
        IReadOnlyList<string> References, string DefineConstants = "", string LanguageVersion = "preview",
        bool AllowUnsafe = false, string OutputType = "Library");

    private sealed record Policy(int SchemaVersion, Capability[] Capabilities);
    private sealed record Capability(string Assembly, string Member, [property: JsonPropertyName("capability")] string CapabilityKind, string Reason,
        string ProviderGap, string[] AllowedApis, string IdentifierHandling, string ParameterHandling, string NativeTest);
    private sealed record Call(string Owner, string Target, string Api, Location Location, bool Raw);
    private static readonly JsonSerializerOptions PolicyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Consumes post-build MSBuild items, including emitted generator output, without executing source generators.</summary>
    public static IReadOnlyList<AssuranceDiagnostic> AnalyzeEvaluatedProjects(
        string repositoryRoot, IEnumerable<string> metadataDocuments, string policyJson)
    {
        string root = Path.GetFullPath(repositoryRoot);
        var inputs = new List<CompilationInput>();
        foreach (string metadata in metadataDocuments)
        {
            using var document = JsonDocument.Parse(metadata);
            var properties = document.RootElement.GetProperty("Properties");
            var items = document.RootElement.GetProperty("Items");
            string Property(string name) => properties.GetProperty(name).GetString()
                ?? throw new InvalidDataException($"Missing evaluated property {name}.");
            string SourcePath(string path)
            {
                string absolute = Path.GetFullPath(path, root);
                string relative = Path.GetRelativePath(root, absolute);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException("Compiled sources must belong to the selected repository.");
                return absolute;
            }
            string project = SourcePath(Property("MSBuildProjectFullPath"));
            if (!File.Exists(project)) throw new InvalidDataException("Evaluated project is missing.");
            if (!string.Equals(Property("EmitCompilerGeneratedFiles"), "true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Emit compiler-generated files before exporting the evaluated compilation.");
            if (!string.Equals(Property("PersistenceAuditExportReady"), "true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The persistence audit export target must complete before analysis.");
            string generatedPath = Property("CompilerGeneratedFilesOutputPath");
            if (string.IsNullOrWhiteSpace(generatedPath))
                throw new InvalidDataException("An explicit compiler-generated output directory is required.");
            string generatedDirectory = SourcePath(Path.GetFullPath(generatedPath, Path.GetDirectoryName(project)!));
            if (!Directory.Exists(generatedDirectory))
                throw new InvalidDataException("The declared generated-source export directory is missing.");
            var generatedFiles = items.GetProperty("PersistenceAuditGeneratedCompile").EnumerateArray()
                .Select(item => SourcePath(item.GetProperty("FullPath").GetString()!)).ToHashSet(StringComparer.Ordinal);
            if (!generatedFiles.SetEquals(Directory.EnumerateFiles(generatedDirectory, "*.cs", SearchOption.AllDirectories)))
                throw new InvalidDataException("Generated-source files differ from the completed build export.");
            var paths = items.GetProperty("Compile").EnumerateArray()
                .Select(item => SourcePath(item.GetProperty("FullPath").GetString()!));
            paths = paths.Concat(generatedFiles);
            var sourceFiles = paths.Distinct(StringComparer.Ordinal).ToArray();
            if (sourceFiles.Any(path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Compile inputs must be C# source files.");
            var sources = sourceFiles.ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
            var references = items.GetProperty("ReferencePath").EnumerateArray()
                .Select(item => Path.GetFullPath(item.GetProperty("FullPath").GetString()!, root)).ToArray();
            if (references.Length == 0) throw new InvalidDataException("Resolved assembly references are required.");
            inputs.Add(new(Property("AssemblyName"), sources, references, Property("DefineConstants"),
                Property("LangVersion"), bool.Parse(Property("AllowUnsafeBlocks")), Property("OutputType")));
        }
        return Analyze(inputs, policyJson);
    }

    public static IReadOnlyList<AssuranceDiagnostic> Analyze(IReadOnlyList<CompilationInput> inputs, string policyJson)
    {
        var diagnostics = new List<AssuranceDiagnostic>();
        Policy? policy;
        try { policy = JsonSerializer.Deserialize<Policy>(policyJson, PolicyOptions); }
        catch (JsonException) { policy = null; }
        if (policy is not { SchemaVersion: 1, Capabilities: not null })
            return [Diagnostic("persistence-policy", Location.None, "A version 1 exact-capability policy is required.")];
        if (inputs.Count == 0 || inputs.Any(input => input.Sources.Count == 0 || string.IsNullOrWhiteSpace(input.AssemblyName))
            || inputs.Select(input => input.AssemblyName).Distinct(StringComparer.Ordinal).Count() != inputs.Count)
            return [Diagnostic("persistence-input", Location.None, "Nonempty, distinct evaluated compilations are required.")];

        var compilations = new Dictionary<string, CSharpCompilation>(StringComparer.Ordinal);
        foreach (var input in inputs.OrderBy(value => value.AssemblyName, StringComparer.Ordinal))
        {
            if (!LanguageVersionFacts.TryParse(input.LanguageVersion, out var language))
                return [Diagnostic("persistence-input", Location.None, "The evaluated language version is invalid.")];
            var parseOptions = new CSharpParseOptions(language, preprocessorSymbols: input.DefineConstants
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var output = input.OutputType switch
            {
                "Exe" => OutputKind.ConsoleApplication,
                "WinExe" => OutputKind.WindowsApplication,
                "Library" => OutputKind.DynamicallyLinkedLibrary,
                _ => throw new InvalidDataException("Unknown evaluated OutputType.")
            };
            var compilation = CSharpCompilation.Create(input.AssemblyName,
                input.Sources.OrderBy(source => source.Key, StringComparer.Ordinal)
                    .Select(source => CSharpSyntaxTree.ParseText(source.Value, parseOptions, source.Key)),
                input.References.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(output, allowUnsafe: input.AllowUnsafe));
            compilations.Add(input.AssemblyName, compilation);
            foreach (var error in compilation.GetDiagnostics().Where(value => value.Severity == DiagnosticSeverity.Error))
                diagnostics.Add(Diagnostic("persistence-unresolved", error.Location,
                    $"{input.AssemblyName}: compilation must resolve before auditing ({error.Id})."));
        }
        if (diagnostics.Count != 0) return Sort(diagnostics);

        var approved = new Dictionary<string, Capability>(StringComparer.Ordinal);
        foreach (var capability in policy.Capabilities)
        {
            if (capability is null || string.IsNullOrWhiteSpace(capability.Assembly)
                || string.IsNullOrWhiteSpace(capability.Member) || string.IsNullOrWhiteSpace(capability.Reason)
                || string.IsNullOrWhiteSpace(capability.ProviderGap) || string.IsNullOrWhiteSpace(capability.IdentifierHandling)
                || string.IsNullOrWhiteSpace(capability.ParameterHandling) || string.IsNullOrWhiteSpace(capability.NativeTest)
                || capability.CapabilityKind is not ("schema" or "session" or "lock" or "provider-translation" or "native-test")
                || capability.AllowedApis is not { Length: > 0 }
                || capability.AllowedApis.Any(string.IsNullOrWhiteSpace)
                || capability.AllowedApis.Distinct(StringComparer.Ordinal).Count() != capability.AllowedApis.Length)
            {
                diagnostics.Add(Diagnostic("persistence-policy", Location.None, "Capability declarations must be complete and narrowly typed."));
                continue;
            }
            string key = Key(capability.Assembly, capability.Member);
            bool ownerExists = compilations.TryGetValue(capability.Assembly, out var compilation)
                && SourceMethods(compilation, capability.Member).Count() == 1;
            bool proofExists = compilations.Values.Sum(value => SourceMethods(value, capability.NativeTest)
                .Count(method => method.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "TUnit.Core.TestAttribute"
                    && attribute.AttributeClass.ContainingAssembly.Name == "TUnit.Core"))) == 1;
            if (!ownerExists || !proofExists || !approved.TryAdd(key, capability))
                diagnostics.Add(Diagnostic("persistence-policy", Location.None,
                    $"{key}: capability owner/native-test owner is missing, ambiguous or duplicated."));
        }

        var calls = new List<Call>();
        foreach (var compilation in compilations.Values)
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var observed = new HashSet<(int Start, string Api)>();
            foreach (var node in tree.GetRoot().DescendantNodes().Where(node => node is ExpressionSyntax))
            {
                var operation = model.GetOperation(node);
                if (operation is IDynamicInvocationOperation or IDynamicObjectCreationOperation)
                    diagnostics.Add(Diagnostic("persistence-unresolved", node.GetLocation(), "Dynamic dispatch cannot establish a persistence boundary."));
                ISymbol? target = operation switch
                {
                    IInvocationOperation invocation => Normalize(invocation.TargetMethod),
                    IMethodReferenceOperation reference => Normalize(reference.Method),
                    IObjectCreationOperation creation when creation.Constructor is not null => creation.Constructor.OriginalDefinition,
                    IPropertyReferenceOperation property when property.Parent is IAssignmentOperation assignment
                        && ReferenceEquals(assignment.Target, property) => property.Property.OriginalDefinition,
                    IPropertyReferenceOperation property when property.Property.IsIndexer => property.Property.OriginalDefinition,
                    _ => null
                };
                if (target is null) continue;
                string? api = target.GetDocumentationCommentId();
                if (api is null || !observed.Add((operation!.Syntax.SpanStart, api))) continue;
                ISymbol? owner = model.GetEnclosingSymbol(node.SpanStart);
                while (owner is not null && (owner.GetDocumentationCommentId() is null
                       || owner is IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction }))
                    owner = owner.ContainingSymbol;
                string ownerKey = Key(compilation.AssemblyName!, owner?.GetDocumentationCommentId() ?? "<initializer>");
                bool raw = IsRaw(target);
                if (raw || compilations.ContainsKey(target.ContainingAssembly.Name))
                    calls.Add(new(ownerKey, Key(target.ContainingAssembly.Name, api), api, operation.Syntax.GetLocation(), raw));
            }
        }

        var unsafeOwners = new HashSet<string>(StringComparer.Ordinal);
        var usedApis = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var call in calls.Where(value => value.Raw))
        {
            if (approved.TryGetValue(call.Owner, out var capability) && capability.AllowedApis.Contains(call.Api, StringComparer.Ordinal))
            {
                if (!usedApis.TryGetValue(call.Owner, out var used)) usedApis.Add(call.Owner, used = new(StringComparer.Ordinal));
                used.Add(call.Api);
                continue;
            }
            unsafeOwners.Add(call.Owner);
            diagnostics.Add(Diagnostic("persistence-unapproved", call.Location, $"{call.Owner} uses {call.Api} without an exact capability."));
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var call in calls.Where(value => !value.Raw))
                if (unsafeOwners.Contains(call.Target) && unsafeOwners.Add(call.Owner))
                {
                    diagnostics.Add(Diagnostic("persistence-unapproved", call.Location, $"{call.Owner} reaches unapproved persistence through {call.Target}."));
                    changed = true;
                }
        } while (changed);
        foreach (var (key, capability) in approved)
            if (!usedApis.TryGetValue(key, out var used) || capability.AllowedApis.Any(api => !used.Contains(api)))
                diagnostics.Add(Diagnostic("persistence-policy", Location.None, $"{key}: capability contains unused API grants."));
        return Sort(diagnostics);
    }

    private static IEnumerable<IMethodSymbol> SourceMethods(Compilation compilation, string id) =>
        DocumentationCommentId.GetSymbolsForDeclarationId(id, compilation).OfType<IMethodSymbol>()
            .Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly));

    private static IMethodSymbol Normalize(IMethodSymbol method)
    {
        method = (method.ReducedFrom ?? method).OriginalDefinition;
        while (method.OverriddenMethod is { } parent) method = parent.OriginalDefinition;
        return method;
    }

    private static bool IsRaw(ISymbol symbol)
    {
        string type = symbol.ContainingType?.ToDisplayString() ?? "";
        if (type is "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions"
            or "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")
            return symbol.Name.StartsWith("ExecuteSql", StringComparison.Ordinal)
                || symbol.Name.StartsWith("SqlQuery", StringComparison.Ordinal)
                || symbol.Name.StartsWith("FromSql", StringComparison.Ordinal) || symbol.Name == "GetDbConnection";
        if (type == "Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder" && symbol.Name == "Sql") return true;
        for (var owner = symbol.ContainingType; owner is not null; owner = owner.BaseType)
        {
            string name = owner.OriginalDefinition.ToDisplayString();
            if (name is "System.Data.Common.DbCommand" or "System.Data.IDbCommand"
                or "System.Data.Common.DbBatch" or "System.Data.Common.DbBatchCommand")
                return symbol.Name.StartsWith("Execute", StringComparison.Ordinal)
                    || symbol.Name is "CommandText" or "Prepare" or "PrepareAsync" or ".ctor" or "CreateBatchCommand";
            if (name is "System.Data.Common.DbConnection" or "System.Data.IDbConnection" or "System.Data.Common.DbDataSource")
                return symbol.Name is "CreateCommand" or "CreateBatch";
            if (name is "System.Data.Common.DbDataReader" or "System.Data.IDataRecord")
                return symbol.Name.StartsWith("Get", StringComparison.Ordinal) || symbol is IPropertySymbol { IsIndexer: true };
        }
        return false;
    }

    private static string Key(string assembly, string member) => $"{assembly}:{member}";
    private static AssuranceDiagnostic Diagnostic(string category, Location location, string message)
    {
        var line = location.GetLineSpan();
        return new(category, line.Path ?? "", line.StartLinePosition.Line + 1, line.StartLinePosition.Character + 1, message);
    }
    private static AssuranceDiagnostic[] Sort(IEnumerable<AssuranceDiagnostic> diagnostics) => diagnostics
        .Distinct().OrderBy(value => value.Path, StringComparer.Ordinal).ThenBy(value => value.Line)
        .ThenBy(value => value.Column).ThenBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Message, StringComparer.Ordinal).ToArray();
}
