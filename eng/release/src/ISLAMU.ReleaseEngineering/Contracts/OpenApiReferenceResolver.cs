using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Result of resolving an OpenAPI 3.x Reference Object against its owning document root.
/// Retains the original raw JSON element so extensions and sibling overrides beside <c>$ref</c>
/// are never dropped during comparison.
/// </summary>
internal readonly record struct ResolvedOpenApiObject(
    JsonElement RawNode,
    JsonElement Target,
    bool IsReference,
    bool IsUnresolvedReference,
    bool HasNonProseSiblings,
    string? Reference);

/// <summary>
/// Cycle-bounded local JSON Pointer resolver and raw-reference sibling differ shared across
/// OpenAPI parameters, request bodies, and responses.
/// </summary>
internal static class OpenApiReferenceResolver
{
    private const int MaximumReferenceHops = 8;
    private const string RefKeyword = "$ref";
    private const string SummaryKeyword = "summary";
    private const string DescriptionKeyword = "description";
    private static readonly HashSet<string> ReferenceMetadataKeys = new(StringComparer.Ordinal)
    {
        RefKeyword,
        SummaryKeyword,
        DescriptionKeyword,
    };

    public static ResolvedOpenApiObject Resolve(JsonElement documentRoot, JsonElement node)
    {
        if (!JsonElementCanonicalizer.TryGetNonEmptyString(node, RefKeyword, out string? initialRef))
        {
            return new ResolvedOpenApiObject(
                node,
                node,
                IsReference: false,
                IsUnresolvedReference: false,
                HasNonProseSiblings: false,
                Reference: null);
        }

        bool hasNonProseSiblings = node.EnumerateObject().Any(prop => !ReferenceMetadataKeys.Contains(prop.Name));
        if (!TryFollowReferenceChain(documentRoot, initialRef, out JsonElement target))
        {
            return new ResolvedOpenApiObject(
                node,
                node,
                IsReference: true,
                IsUnresolvedReference: true,
                HasNonProseSiblings: hasNonProseSiblings,
                Reference: initialRef);
        }

        return new ResolvedOpenApiObject(
            node,
            target,
            IsReference: true,
            IsUnresolvedReference: false,
            HasNonProseSiblings: hasNonProseSiblings,
            Reference: initialRef);
    }

    public static void DiffRawReferenceFields(
        ResolvedOpenApiObject baseNode,
        ResolvedOpenApiObject headNode,
        string target,
        ContractChangeCollector collector)
    {
        if (!baseNode.IsReference && !headNode.IsReference)
        {
            return;
        }

        JsonElement rawBase = baseNode.IsReference ? baseNode.RawNode : default;
        JsonElement rawHead = headNode.IsReference ? headNode.RawNode : default;
        DiffExtensions(rawBase, rawHead, target, collector);
        DiffUnhandledFields(rawBase, rawHead, target, ReferenceMetadataKeys, collector);
    }

    public static void DiffExtensions(
        JsonElement baseNode,
        JsonElement headNode,
        string target,
        ContractChangeCollector collector)
    {
        Dictionary<string, string> baseExt = ReadExtensions(baseNode);
        Dictionary<string, string> headExt = ReadExtensions(headNode);

        foreach (string key in baseExt.Keys.Union(headExt.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            baseExt.TryGetValue(key, out string? baseVal);
            headExt.TryGetValue(key, out string? headVal);
            if (!string.Equals(baseVal, headVal, StringComparison.Ordinal))
            {
                collector.ReviewRequired(
                    ContractChangeKinds.ExtensionChanged,
                    $"{target} {key}",
                    $"Vendor extension '{key}' changed on {target}; manual contract review required.");
            }
        }
    }

    public static void DiffUnhandledFields(
        JsonElement baseNode,
        JsonElement headNode,
        string target,
        HashSet<string> handledKeys,
        ContractChangeCollector collector)
    {
        Dictionary<string, string> baseExtra = ReadUnhandledFields(baseNode, handledKeys);
        Dictionary<string, string> headExtra = ReadUnhandledFields(headNode, handledKeys);

        foreach (string key in baseExtra.Keys.Union(headExtra.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            baseExtra.TryGetValue(key, out string? baseVal);
            headExtra.TryGetValue(key, out string? headVal);
            if (!string.Equals(baseVal, headVal, StringComparison.Ordinal))
            {
                collector.ReviewRequired(
                    ContractChangeKinds.OpenApiReviewRequired,
                    $"{target}.{key}",
                    $"OpenAPI structural field '{key}' changed on {target}; manual contract review required.");
            }
        }
    }

    private static bool TryFollowReferenceChain(JsonElement documentRoot, string initialRef, out JsonElement target)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string currentRef = initialRef;

        for (int hop = 0; hop < MaximumReferenceHops; hop++)
        {
            if (!visited.Add(currentRef) ||
                !JsonElementCanonicalizer.TryResolveLocalRef(documentRoot, currentRef, out JsonElement resolved) ||
                resolved.ValueKind != JsonValueKind.Object)
            {
                target = default;
                return false;
            }

            if (!JsonElementCanonicalizer.TryGetNonEmptyString(resolved, RefKeyword, out string? nextRef))
            {
                target = resolved;
                return true;
            }

            currentRef = nextRef;
        }

        target = default;
        return false;
    }

    private static Dictionary<string, string> ReadExtensions(JsonElement node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty prop in node.EnumerateObject()
            .Where(p => p.Name.StartsWith("x-", StringComparison.OrdinalIgnoreCase)))
        {
            result[prop.Name] = JsonElementCanonicalizer.CanonicalJson(prop.Value);
        }

        return result;
    }

    private static Dictionary<string, string> ReadUnhandledFields(JsonElement node, HashSet<string> handledKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty prop in node.EnumerateObject()
            .Where(p => !handledKeys.Contains(p.Name) && !p.Name.StartsWith("x-", StringComparison.OrdinalIgnoreCase)))
        {
            result[prop.Name] = JsonElementCanonicalizer.CanonicalJson(prop.Value);
        }

        return result;
    }
}
