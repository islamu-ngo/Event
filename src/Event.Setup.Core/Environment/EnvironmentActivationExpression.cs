namespace ISLAMU.Event.Setup.Core.Environment;

public abstract record EnvironmentActivationExpression
{
    private protected EnvironmentActivationExpression(
        EnvironmentActivationKind kind,
        string? identifier,
        IEnumerable<EnvironmentActivationExpression> operands)
    {
        Kind = kind;
        Identifier = identifier;
        Operands = Array.AsReadOnly(operands.ToArray());
    }

    public EnvironmentActivationKind Kind { get; }
    public string? Identifier { get; }
    public IReadOnlyList<EnvironmentActivationExpression> Operands { get; }

    public static EnvironmentActivationExpression Topology(string identifier) =>
        new IdentifierExpression(EnvironmentActivationKind.Topology, identifier);

    public static EnvironmentActivationExpression Capability(string identifier) =>
        new IdentifierExpression(EnvironmentActivationKind.Capability, identifier);

    public static EnvironmentActivationExpression Provider(string identifier) =>
        new IdentifierExpression(EnvironmentActivationKind.Provider, identifier);

    public static EnvironmentActivationExpression Feature(string identifier) =>
        new IdentifierExpression(EnvironmentActivationKind.Feature, identifier);

    public static EnvironmentActivationExpression All(params EnvironmentActivationExpression[] operands) =>
        new CompoundExpression(EnvironmentActivationKind.All, operands);

    public static EnvironmentActivationExpression Any(params EnvironmentActivationExpression[] operands) =>
        new CompoundExpression(EnvironmentActivationKind.Any, operands);

    public static EnvironmentActivationExpression Not(EnvironmentActivationExpression operand) =>
        new CompoundExpression(EnvironmentActivationKind.Not, [operand]);

    public override string ToString() => Kind.ToString();

    private sealed record IdentifierExpression : EnvironmentActivationExpression
    {
        internal IdentifierExpression(EnvironmentActivationKind kind, string identifier)
            : base(kind, identifier, [])
        {
        }
    }

    private sealed record CompoundExpression : EnvironmentActivationExpression
    {
        internal CompoundExpression(
            EnvironmentActivationKind kind,
            IEnumerable<EnvironmentActivationExpression> operands)
            : base(kind, null, operands)
        {
        }
    }
}

public sealed record EnvironmentActivationGraph
{
    private readonly string[] _topologies;
    private readonly string[] _capabilities;
    private readonly string[] _providers;
    private readonly string[] _duplicateFeatureKeys;
    private readonly System.Collections.ObjectModel.ReadOnlyDictionary<string, EnvironmentActivationExpression> _features;

    public EnvironmentActivationGraph(
        IEnumerable<string> topologies,
        IEnumerable<string> capabilities,
        IEnumerable<string> providers,
        IEnumerable<KeyValuePair<string, EnvironmentActivationExpression>> features)
    {
        ArgumentNullException.ThrowIfNull(topologies);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(features);
        _topologies = Snapshot(topologies);
        _capabilities = Snapshot(capabilities);
        _providers = Snapshot(providers);
        KeyValuePair<string, EnvironmentActivationExpression>[] featureSnapshot = features.ToArray();
        _duplicateFeatureKeys = featureSnapshot.GroupBy(item => item.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal).ToArray();
        var uniqueFeatures = new Dictionary<string, EnvironmentActivationExpression>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, EnvironmentActivationExpression> feature in
                 featureSnapshot.OrderBy(item => item.Key, StringComparer.Ordinal))
            uniqueFeatures.TryAdd(feature.Key, feature.Value);
        _features = new System.Collections.ObjectModel.ReadOnlyDictionary<string, EnvironmentActivationExpression>(
            uniqueFeatures);
    }

    public IReadOnlyList<string> Topologies => Array.AsReadOnly((string[])_topologies.Clone());
    public IReadOnlyList<string> Capabilities => Array.AsReadOnly((string[])_capabilities.Clone());
    public IReadOnlyList<string> Providers => Array.AsReadOnly((string[])_providers.Clone());
    public IReadOnlyDictionary<string, EnvironmentActivationExpression> Features =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, EnvironmentActivationExpression>(
            new Dictionary<string, EnvironmentActivationExpression>(_features, StringComparer.Ordinal));

    internal IReadOnlyList<string> DuplicateFeatureKeys =>
        Array.AsReadOnly((string[])_duplicateFeatureKeys.Clone());

    internal bool HasTopology(string value) => Array.BinarySearch(_topologies, value, StringComparer.Ordinal) >= 0;
    internal bool HasCapability(string value) => Array.BinarySearch(_capabilities, value, StringComparer.Ordinal) >= 0;
    internal bool HasProvider(string value) => Array.BinarySearch(_providers, value, StringComparer.Ordinal) >= 0;
    internal bool TryFeature(string value, out EnvironmentActivationExpression? expression) =>
        _features.TryGetValue(value, out expression);

    private static string[] Snapshot(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
