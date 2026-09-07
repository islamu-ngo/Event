namespace ISLAMU.Event.Setup.Core.Environment;

public sealed record EnvironmentCatalogueResult
{
    private readonly EnvironmentDiagnostic[] _diagnostics;

    internal EnvironmentCatalogueResult(
        EnvironmentCatalogue? catalogue,
        IEnumerable<EnvironmentDiagnostic> diagnostics)
    {
        Catalogue = catalogue;
        _diagnostics = diagnostics.ToArray();
    }

    public EnvironmentCatalogue? Catalogue { get; }
    public IReadOnlyList<EnvironmentDiagnostic> Diagnostics =>
        Array.AsReadOnly((EnvironmentDiagnostic[])_diagnostics.Clone());
}

public sealed class EnvironmentCatalogue
{
    private readonly EnvironmentVariableDefinition[] _definitions;
    private readonly System.Collections.ObjectModel.ReadOnlyDictionary<string, EnvironmentVariableDefinition> _byKey;
    private readonly EnvironmentActivationGraph _graph;

    private EnvironmentCatalogue(
        IEnumerable<EnvironmentVariableDefinition> definitions,
        EnvironmentActivationGraph graph)
    {
        _definitions = definitions.OrderBy(item => item.Order)
            .ThenBy(item => item.Key, StringComparer.Ordinal).ToArray();
        _byKey = new System.Collections.ObjectModel.ReadOnlyDictionary<string, EnvironmentVariableDefinition>(
            _definitions.ToDictionary(item => item.Key, StringComparer.Ordinal));
        _graph = graph;
    }

    public IReadOnlyList<EnvironmentVariableDefinition> Definitions =>
        Array.AsReadOnly((EnvironmentVariableDefinition[])_definitions.Clone());
    public IReadOnlyList<string> Topologies => _graph.Topologies;
    public IReadOnlyList<string> Capabilities => _graph.Capabilities;
    public IReadOnlyList<string> Providers => _graph.Providers;

    public static EnvironmentCatalogueResult Create(
        IEnumerable<EnvironmentVariableDefinition> definitions,
        EnvironmentActivationGraph graph,
        IEnumerable<string>? secretBindingEnvironmentKeys = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(graph);
        EnvironmentVariableDefinition[] snapshot = definitions.ToArray();
        string[] secretKeys = (secretBindingEnvironmentKeys ?? [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var diagnostics = new List<EnvironmentDiagnostic>();
        ValidateDefinitions(snapshot, secretKeys, diagnostics);
        ValidateGraph(graph, snapshot, diagnostics);
        if (diagnostics.Count != 0)
        {
            return new EnvironmentCatalogueResult(
                null,
                diagnostics.OrderBy(item => item.Path, StringComparer.Ordinal)
                    .ThenBy(item => item.Code, StringComparer.Ordinal));
        }

        return new EnvironmentCatalogueResult(new EnvironmentCatalogue(snapshot, graph), []);
    }

    public EnvironmentVariableDefinition? Lookup(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.TryGetValue(key, out EnvironmentVariableDefinition? definition)
            ? definition
            : null;
    }

    public IReadOnlyList<EnvironmentVariableDefinition> Relevant(EnvironmentActivationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var relevant = _definitions.Where(definition =>
            Evaluate(definition.Activation, context, new HashSet<string>(StringComparer.Ordinal))).ToArray();
        return Array.AsReadOnly(relevant);
    }

    private bool Evaluate(
        EnvironmentActivationExpression expression,
        EnvironmentActivationContext context,
        HashSet<string> featurePath) => expression.Kind switch
    {
        EnvironmentActivationKind.Topology => string.Equals(
            expression.Identifier, context.Topology, StringComparison.Ordinal),
        EnvironmentActivationKind.Capability => context.HasCapability(expression.Identifier!),
        EnvironmentActivationKind.Provider => context.HasProvider(expression.Identifier!),
        EnvironmentActivationKind.All => expression.Operands.All(item => Evaluate(
            item, context, new HashSet<string>(featurePath, StringComparer.Ordinal))),
        EnvironmentActivationKind.Any => expression.Operands.Any(item => Evaluate(
            item, context, new HashSet<string>(featurePath, StringComparer.Ordinal))),
        EnvironmentActivationKind.Not => !Evaluate(expression.Operands[0], context,
            new HashSet<string>(featurePath, StringComparer.Ordinal)),
        EnvironmentActivationKind.Feature when expression.Identifier is not null =>
            EvaluateFeature(expression.Identifier, context, featurePath),
        _ => false,
    };

    private bool EvaluateFeature(
        string identifier,
        EnvironmentActivationContext context,
        HashSet<string> featurePath)
    {
        var branchPath = new HashSet<string>(featurePath, StringComparer.Ordinal);
        return branchPath.Add(identifier)
            && _graph.TryFeature(identifier, out EnvironmentActivationExpression? feature)
            && Evaluate(feature!, context, branchPath);
    }

    private static void ValidateDefinitions(
        EnvironmentVariableDefinition[] definitions,
        IReadOnlyList<string> secretKeys,
        List<EnvironmentDiagnostic> diagnostics)
    {
        var exactKeys = new HashSet<string>(StringComparer.Ordinal);
        var foldedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orders = new HashSet<int>();
        var dotenvOrders = new HashSet<int>();
        var composeOrders = new HashSet<int>();
        var knownKeys = definitions.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var registrySecrets = secretKeys.ToHashSet(StringComparer.Ordinal);
        for (int index = 0; index < definitions.Length; index++)
        {
            EnvironmentVariableDefinition item = definitions[index];
            string path = $"$.definitions[{index}]";
            if (!IsCanonicalKey(item.Key)) Add("catalogue-key-noncanonical", path, item, diagnostics);
            if (!exactKeys.Add(item.Key)) Add("catalogue-duplicate-key", path, item, diagnostics);
            else if (!foldedKeys.Add(item.Key)) Add("catalogue-key-case-collision", path, item, diagnostics);
            if (item.Order < 0 || !orders.Add(item.Order))
                Add("catalogue-order-collision", path, item, diagnostics);
            if (!Enum.IsDefined(item.Category) || !Enum.IsDefined(item.Sensitivity)
                || !Enum.IsDefined(item.Requirement) || !Enum.IsDefined(item.RestartBehavior))
                Add("catalogue-enum-invalid", path, item, diagnostics);
            if (item.Requirement == EnvironmentVariableRequirement.Defaulted && item.SafeDefault is null)
                Add("catalogue-default-missing", path, item, diagnostics);
            if (item.Requirement != EnvironmentVariableRequirement.Defaulted && item.SafeDefault is not null)
                Add("catalogue-default-classification", path, item, diagnostics);
            if (item.SafeDefault is not null
                && item.Sensitivity != EnvironmentVariableSensitivity.Public)
                Add("catalogue-default-sensitive", path, item, diagnostics);
            if (item.SafeDefault is not null && !IsSafeDefault(item.SafeDefault))
                Add("catalogue-default-unsafe", path, item, diagnostics);
            bool registrySecret = registrySecrets.Contains(item.Key);
            if (registrySecret != (item.Sensitivity == EnvironmentVariableSensitivity.Secret))
                Add("catalogue-secret-classification-mismatch", path, item, diagnostics);
            ValidateGeneration(item, path, dotenvOrders, composeOrders, diagnostics);
            if (!IsIdentifier(item.ValidatorId) || !IsDocumentation(item.Documentation))
                Add("catalogue-identifier-invalid", path, item, diagnostics);
        }

        foreach (string fake in secretKeys.Where(key => !knownKeys.Contains(key)))
            diagnostics.Add(new EnvironmentDiagnostic(
                "catalogue-secret-key-missing", "$.secretBindingEnvironmentKeys", fake, "secret"));
    }

    private static void ValidateGeneration(
        EnvironmentVariableDefinition item,
        string path,
        HashSet<int> dotenvOrders,
        HashSet<int> composeOrders,
        ICollection<EnvironmentDiagnostic> diagnostics)
    {
        EnvironmentGenerationPolicy generation = item.Generation;
        const EnvironmentGenerationSurface allSurfaces = EnvironmentGenerationSurface.Dotenv
            | EnvironmentGenerationSurface.Compose | EnvironmentGenerationSurface.Startup;
        if (generation.Surfaces == EnvironmentGenerationSurface.None
            || (generation.Surfaces & ~allSurfaces) != 0)
            Add("catalogue-generation-surface-invalid", path, item, diagnostics);
        bool dotenv = generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Dotenv);
        bool compose = generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Compose);
        if (dotenv != generation.DotenvOrder.HasValue
            || generation.DotenvOrder is < 0
            || generation.DotenvOrder.HasValue && !dotenvOrders.Add(generation.DotenvOrder.Value))
            Add("catalogue-dotenv-order-invalid", path, item, diagnostics);
        if (compose != generation.ComposeOrder.HasValue
            || generation.ComposeOrder is < 0
            || generation.ComposeOrder.HasValue && !composeOrders.Add(generation.ComposeOrder.Value))
            Add("catalogue-compose-order-invalid", path, item, diagnostics);
        if (generation.ComposeRequired && !compose)
            Add("catalogue-compose-required-invalid", path, item, diagnostics);
    }

    private static void ValidateGraph(
        EnvironmentActivationGraph graph,
        EnvironmentVariableDefinition[] definitions,
        List<EnvironmentDiagnostic> diagnostics)
    {
        foreach (string duplicate in graph.DuplicateFeatureKeys)
            diagnostics.Add(new EnvironmentDiagnostic(
                "activation-feature-duplicate", "$.features", duplicate, "activation"));
        foreach ((string feature, EnvironmentActivationExpression expression) in graph.Features)
        {
            ValidateExpression(expression, graph, $"$.features.{feature}", feature, diagnostics);
            DetectCycle(feature, feature, graph, [], diagnostics);
        }
        for (int index = 0; index < definitions.Length; index++)
            ValidateExpression(definitions[index].Activation, graph,
                $"$.definitions[{index}].activation", definitions[index].Key, diagnostics);
    }

    private static void ValidateExpression(
        EnvironmentActivationExpression expression,
        EnvironmentActivationGraph graph,
        string path,
        string owner,
        ICollection<EnvironmentDiagnostic> diagnostics)
    {
        bool identifierNode = expression.Kind is EnvironmentActivationKind.Topology
            or EnvironmentActivationKind.Capability or EnvironmentActivationKind.Provider
            or EnvironmentActivationKind.Feature;
        bool identifierValid = expression.Identifier is not null && IsIdentifier(expression.Identifier);
        bool arityValid = expression.Kind switch
        {
            EnvironmentActivationKind.All or EnvironmentActivationKind.Any => expression.Operands.Count >= 2,
            EnvironmentActivationKind.Not => expression.Operands.Count == 1,
            _ => expression.Operands.Count == 0,
        };
        if (identifierNode != identifierValid || !arityValid)
            diagnostics.Add(new EnvironmentDiagnostic("activation-shape-invalid", path, owner, "activation"));
        if (expression.Kind == EnvironmentActivationKind.Topology
            && expression.Identifier is not null && !graph.HasTopology(expression.Identifier))
            diagnostics.Add(new EnvironmentDiagnostic("activation-topology-unknown", path, owner, "activation"));
        if (expression.Kind == EnvironmentActivationKind.Capability
            && expression.Identifier is not null && !graph.HasCapability(expression.Identifier))
            diagnostics.Add(new EnvironmentDiagnostic("activation-capability-unknown", path, owner, "activation"));
        if (expression.Kind == EnvironmentActivationKind.Provider
            && expression.Identifier is not null && !graph.HasProvider(expression.Identifier))
            diagnostics.Add(new EnvironmentDiagnostic("activation-provider-unknown", path, owner, "activation"));
        if (expression.Kind == EnvironmentActivationKind.Feature
            && expression.Identifier is not null && !graph.Features.ContainsKey(expression.Identifier))
            diagnostics.Add(new EnvironmentDiagnostic("activation-feature-unknown", path, owner, "activation"));
        foreach (EnvironmentActivationExpression operand in expression.Operands)
            ValidateExpression(operand, graph, path, owner, diagnostics);
    }

    private static void DetectCycle(
        string origin,
        string current,
        EnvironmentActivationGraph graph,
        HashSet<string> path,
        ICollection<EnvironmentDiagnostic> diagnostics)
    {
        if (!path.Add(current))
        {
            if (string.Equals(current, origin, StringComparison.Ordinal))
                diagnostics.Add(new EnvironmentDiagnostic(
                    "activation-cycle", "$.features", origin, "activation"));
            return;
        }
        if (!graph.TryFeature(current, out EnvironmentActivationExpression? expression)) return;
        foreach (string reference in FeatureReferences(expression!))
            DetectCycle(origin, reference, graph, new HashSet<string>(path, StringComparer.Ordinal), diagnostics);
    }

    private static IEnumerable<string> FeatureReferences(EnvironmentActivationExpression expression) =>
        (expression.Kind == EnvironmentActivationKind.Feature && expression.Identifier is not null
            ? [expression.Identifier]
            : Array.Empty<string>()).Concat(expression.Operands.SelectMany(FeatureReferences));

    private static void Add(
        string code,
        string path,
        EnvironmentVariableDefinition definition,
        ICollection<EnvironmentDiagnostic> diagnostics) =>
        diagnostics.Add(new EnvironmentDiagnostic(code, path, definition.Key, "catalogue"));

    private static bool IsCanonicalKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128 || key[0] is < 'A' or > 'Z'
            || key[^1] == '_' || key.Contains("___", StringComparison.Ordinal)) return false;
        return key.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    }

    private static bool IsIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] is < 'a' or > 'z' || value[^1] == '-') return false;
        return value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    private static bool IsDocumentation(EnvironmentDocumentationMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.LocalizationKey)
        && !string.IsNullOrWhiteSpace(metadata.HelpKey)
        && IsIdentifier(metadata.Anchor);

    private static bool IsSafeDefault(string value) =>
        value.Length <= 256 && !value.Any(character => character is '$' or '`' or '\r' or '\n' or '\0'
            || character < ' ');
}
