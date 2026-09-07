namespace ISLAMU.Event.Setup.Core.Composition;

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

internal sealed class SetupCompositionException : Exception
{
    internal SetupCompositionException(SetupCompositionFailureCode code) : base("Setup composition failed.") => Code = code;
    internal SetupCompositionFailureCode Code { get; }
}

internal abstract record CompositionNode;
internal sealed record CompositionMap(IReadOnlyDictionary<string, CompositionNode> Entries) : CompositionNode;
internal sealed record CompositionSequence(IReadOnlyList<CompositionNode> Entries) : CompositionNode;
internal sealed record CompositionScalar(CompositionScalarKind Kind, string? Value) : CompositionNode;

internal enum CompositionScalarKind
{
    String,
    Integer,
    Boolean,
    Null,
    JsonNumber
}

internal sealed class CompositionBudget
{
    private readonly SetupCompositionLimits _limits;
    private int _nodes;
    private int _scalarCharacters;

    internal CompositionBudget(SetupCompositionLimits limits) => _limits = limits;
    internal int Nodes => _nodes;

    internal void Node(int depth)
    {
        if (depth > _limits.NestingDepth)
            throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
        try { _nodes = checked(_nodes + 1); }
        catch (OverflowException) { throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded); }
        if (_nodes > _limits.NormalizedNodes)
            throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
    }

    internal void Scalar(string? value)
    {
        int length = value?.Length ?? 0;
        if (length > _limits.ScalarCharacters)
            throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
        try { _scalarCharacters = checked(_scalarCharacters + length); }
        catch (OverflowException) { throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded); }
        if (_scalarCharacters > _limits.AggregateScalarCharacters)
            throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
    }
}

internal static class SetupCompositionNormalizer
{
    private static readonly HashSet<string> ForbiddenAuthorityKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "secret", "secrets", "password", "apiKey", "accessToken", "connectionString",
        "providerCredentials", "providerCoordinate", "providerCoordinates", "applicationData",
        "publicationEvidence", "acceptanceEvidence", "userId", "tenantId", "targetTenantId"
    };

    internal static CompositionMap ParseJson(
        ReadOnlyMemory<byte> bytes, SetupCompositionLimits limits, CancellationToken cancellationToken,
        CompositionBudget? sharedBudget = null)
    {
        if (bytes.IsEmpty)
            throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
        if (bytes.Length > limits.AggregateSourceBytes)
            throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = limits.NestingDepth
            });
            var budget = sharedBudget ?? new CompositionBudget(limits);
            CompositionNode root = FromJson(document.RootElement, 1, budget, limits, cancellationToken);
            return root as CompositionMap
                ?? throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
        }
        catch (SetupCompositionException) { throw; }
        catch (JsonException)
        {
            throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
        }
    }

    internal static CompositionMap Merge(
        IEnumerable<CompositionMap> fragments, SetupCompositionLimits limits,
        CancellationToken cancellationToken)
    {
        var merged = new SortedDictionary<string, CompositionNode>(StringComparer.Ordinal);
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (CompositionMap fragment in fragments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MergeInto(merged, identities, fragment.Entries, limits, cancellationToken);
        }
        return new CompositionMap(merged);
    }

    internal static byte[] WriteJson(CompositionMap root, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteNode(writer, root, cancellationToken);
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static CompositionNode FromJson(
        JsonElement element, int depth, CompositionBudget budget, SetupCompositionLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        budget.Node(depth);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var entries = new SortedDictionary<string, CompositionNode>(StringComparer.Ordinal);
                var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int count = 0;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (checked(++count) > limits.MappingEntries)
                        throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
                    AddKey(entries, identities, property.Name,
                        FromJson(property.Value, checked(depth + 1), budget, limits, cancellationToken));
                }
                return new CompositionMap(entries);
            }
            case JsonValueKind.Array:
            {
                var entries = new List<CompositionNode>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (checked(entries.Count + 1) > limits.SequenceEntries)
                        throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
                    entries.Add(FromJson(item, checked(depth + 1), budget, limits, cancellationToken));
                }
                return new CompositionSequence(entries.AsReadOnly());
            }
            case JsonValueKind.String:
            {
                string value = element.GetString()!;
                budget.Scalar(value);
                return new CompositionScalar(CompositionScalarKind.String, value);
            }
            case JsonValueKind.Number:
            {
                string value = element.GetRawText();
                budget.Scalar(value);
                return new CompositionScalar(CompositionScalarKind.JsonNumber, value);
            }
            case JsonValueKind.True:
                budget.Scalar("true");
                return new CompositionScalar(CompositionScalarKind.Boolean, "true");
            case JsonValueKind.False:
                budget.Scalar("false");
                return new CompositionScalar(CompositionScalarKind.Boolean, "false");
            case JsonValueKind.Null:
                return new CompositionScalar(CompositionScalarKind.Null, null);
            default:
                throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
        }
    }

    internal static void AddKey(
        SortedDictionary<string, CompositionNode> entries, Dictionary<string, string> identities,
        string key, CompositionNode value)
    {
        if (string.IsNullOrEmpty(key))
            throw new SetupCompositionException(SetupCompositionFailureCode.InvalidKey);
        if (ForbiddenAuthorityKeys.Contains(key))
            throw new SetupCompositionException(SetupCompositionFailureCode.ForbiddenAuthority);
        if (entries.ContainsKey(key))
            throw new SetupCompositionException(SetupCompositionFailureCode.DuplicateKey);

        string normalized = key.Normalize(NormalizationForm.FormC);
        if (identities.TryGetValue(normalized, out _))
            throw new SetupCompositionException(SetupCompositionFailureCode.KeyCollision);
        identities.Add(normalized, key);
        entries.Add(key, value);
    }

    private static void MergeInto(
        SortedDictionary<string, CompositionNode> target, Dictionary<string, string> identities,
        IReadOnlyDictionary<string, CompositionNode> source, SetupCompositionLimits limits,
        CancellationToken cancellationToken)
    {
        foreach ((string key, CompositionNode incoming) in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!target.TryGetValue(key, out CompositionNode? existing))
            {
                AddKey(target, identities, key, incoming);
                continue;
            }

            if (existing is CompositionMap existingMap && incoming is CompositionMap incomingMap)
            {
                var nested = new SortedDictionary<string, CompositionNode>(StringComparer.Ordinal);
                foreach ((string nestedKey, CompositionNode nestedValue) in existingMap.Entries)
                    nested.Add(nestedKey, nestedValue);
                var nestedIdentities = nested.Keys.ToDictionary(
                    static key => key.Normalize(NormalizationForm.FormC), static key => key,
                    StringComparer.OrdinalIgnoreCase);
                MergeInto(nested, nestedIdentities, incomingMap.Entries, limits, cancellationToken);
                if (nested.Count > limits.MappingEntries)
                    throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
                target[key] = new CompositionMap(nested);
                continue;
            }

            throw new SetupCompositionException(SetupCompositionFailureCode.SourceConflict);
        }
    }

    private static void WriteNode(Utf8JsonWriter writer, CompositionNode node, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (node)
        {
            case CompositionMap map:
                writer.WriteStartObject();
                foreach ((string key, CompositionNode value) in map.Entries)
                {
                    writer.WritePropertyName(key);
                    WriteNode(writer, value, cancellationToken);
                }
                writer.WriteEndObject();
                break;
            case CompositionSequence sequence:
                writer.WriteStartArray();
                foreach (CompositionNode value in sequence.Entries)
                    WriteNode(writer, value, cancellationToken);
                writer.WriteEndArray();
                break;
            case CompositionScalar { Kind: CompositionScalarKind.String } scalar:
                writer.WriteStringValue(scalar.Value);
                break;
            case CompositionScalar { Kind: CompositionScalarKind.Integer or CompositionScalarKind.JsonNumber } scalar:
                writer.WriteRawValue(scalar.Value!, skipInputValidation: false);
                break;
            case CompositionScalar { Kind: CompositionScalarKind.Boolean } scalar:
                writer.WriteBooleanValue(string.Equals(scalar.Value, "true", StringComparison.Ordinal));
                break;
            case CompositionScalar { Kind: CompositionScalarKind.Null }:
                writer.WriteNullValue();
                break;
            default:
                throw new SetupCompositionException(SetupCompositionFailureCode.InvalidDocument);
        }
    }
}
