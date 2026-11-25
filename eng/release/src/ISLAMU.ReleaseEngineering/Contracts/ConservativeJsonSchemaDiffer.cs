using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Immutable traversal context for conservative JSON Schema comparison, replacing long parameter lists.
/// </summary>
internal sealed class JsonSchemaDiffContext(
    ContractChangeCollector collector,
    JsonElement baseRoot,
    JsonElement headRoot)
{
    public ContractChangeCollector Collector { get; } = collector;

    public JsonElement BaseRoot { get; } = baseRoot;

    public JsonElement HeadRoot { get; } = headRoot;

    public HashSet<string> VisitedRefs { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Shared conservative JSON Schema 2020-12 / HAL structural differ used across OpenAPI and
/// Configuration contracts. Unknown constraints and combined <c>$ref</c> siblings fail conservatively
/// to <see cref="ContractChangeSeverity.ReviewRequired"/> without emitting false breaking warnings.
/// </summary>
internal static class ConservativeJsonSchemaDiffer
{
    private const int MaximumDepth = 24;
    private const string RefKeyword = "$ref";
    private const string TypeKeyword = "type";
    private const string RequiredKeyword = "required";
    private const string PropertiesKeyword = "properties";
    private const string ItemsKeyword = "items";
    private const string EnumKeyword = "enum";
    private const string ConstKeyword = "const";

    private static readonly HashSet<string> HandledKeywords = new(StringComparer.Ordinal)
    {
        RefKeyword,
        TypeKeyword,
        RequiredKeyword,
        PropertiesKeyword,
        ItemsKeyword,
        EnumKeyword,
        ConstKeyword,
        "$defs",
        "definitions",
        "title",
        "description",
        "$comment",
        "example",
        "examples",
    };

    private static readonly HashSet<string> RefNonConstraintMetadataKeywords = new(StringComparer.Ordinal)
    {
        RefKeyword,
        "title",
        "description",
        "$comment",
        "example",
        "examples",
    };

    public static void DiffSchema(
        ContractChangeCollector collector,
        string target,
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseSchema,
        JsonElement headSchema)
    {
        var context = new JsonSchemaDiffContext(collector, baseRoot, headRoot);
        DiffNode(context, target, baseSchema, headSchema, depth: 0);
    }

    private static void DiffNode(
        JsonSchemaDiffContext context,
        string target,
        JsonElement baseSchema,
        JsonElement headSchema,
        int depth)
    {
        if (depth > MaximumDepth)
        {
            context.Collector.ReviewRequired(
                ContractChangeKinds.SchemaReviewRequired,
                target,
                $"Schema comparison depth exceeded limit at '{target}'; manual review required.");
            return;
        }

        if (baseSchema.ValueKind != JsonValueKind.Object || headSchema.ValueKind != JsonValueKind.Object)
        {
            if (!JsonElementCanonicalizer.AreCanonicalEqual(baseSchema, headSchema))
            {
                context.Collector.ReviewRequired(
                    ContractChangeKinds.SchemaReviewRequired,
                    target,
                    $"Structural schema node changed at '{target}'; manual review required.");
            }

            return;
        }

        bool baseHasRef = JsonElementCanonicalizer.TryGetNonEmptyString(baseSchema, RefKeyword, out string? baseRef);
        bool headHasRef = JsonElementCanonicalizer.TryGetNonEmptyString(headSchema, RefKeyword, out string? headRef);
        if (baseHasRef || headHasRef)
        {
            DiffReferencedSchemas(context, target, baseSchema, headSchema, baseRef, headRef, depth);
            return;
        }

        DiffTypeSets(context.Collector, target, ReadTypeSet(baseSchema), ReadTypeSet(headSchema));
        DiffConstAndEnum(context.Collector, target, baseSchema, headSchema);
        DiffPropertiesAndRequired(context, target, baseSchema, headSchema, depth);
        DiffItems(context, target, baseSchema, headSchema, depth);
        DiffUnhandledKeywords(context.Collector, target, baseSchema, headSchema);
    }

    private static void DiffReferencedSchemas(
        JsonSchemaDiffContext context,
        string target,
        JsonElement baseSchema,
        JsonElement headSchema,
        string? baseRef,
        string? headRef,
        int depth)
    {
        if (baseRef is not null && headRef is not null && string.Equals(baseRef, headRef, StringComparison.Ordinal))
        {
            bool bothResolved =
                JsonElementCanonicalizer.TryResolveLocalRef(context.BaseRoot, baseRef, out _) &&
                JsonElementCanonicalizer.TryResolveLocalRef(context.HeadRoot, headRef, out _);
            if (!bothResolved)
            {
                context.Collector.ReviewRequired(
                    ContractChangeKinds.UnresolvedRef,
                    target,
                    $"Unresolved or external $ref '{headRef}' at '{target}'; manual review required.");
            }

            DiffRefSiblings(context, target, baseSchema, headSchema, bothResolved);
            return;
        }

        bool baseResolved = TryResolveOrRecordRef(context.BaseRoot, baseRef, baseSchema, "base", target, context.Collector, out JsonElement resolvedBase);
        bool headResolved = TryResolveOrRecordRef(context.HeadRoot, headRef, headSchema, "head", target, context.Collector, out JsonElement resolvedHead);

        if (baseResolved && headResolved)
        {
            string cycleKey = $"{baseRef ?? "<inline>"}->{headRef ?? "<inline>"}";
            if (context.VisitedRefs.Add(cycleKey))
            {
                DiffNode(context, target, resolvedBase, resolvedHead, depth + 1);
                context.VisitedRefs.Remove(cycleKey);
            }
        }

        if ((baseRef is not null) != (headRef is not null))
        {
            JsonElement refNode = baseRef is not null ? baseSchema : headSchema;
            if (HasConstraintSiblingsBesideRef(refNode))
            {
                context.Collector.ReviewRequired(
                    ContractChangeKinds.SchemaReviewRequired,
                    target,
                    $"Sibling schema constraints beside $ref changed across inline/reference refactor at '{target}'; manual review required.");
            }

            return;
        }

        DiffRefSiblings(context, target, baseSchema, headSchema, baseResolved && headResolved);
    }

    private static bool TryResolveOrRecordRef(
        JsonElement root,
        string? reference,
        JsonElement fallbackSchema,
        string sideLabel,
        string target,
        ContractChangeCollector collector,
        out JsonElement resolved)
    {
        if (reference is null)
        {
            resolved = fallbackSchema;
            return true;
        }

        if (JsonElementCanonicalizer.TryResolveLocalRef(root, reference, out resolved))
        {
            return true;
        }

        collector.ReviewRequired(
            ContractChangeKinds.UnresolvedRef,
            target,
            $"Unresolved or external {sideLabel} $ref '{reference}' at '{target}'; manual review required.");
        return false;
    }

    private static void DiffRefSiblings(
        JsonSchemaDiffContext context,
        string target,
        JsonElement baseSchema,
        JsonElement headSchema,
        bool bothTargetsResolved)
    {
        Dictionary<string, string> baseSiblings = ReadRefConstraintSiblings(baseSchema);
        Dictionary<string, string> headSiblings = ReadRefConstraintSiblings(headSchema);

        List<string> changedKeys = FindChangedKeys(baseSiblings, headSiblings);
        if (changedKeys.Count == 0)
        {
            return;
        }

        if (bothTargetsResolved && changedKeys.Contains(TypeKeyword, StringComparer.Ordinal))
        {
            EffectiveTypeResult baseEffective = ComputeEffectiveTypes(context.BaseRoot, baseSchema);
            EffectiveTypeResult headEffective = ComputeEffectiveTypes(context.HeadRoot, headSchema);
            if (!baseEffective.IsContradictory && !headEffective.IsContradictory)
            {
                DiffTypeSets(context.Collector, target, baseEffective.Types, headEffective.Types);
                changedKeys.Remove(TypeKeyword);
            }
        }

        if (changedKeys.Count > 0)
        {
            context.Collector.ReviewRequired(
                ContractChangeKinds.SchemaReviewRequired,
                target,
                $"Sibling schema constraints beside $ref changed at '{target}' ({string.Join(", ", changedKeys)}); manual review required.");
        }
    }

    private static void DiffItems(
        JsonSchemaDiffContext context,
        string target,
        JsonElement baseSchema,
        JsonElement headSchema,
        int depth)
    {
        bool baseHasItems = baseSchema.TryGetProperty(ItemsKeyword, out JsonElement baseItems);
        bool headHasItems = headSchema.TryGetProperty(ItemsKeyword, out JsonElement headItems);
        if (baseHasItems && headHasItems)
        {
            DiffNode(context, $"{target}[]", baseItems, headItems, depth + 1);
        }
        else if (baseHasItems != headHasItems)
        {
            context.Collector.ReviewRequired(
                ContractChangeKinds.SchemaReviewRequired,
                $"{target}[]",
                $"Array item schema presence changed at '{target}'; manual review required.");
        }
    }

    private static void DiffTypeSets(
        ContractChangeCollector collector,
        string target,
        HashSet<string> baseTypes,
        HashSet<string> headTypes)
    {
        if (baseTypes.SetEquals(headTypes))
        {
            return;
        }

        // In JSON Schema 2020-12, omitting "type" (empty set) means unconstrained (any JSON type is allowed).
        if (baseTypes.Count == 0)
        {
            string[] restricted = headTypes.Order(StringComparer.Ordinal).ToArray();
            collector.Breaking(
                ContractChangeKinds.SchemaTypeNarrowed,
                target,
                $"Schema type narrowed at '{target}' from unconstrained to ({string.Join(", ", restricted)}).");
            return;
        }

        if (headTypes.Count == 0)
        {
            collector.ReviewRequired(
                ContractChangeKinds.SchemaTypeChanged,
                target,
                $"Schema type constraint removed at '{target}' (now unconstrained); verify consumer compatibility.");
            return;
        }

        string[] removed = baseTypes.Except(headTypes, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (removed.Length > 0)
        {
            collector.Breaking(
                ContractChangeKinds.SchemaTypeNarrowed,
                target,
                $"Schema type narrowed at '{target}' (removed: {string.Join(", ", removed)}).");
            return;
        }

        string[] added = headTypes.Except(baseTypes, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        collector.ReviewRequired(
            ContractChangeKinds.SchemaTypeChanged,
            target,
            $"Schema type widened at '{target}' (added: {string.Join(", ", added)}); verify consumer compatibility.");
    }

    private static void DiffConstAndEnum(
        ContractChangeCollector collector,
        string target,
        JsonElement baseSchema,
        JsonElement headSchema)
    {
        bool baseHasConst = baseSchema.TryGetProperty(ConstKeyword, out JsonElement baseConst);
        bool headHasConst = headSchema.TryGetProperty(ConstKeyword, out JsonElement headConst);
        if (baseHasConst || headHasConst)
        {
            string baseText = baseHasConst ? JsonElementCanonicalizer.CanonicalJson(baseConst) : "<none>";
            string headText = headHasConst ? JsonElementCanonicalizer.CanonicalJson(headConst) : "<none>";
            if (!string.Equals(baseText, headText, StringComparison.Ordinal))
            {
                collector.Breaking(ContractChangeKinds.SchemaConstChanged, target, $"Constant contract value changed at '{target}'.");
            }
        }

        HashSet<string> baseEnum = ReadEnumSet(baseSchema);
        HashSet<string> headEnum = ReadEnumSet(headSchema);
        if (baseEnum.SetEquals(headEnum))
        {
            return;
        }

        if (baseEnum.Except(headEnum, StringComparer.Ordinal).Any())
        {
            collector.Breaking(ContractChangeKinds.SchemaEnumNarrowed, target, $"Removed one or more enum values at '{target}'.");
        }
        else if (headEnum.Except(baseEnum, StringComparer.Ordinal).Any())
        {
            collector.ReviewRequired(ContractChangeKinds.SchemaEnumExpanded, target, $"Added enum values at '{target}'; verify closed consumer switches.");
        }
    }

    private static void DiffPropertiesAndRequired(
        JsonSchemaDiffContext context,
        string target,
        JsonElement baseSchema,
        JsonElement headSchema,
        int depth)
    {
        HashSet<string> baseRequired = JsonElementCanonicalizer.ReadStringArray(baseSchema, RequiredKeyword);
        HashSet<string> headRequired = JsonElementCanonicalizer.ReadStringArray(headSchema, RequiredKeyword);
        Dictionary<string, JsonElement> baseProps = JsonElementCanonicalizer.ReadObjectMap(baseSchema, PropertiesKeyword);
        Dictionary<string, JsonElement> headProps = JsonElementCanonicalizer.ReadObjectMap(headSchema, PropertiesKeyword);

        bool isHalLinks = target.EndsWith("._links", StringComparison.Ordinal) ||
            string.Equals(target, "_links", StringComparison.Ordinal);

        foreach (string propName in baseProps.Keys.Union(headProps.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            DiffSingleProperty(
                context,
                target,
                propName,
                baseProps,
                headProps,
                baseRequired,
                headRequired,
                isHalLinks,
                depth);
        }

        foreach (string newlyRequired in headRequired.Except(baseRequired, StringComparer.Ordinal)
            .Where(name => !headProps.ContainsKey(name) && !baseProps.ContainsKey(name))
            .Order(StringComparer.Ordinal))
        {
            context.Collector.Breaking(
                ContractChangeKinds.RequiredPropertyAdded,
                $"{target}.{newlyRequired}",
                $"Added required field '{newlyRequired}' to '{target}'.");
        }
    }

    private static void DiffSingleProperty(
        JsonSchemaDiffContext context,
        string parentTarget,
        string propName,
        Dictionary<string, JsonElement> baseProps,
        Dictionary<string, JsonElement> headProps,
        HashSet<string> baseRequired,
        HashSet<string> headRequired,
        bool isHalLinks,
        int depth)
    {
        bool hasBase = baseProps.TryGetValue(propName, out JsonElement baseProp);
        bool hasHead = headProps.TryGetValue(propName, out JsonElement headProp);
        string propTarget = $"{parentTarget}.{propName}";

        if (hasBase && !hasHead)
        {
            RecordRemovedProperty(context.Collector, parentTarget, propName, propTarget, isHalLinks);
            return;
        }

        if (!hasBase && hasHead)
        {
            RecordAddedProperty(context.Collector, parentTarget, propName, propTarget, isHalLinks, headRequired.Contains(propName));
            return;
        }

        DiffPropertyRequiredTransition(context.Collector, parentTarget, propName, propTarget, baseRequired.Contains(propName), headRequired.Contains(propName));
        DiffNode(context, propTarget, baseProp, headProp, depth + 1);
    }

    private static void RecordRemovedProperty(
        ContractChangeCollector collector,
        string parentTarget,
        string propName,
        string propTarget,
        bool isHalLinks)
    {
        string kind = isHalLinks ? ContractChangeKinds.HalLinkRemoved : ContractChangeKinds.PropertyRemoved;
        string summary = isHalLinks
            ? $"Removed HAL link relation '{propName}' from '{parentTarget}'."
            : $"Removed property '{propName}' from '{parentTarget}'.";
        collector.Breaking(kind, propTarget, summary);
    }

    private static void RecordAddedProperty(
        ContractChangeCollector collector,
        string parentTarget,
        string propName,
        string propTarget,
        bool isHalLinks,
        bool isRequired)
    {
        if (isHalLinks)
        {
            string severity = isRequired ? ContractChangeSeverity.Breaking : ContractChangeSeverity.Additive;
            collector.Add(severity, ContractChangeKinds.HalLinkAdded, propTarget, $"Added HAL link relation '{propName}' to '{parentTarget}'.");
            return;
        }

        if (isRequired)
        {
            collector.Breaking(ContractChangeKinds.RequiredPropertyAdded, propTarget, $"Added required property '{propName}' to '{parentTarget}'.");
        }
        else
        {
            collector.Additive(ContractChangeKinds.OptionalPropertyAdded, propTarget, $"Added optional property '{propName}' to '{parentTarget}'.");
        }
    }

    private static void DiffPropertyRequiredTransition(
        ContractChangeCollector collector,
        string parentTarget,
        string propName,
        string propTarget,
        bool wasRequired,
        bool isRequired)
    {
        if (!wasRequired && isRequired)
        {
            collector.Breaking(ContractChangeKinds.RequiredPropertyAdded, propTarget, $"Property '{propName}' on '{parentTarget}' became required.");
        }
        else if (wasRequired && !isRequired)
        {
            collector.ReviewRequired(ContractChangeKinds.RequiredPropertyRemoved, propTarget, $"Property '{propName}' on '{parentTarget}' is no longer marked required; verify consumer assumptions.");
        }
    }

    private static void DiffUnhandledKeywords(
        ContractChangeCollector collector,
        string target,
        JsonElement baseSchema,
        JsonElement headSchema)
    {
        Dictionary<string, string> baseExtra = ReadUnhandledKeywords(baseSchema);
        Dictionary<string, string> headExtra = ReadUnhandledKeywords(headSchema);
        List<string> changedKeys = FindChangedKeys(baseExtra, headExtra);

        if (changedKeys.Count > 0)
        {
            collector.ReviewRequired(
                ContractChangeKinds.SchemaReviewRequired,
                target,
                $"Structural schema constraints or defaults changed at '{target}' ({string.Join(", ", changedKeys)}); manual review required.");
        }
    }

    private static Dictionary<string, string> ReadUnhandledKeywords(JsonElement schema)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty prop in schema.EnumerateObject().Where(p => !HandledKeywords.Contains(p.Name)))
        {
            result[prop.Name] = JsonElementCanonicalizer.CanonicalJson(prop.Value);
        }

        return result;
    }

    private static Dictionary<string, string> ReadRefConstraintSiblings(JsonElement schema)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty prop in schema.EnumerateObject().Where(p => !RefNonConstraintMetadataKeywords.Contains(p.Name)))
        {
            result[prop.Name] = JsonElementCanonicalizer.CanonicalJson(prop.Value);
        }

        return result;
    }

    private static bool HasConstraintSiblingsBesideRef(JsonElement schema) =>
        schema.EnumerateObject().Any(prop => !RefNonConstraintMetadataKeywords.Contains(prop.Name));

    private static List<string> FindChangedKeys(Dictionary<string, string> baseMap, Dictionary<string, string> headMap) =>
        baseMap.Keys.Union(headMap.Keys, StringComparer.Ordinal)
            .Where(key =>
            {
                baseMap.TryGetValue(key, out string? baseVal);
                headMap.TryGetValue(key, out string? headVal);
                return !string.Equals(baseVal, headVal, StringComparison.Ordinal);
            })
            .Order(StringComparer.Ordinal)
            .ToList();

    private readonly record struct EffectiveTypeResult(HashSet<string> Types, bool IsContradictory);

    private static EffectiveTypeResult ComputeEffectiveTypes(JsonElement root, JsonElement schema)
    {
        HashSet<string>? effective = null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        JsonElement current = schema;

        for (int depth = 0; depth <= MaximumDepth && current.ValueKind == JsonValueKind.Object; depth++)
        {
            if (!TryIntersectLocalTypes(current, ref effective))
            {
                return new EffectiveTypeResult(effective!, IsContradictory: true);
            }

            if (!JsonElementCanonicalizer.TryGetNonEmptyString(current, RefKeyword, out string? reference) ||
                !visited.Add(reference) ||
                !JsonElementCanonicalizer.TryResolveLocalRef(root, reference, out JsonElement next))
            {
                break;
            }

            current = next;
        }

        return new EffectiveTypeResult(effective ?? new HashSet<string>(StringComparer.Ordinal), IsContradictory: false);
    }

    private static bool TryIntersectLocalTypes(JsonElement node, ref HashSet<string>? effective)
    {
        HashSet<string> localTypes = ReadTypeSet(node);
        if (localTypes.Count == 0)
        {
            return true;
        }

        if (effective is null)
        {
            effective = new HashSet<string>(localTypes, StringComparer.Ordinal);
            return true;
        }

        effective.IntersectWith(localTypes);
        return effective.Count > 0;
    }

    private static HashSet<string> ReadTypeSet(JsonElement schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!schema.TryGetProperty(TypeKeyword, out JsonElement typeProp))
        {
            return set;
        }

        if (typeProp.ValueKind == JsonValueKind.String && typeProp.GetString() is { Length: > 0 } single)
        {
            set.Add(single);
        }
        else if (typeProp.ValueKind == JsonValueKind.Array)
        {
            foreach (string value in typeProp.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(text => text is { Length: > 0 })!)
            {
                set.Add(value);
            }
        }

        return set;
    }

    private static HashSet<string> ReadEnumSet(JsonElement schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty(EnumKeyword, out JsonElement enumProp) && enumProp.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in enumProp.EnumerateArray())
            {
                set.Add(JsonElementCanonicalizer.CanonicalJson(item));
            }
        }

        return set;
    }
}
