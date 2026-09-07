namespace ISLAMU.Setup.Core.EnvironmentTests;

using System.Collections;
using System.Reflection;
using ISLAMU.Event.Setup.Core;

internal sealed class EnvironmentContractRuntime
{
    private readonly Assembly _assembly = typeof(SetupProfile).Assembly;

    internal string[] MissingCataloguePrerequisites(string repositoryRoot)
    {
        var missing = MissingTypes(EnvironmentContractExpectedVectors.RequiredCatalogueProductTypes).ToList();
        if (!File.Exists(Path.Combine(
                repositoryRoot, EnvironmentContractExpectedVectors.MachineCatalogueRelativePath)))
            missing.Add("missing-machine-catalogue:" + EnvironmentContractExpectedVectors.MachineCatalogueRelativePath);
        return missing.Order(StringComparer.Ordinal).ToArray();
    }

    internal string[] MissingDotenvPrerequisites() =>
        MissingTypes(EnvironmentContractExpectedVectors.RequiredDotenvProductTypes);

    internal bool IsCatalogueComplete(string repositoryRoot) =>
        MissingCataloguePrerequisites(repositoryRoot).Length == 0;

    internal bool IsDotenvComplete() => MissingDotenvPrerequisites().Length == 0;

    internal Type RequireType(string shortName) => Type(shortName)
        ?? throw new InvalidOperationException(
            $"Missing final owner '{EnvironmentContractExpectedVectors.ProductNamespace}.{shortName}'.");

    internal string[] VerifyCataloguePublicSurface()
    {
        var failures = new List<string>();
        RequireExactProperties("EnvironmentVariableDefinition", EnvironmentContractExpectedVectors.DefinitionProperties, failures);
        Type diagnostic = RequireType("EnvironmentDiagnostic");
        failures.AddRange(EnvironmentInvariantVerifier.VerifyDiagnosticShape(diagnostic));
        RequireMethods("EnvironmentActivationExpression", EnvironmentContractExpectedVectors.RequiredExpressionFactories, failures);
        RequireMethods("EnvironmentCatalogue", EnvironmentContractExpectedVectors.RequiredCatalogueMethods, failures);

        Type expression = RequireType("EnvironmentActivationExpression");
        Type[] expressionClosure = _assembly.GetTypes()
            .Where(type => type != expression && expression.IsAssignableFrom(type))
            .ToArray();
        if (!expression.IsAbstract) failures.Add("activation-expression-not-closed-root");
        if (expressionClosure.Length == 0 || expressionClosure.Any(type => !type.IsSealed))
            failures.Add("activation-expression-open-node");
        foreach (Type type in expressionClosure.Append(expression))
        {
            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                Type? exposed = member switch
                {
                    PropertyInfo property => property.PropertyType,
                    MethodInfo method => method.ReturnType,
                    _ => null,
                };
                if (exposed is not null && IsExecutableExpressionType(exposed))
                    failures.Add("activation-expression-executable-language");
                if (member is MethodInfo publicMethod
                    && publicMethod.GetParameters().Any(parameter => IsExecutableExpressionType(parameter.ParameterType)))
                    failures.Add("activation-expression-executable-language");
                if (member.Name is "Parse" or "Compile" or "Eval")
                    failures.Add("activation-expression-string-language");
            }
        }

        return failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal string[] VerifyDotenvPublicSurface()
    {
        var failures = new List<string>();
        RequireExactProperties("DotenvEntry", EnvironmentContractExpectedVectors.EntryProperties, failures);
        RequireMethods("DotenvCodec", EnvironmentContractExpectedVectors.RequiredDotenvCodecMethods, failures);
        RequireMethods("DotenvReadiness", ["Evaluate"], failures);
        return failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal object ParseDotenv(byte[] bytes) => InvokeStatic(
        "DotenvCodec", "Parse", new ReadOnlyMemory<byte>(bytes));

    internal object RenderDotenv(object document, bool finalNewline) =>
        InvokeStatic("DotenvCodec", "Render", document, finalNewline);

    internal object CreateDotenvDocument(
        IEnumerable<(string Key, string? Value, string Kind, bool IsSecret, string Provenance)> entries)
    {
        Type entryType = RequireType("DotenvEntry");
        Type kindType = RequireType("DotenvEntryKind");
        Type provenanceType = RequireType("DotenvProvenance");
        Array values = Array.CreateInstance(entryType, entries.Count());
        int index = 0;
        foreach ((string key, string? value, string kind, bool isSecret, string provenance) in entries)
        {
            object entry = Activator.CreateInstance(entryType,
                key, value, Enum.Parse(kindType, kind), isSecret, Enum.Parse(provenanceType, provenance))
                ?? throw new InvalidOperationException("DotenvEntry construction returned null.");
            values.SetValue(entry, index++);
        }

        return Activator.CreateInstance(RequireType("DotenvDocument"), values)
            ?? throw new InvalidOperationException("DotenvDocument construction returned null.");
    }

    internal static object? Property(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);

    internal static string[] DiagnosticCodes(object result) =>
        ((IEnumerable?)Property(result, "Diagnostics") ?? Array.Empty<object>()).Cast<object>()
        .Select(item => Property(item, "Code")?.ToString() ?? string.Empty)
        .ToArray();

    internal static string[] PublicDiagnosticStrings(object result) =>
        ((IEnumerable?)Property(result, "Diagnostics") ?? Array.Empty<object>()).Cast<object>()
        .SelectMany(item => item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.GetValue(item) as string)
            .OfType<string>())
        .ToArray();

    internal static byte[] RenderedBytes(object result) => Property(result, "Bytes") switch
    {
        byte[] bytes => bytes,
        ReadOnlyMemory<byte> memory => memory.ToArray(),
        _ => throw new InvalidOperationException("DotenvRenderResult.Bytes must be byte[] or ReadOnlyMemory<byte>.")
    };

    internal static object RequiredProperty(object target, string name) => Property(target, name)
        ?? throw new InvalidOperationException($"Missing public result property '{name}'.");

    internal static IReadOnlyList<object> Entries(object document) =>
        ((IEnumerable?)Property(document, "Entries") ?? Array.Empty<object>()).Cast<object>().ToArray();

    private object InvokeStatic(string typeName, string methodName, params object[] arguments)
    {
        MethodInfo[] candidates = RequireType(typeName).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == methodName && method.GetParameters().Length == arguments.Length)
            .ToArray();
        foreach (MethodInfo candidate in candidates)
        {
            try
            {
                return candidate.Invoke(null, arguments)
                    ?? throw new InvalidOperationException($"{typeName}.{methodName} returned null.");
            }
            catch (ArgumentException)
            {
                // A same-arity overload may not own this contract shape.
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw exception.InnerException;
            }
        }
        throw new InvalidOperationException($"Missing compatible public method '{typeName}.{methodName}'.");
    }

    private void RequireExactProperties(string typeName, IEnumerable<string> expected, List<string> failures)
    {
        string[] actual = RequireType(typeName).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            failures.Add($"public-property-shape:{typeName}");
    }

    private void RequireMethods(string typeName, IEnumerable<string> expected, List<string> failures)
    {
        HashSet<string> actual = RequireType(typeName)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string name in expected.Where(name => !actual.Contains(name)))
            failures.Add($"missing-public-method:{typeName}.{name}");
    }

    private string[] MissingTypes(IEnumerable<string> names) => names
        .Where(name => Type(name) is null)
        .Select(name => $"missing-owner:{EnvironmentContractExpectedVectors.ProductNamespace}.{name}")
        .Order(StringComparer.Ordinal).ToArray();

    private Type? Type(string shortName) => _assembly.GetType(
        $"{EnvironmentContractExpectedVectors.ProductNamespace}.{shortName}", throwOnError: false);

    private static bool IsExecutableExpressionType(Type type)
    {
        Type candidate = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return typeof(Delegate).IsAssignableFrom(type)
            || candidate.FullName?.StartsWith("System.Linq.Expressions", StringComparison.Ordinal) == true
            || candidate.FullName?.StartsWith("System.Reflection", StringComparison.Ordinal) == true;
    }
}
