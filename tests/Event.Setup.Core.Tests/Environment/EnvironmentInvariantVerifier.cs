namespace ISLAMU.Setup.Core.EnvironmentTests;

using System.Text;
using System.Text.RegularExpressions;

internal static partial class EnvironmentInvariantVerifier
{
    private static readonly string[] ForbiddenDiagnosticMemberFragments =
    [
        "Comment", "ConnectionString", "Default", "Description", "Email", "Help", "Host",
        "Message", "Secret", "Supplied", "Token", "Url", "Value",
    ];

    internal static string[] VerifyActivationGraph(ActivationGraphFixture graph)
    {
        var failures = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string feature, ActivationNode expression) in graph.Features)
            VisitExpression(feature, expression, graph, failures);

        foreach (string feature in graph.Features.Keys)
            DetectCycle(feature, feature, graph, [], failures);

        return failures.Order(StringComparer.Ordinal).ToArray();
    }

    internal static string[] VerifyCatalogue(IReadOnlyList<CatalogueDefinitionFixture> definitions)
    {
        var failures = new HashSet<string>(StringComparer.Ordinal);
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orders = new HashSet<int>();

        foreach (CatalogueDefinitionFixture definition in definitions)
        {
            if (!CanonicalEnvironmentKey().IsMatch(definition.Key))
                failures.Add("catalogue-key-noncanonical");
            if (!exact.Add(definition.Key))
                failures.Add("catalogue-duplicate-key");
            else if (!folded.Add(definition.Key))
                failures.Add("catalogue-key-case-collision");
            if (definition.Order < 0 || !orders.Add(definition.Order))
                failures.Add("catalogue-order-duplicate");
            if (definition.Requirement == "defaulted" && definition.SafeDefault is null)
                failures.Add("catalogue-default-missing");
            if (definition.Requirement != "defaulted" && definition.SafeDefault is not null)
                failures.Add("catalogue-default-classification");
            if (definition.SafeDefault is not null && definition.Sensitivity == "secret")
                failures.Add("catalogue-secret-default");
            if (definition.SafeDefault is not null && definition.Sensitivity == "sensitive")
                failures.Add("catalogue-sensitive-default");
        }

        return failures.Order(StringComparer.Ordinal).ToArray();
    }

    internal static string[] VerifyRelevantProjection(
        IReadOnlyList<CatalogueDefinitionFixture> definitions,
        ActivationGraphFixture graph,
        string topology,
        IReadOnlySet<string> capabilities,
        IReadOnlySet<string> providers,
        IReadOnlySet<string> includedKeys)
    {
        string[] expected = definitions
            .Where(definition => Evaluate(
                definition.Activation, graph, topology, capabilities, providers, []))
            .OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.Key, StringComparer.Ordinal)
            .Select(definition => definition.Key)
            .ToArray();
        string[] actual = includedKeys.Order(StringComparer.Ordinal).ToArray();
        string[] expectedSet = expected.Order(StringComparer.Ordinal).ToArray();
        return actual.SequenceEqual(expectedSet, StringComparer.Ordinal)
            ? []
            : ["catalogue-irrelevant-key-included"];
    }

    internal static string[] VerifySecretParity(
        IReadOnlyList<CatalogueDefinitionFixture> definitions,
        IReadOnlySet<string> machineSecretBindings)
    {
        var failures = new HashSet<string>(StringComparer.Ordinal);
        var keys = definitions.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        foreach (CatalogueDefinitionFixture definition in definitions)
        {
            bool registrySecret = machineSecretBindings.Contains(definition.Key);
            bool catalogueSecret = definition.Sensitivity == "secret";
            if (registrySecret != catalogueSecret)
                failures.Add("catalogue-secret-classification-mismatch");
        }

        if (machineSecretBindings.Any(key => !keys.Contains(key)))
            failures.Add("catalogue-fake-secret-binding");
        return failures.Order(StringComparer.Ordinal).ToArray();
    }

    internal static string[] VerifyDotenvText(string text)
    {
        var failures = new HashSet<string>(StringComparer.Ordinal);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > EnvironmentContractExpectedVectors.MaximumDotenvFileUtf8Bytes)
            failures.Add("dotenv-file-too-large");
        if (text.StartsWith('\uFEFF')) failures.Add("dotenv-bom-forbidden");
        if (text.Contains('\r')) failures.Add("dotenv-carriage-return-forbidden");
        if (text.Contains('\0') || text.Any(character => character < ' ' && character is not '\n'))
            failures.Add("dotenv-control-character");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var foldedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] lines = text.Split('\n');
        int entries = 0;
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (line.Length == 0 || line.StartsWith('#')) continue;
            entries++;
            if (Encoding.UTF8.GetByteCount(line) > EnvironmentContractExpectedVectors.MaximumDotenvLineUtf8Bytes)
                failures.Add("dotenv-line-too-large");
            if (line.StartsWith("export ", StringComparison.Ordinal))
                failures.Add("dotenv-export-forbidden");
            if (line.Length > 0 && char.IsWhiteSpace(line[0]) || line.Contains(" =", StringComparison.Ordinal))
                failures.Add("dotenv-whitespace-forbidden");

            int equals = line.IndexOf('=');
            if (equals < 0) failures.Add("dotenv-equals-missing");
            string key = equals < 0 ? line : line[..equals];
            string value = equals < 0 ? string.Empty : line[(equals + 1)..];
            if (!CanonicalEnvironmentKey().IsMatch(key)
                || key.Length > EnvironmentContractExpectedVectors.MaximumDotenvKeyCharacters)
                failures.Add("dotenv-key-invalid");
            if (!keys.Add(key)) failures.Add("dotenv-duplicate-key");
            else if (!foldedKeys.Add(key)) failures.Add("dotenv-key-case-collision");
            if (Encoding.UTF8.GetByteCount(value) > EnvironmentContractExpectedVectors.MaximumDotenvValueUtf8Bytes)
                failures.Add("dotenv-value-too-large");
            if (value.Contains('$') || value.Contains('`'))
                failures.Add("dotenv-expansion-forbidden");
            if (value.Contains(';') || value.Contains('>') || value.Contains('<') || value.Contains('|') || value.Contains('&')
                || value.Contains('#') && !value.StartsWith('"'))
                failures.Add("dotenv-trailing-syntax");
            if (value.StartsWith('\'')) failures.Add("dotenv-quote-invalid");
            if (value.StartsWith('"') && !value.EndsWith('"'))
                failures.Add(lines.Skip(lineIndex + 1).Any(candidate => candidate.Contains('"'))
                    ? "dotenv-multiline-forbidden" : "dotenv-quote-invalid");
            if (value.StartsWith('"') && value.LastIndexOf('"') != value.Length - 1)
                failures.Add("dotenv-trailing-syntax");
            if (value.StartsWith('"') && value.Contains("\\n", StringComparison.Ordinal))
                failures.Add("dotenv-escape-invalid");
        }

        if (entries > EnvironmentContractExpectedVectors.MaximumDotenvEntryCount)
            failures.Add("dotenv-count-exceeded");
        return failures.Order(StringComparer.Ordinal).ToArray();
    }

    internal static (string State, string[] Missing, string[] Blocked) ComputeReadiness(
        IReadOnlyList<CatalogueDefinitionFixture> relevant,
        IReadOnlyDictionary<string, string> entryKinds)
    {
        string[] missing = relevant
            .Where(item => item.Requirement == "required"
                && item.Sensitivity != "secret"
                && (!entryKinds.TryGetValue(item.Key, out string? kind) || kind == "empty"))
            .Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
        string[] blocked = relevant
            .Where(item => item.Requirement == "required"
                && item.Sensitivity == "secret"
                && (!entryKinds.TryGetValue(item.Key, out string? kind) || kind == "empty"))
            .Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
        string state = blocked.Length > 0 ? "blocked" : missing.Length > 0 ? "incomplete" : "ready";
        return (state, missing, blocked);
    }

    internal static string[] VerifyDiagnosticShape(Type diagnosticType)
    {
        string[] names = diagnosticType.GetProperties().Select(property => property.Name).ToArray();
        var failures = names
            .Where(name => ForbiddenDiagnosticMemberFragments.Any(fragment =>
                name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .Select(_ => "diagnostic-value-member")
            .ToHashSet(StringComparer.Ordinal);
        if (!names.Order(StringComparer.Ordinal).SequenceEqual(
                EnvironmentContractExpectedVectors.DiagnosticProperties, StringComparer.Ordinal))
            failures.Add("diagnostic-shape-drift");
        return failures.Order(StringComparer.Ordinal).ToArray();
    }

    internal static string[] VerifyDiagnosticValues(
        IEnumerable<EnvironmentDiagnosticFixture> diagnostics,
        IEnumerable<string> forbiddenValues)
    {
        string combined = string.Join('\n', diagnostics.SelectMany(item => new[]
        {
            item.Code, item.Path, item.Key, item.Category, item.SuppliedValue, item.Message,
        }).Where(value => value is not null));
        return forbiddenValues.Any(value => !string.IsNullOrEmpty(value)
                && combined.Contains(value, StringComparison.Ordinal))
            ? ["diagnostic-value-leak"]
            : [];
    }

    private static void VisitExpression(
        string owner,
        ActivationNode node,
        ActivationGraphFixture graph,
        ISet<string> failures)
    {
        if (node.Kind == "capability" && !graph.Capabilities.Contains(node.Identifier!))
            failures.Add("activation-unknown-capability");
        if (node.Kind == "provider" && !graph.Providers.Contains(node.Identifier!))
            failures.Add("activation-unknown-provider");
        if (node.Kind == "topology" && !graph.Topologies.Contains(node.Identifier!))
            failures.Add("activation-unknown-topology");
        if (node.Kind == "feature" && !graph.Features.ContainsKey(node.Identifier!))
            failures.Add("activation-unknown-feature");
        if (node.Kind == "feature" && node.Identifier == owner)
            failures.Add("activation-self-reference");
        foreach (ActivationNode operand in node.Operands)
            VisitExpression(owner, operand, graph, failures);
    }

    private static void DetectCycle(
        string origin,
        string current,
        ActivationGraphFixture graph,
        HashSet<string> path,
        ISet<string> failures)
    {
        if (!path.Add(current))
        {
            if (current == origin) failures.Add("activation-cycle");
            return;
        }

        if (graph.Features.TryGetValue(current, out ActivationNode? expression))
        {
            foreach (string dependency in FeatureReferences(expression))
                DetectCycle(origin, dependency, graph, new HashSet<string>(path, StringComparer.Ordinal), failures);
        }
    }

    private static IEnumerable<string> FeatureReferences(ActivationNode node) =>
        (node.Kind == "feature" && node.Identifier is not null ? [node.Identifier] : Array.Empty<string>())
        .Concat(node.Operands.SelectMany(FeatureReferences));

    private static bool Evaluate(
        ActivationNode node,
        ActivationGraphFixture graph,
        string topology,
        IReadOnlySet<string> capabilities,
        IReadOnlySet<string> providers,
        HashSet<string> path) => node.Kind switch
        {
            "topology" => node.Identifier == topology,
            "capability" => capabilities.Contains(node.Identifier!),
            "provider" => providers.Contains(node.Identifier!),
            "all" => node.Operands.All(value => Evaluate(value, graph, topology, capabilities, providers, path)),
            "any" => node.Operands.Any(value => Evaluate(value, graph, topology, capabilities, providers, path)),
            "not" => node.Operands.Count == 1 && !Evaluate(node.Operands[0], graph, topology, capabilities, providers, path),
            "feature" when node.Identifier is not null && path.Add(node.Identifier)
                && graph.Features.TryGetValue(node.Identifier, out ActivationNode? feature) =>
                    Evaluate(feature, graph, topology, capabilities, providers, path),
            _ => false,
        };

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalEnvironmentKey();
}
