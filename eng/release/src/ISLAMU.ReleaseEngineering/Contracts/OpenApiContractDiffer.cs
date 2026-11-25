using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Conservative structural differ for OpenAPI 3.x / JSON Schema 2020-12 documents including
/// operation routing, inherited and explicit security policies, referenced parameters/bodies/responses,
/// components, and HAL <c>_links</c>.
/// </summary>
public sealed class OpenApiContractDiffer : IReleaseContractDiffer
{
    private const string SchemasDirectory = "schemas";
    private const string ComponentsKeyword = "components";
    private const string SchemasKeyword = "schemas";
    private const string PathsKeyword = "paths";
    private const string ParametersKeyword = "parameters";
    private const string RequestBodyKeyword = "requestBody";
    private const string ResponsesKeyword = "responses";
    private const string ContentKeyword = "content";
    private const string SchemaKeyword = "schema";
    private const string RequiredKeyword = "required";
    private const string SummaryKeyword = "summary";
    private const string DescriptionKeyword = "description";
    private const string ExampleKeyword = "example";
    private const string ExamplesKeyword = "examples";
    private const string SecurityKeyword = "security";
    private const string DefsKeyword = "$defs";

    private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete", "head", "options", "trace"];
    private static readonly HashSet<string> DocumentHandledKeys = new(StringComparer.Ordinal)
    {
        PathsKeyword, ComponentsKeyword, DefsKeyword, SecurityKeyword, "info", "tags", "externalDocs",
    };
    private static readonly HashSet<string> PathItemHandledKeys = new(
        HttpMethods.Concat([ParametersKeyword, SummaryKeyword, DescriptionKeyword]),
        StringComparer.Ordinal);
    private static readonly HashSet<string> OperationHandledKeys = new(StringComparer.Ordinal)
    {
        SecurityKeyword, ParametersKeyword, RequestBodyKeyword, ResponsesKeyword,
        "tags", SummaryKeyword, DescriptionKeyword, "externalDocs", "operationId",
    };
    private static readonly HashSet<string> ParameterHandledKeys = new(StringComparer.Ordinal)
    {
        "name", "in", RequiredKeyword, SchemaKeyword, DescriptionKeyword, ExampleKeyword, ExamplesKeyword,
    };
    private static readonly HashSet<string> RequestBodyHandledKeys = new(StringComparer.Ordinal)
    {
        RequiredKeyword, ContentKeyword, DescriptionKeyword,
    };
    private static readonly HashSet<string> ResponseHandledKeys = new(StringComparer.Ordinal)
    {
        ContentKeyword, DescriptionKeyword, SummaryKeyword,
    };
    private static readonly HashSet<string> MediaTypeHandledKeys = new(StringComparer.Ordinal)
    {
        SchemaKeyword, ExampleKeyword, ExamplesKeyword,
    };

    private sealed record DocumentContext(
        JsonElement BaseRoot,
        JsonElement HeadRoot,
        ContractChangeCollector Collector);

    public string Family => ContractFamilies.OpenApi;

    public int Order => 10;

    public ContractDifferResult Diff(IGitObjectReader reader, string baseOid, string headOid)
    {
        ArgumentNullException.ThrowIfNull(reader);

        GitTreeListResult baseTree = reader.ListFiles(baseOid, SchemasDirectory);
        if (!baseTree.IsValid)
        {
            return Failure(baseTree.Diagnostic);
        }

        GitTreeListResult headTree = reader.ListFiles(headOid, SchemasDirectory);
        if (!headTree.IsValid)
        {
            return Failure(headTree.Diagnostic);
        }

        HashSet<string> basePaths = baseTree.Paths.Where(IsOpenApiJsonPath).ToHashSet(StringComparer.Ordinal);
        HashSet<string> headPaths = headTree.Paths.Where(IsOpenApiJsonPath).ToHashSet(StringComparer.Ordinal);

        var changes = new List<ContractDiffChange>();
        foreach (string path in basePaths.Union(headPaths, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            string? diagnostic = DiffFile(reader, baseOid, headOid, path, basePaths.Contains(path), headPaths.Contains(path), changes);
            if (diagnostic is not null)
            {
                return Failure(diagnostic);
            }
        }

        return new ContractDifferResult(true, Family, changes);
    }

    private string? DiffFile(
        IGitObjectReader reader,
        string baseOid,
        string headOid,
        string path,
        bool inBase,
        bool inHead,
        List<ContractDiffChange> changes)
    {
        var collector = new ContractChangeCollector(Family, path, changes);
        if (inBase && !inHead)
        {
            collector.Breaking(ContractChangeKinds.ContractFileRemoved, path, $"Removed OpenAPI contract file '{path}'.");
            return null;
        }

        if (!inBase && inHead)
        {
            return ValidateAddedFile(reader, headOid, path, collector);
        }

        return DiffModifiedFile(reader, baseOid, headOid, path, collector);
    }

    private static string? ValidateAddedFile(IGitObjectReader reader, string headOid, string path, ContractChangeCollector collector)
    {
        GitBlobReadResult addedBlob = reader.ReadBlob(headOid, path);
        if (addedBlob.Diagnostic is not null || !addedBlob.Found || addedBlob.Bytes is null)
        {
            return addedBlob.Diagnostic ?? "contract_diff_openapi_read_failed";
        }

        if (!JsonElementCanonicalizer.TryParseObject(addedBlob.Bytes, out JsonDocument? addedDoc))
        {
            return "contract_diff_openapi_malformed";
        }

        addedDoc.Dispose();
        collector.ReviewRequired(
            ContractChangeKinds.ContractFileAdded,
            path,
            $"Added new OpenAPI contract file '{path}'; review initial public contract surface.");
        return null;
    }

    private static string? DiffModifiedFile(
        IGitObjectReader reader,
        string baseOid,
        string headOid,
        string path,
        ContractChangeCollector collector)
    {
        GitBlobReadResult baseBlob = reader.ReadBlob(baseOid, path);
        GitBlobReadResult headBlob = reader.ReadBlob(headOid, path);
        if (baseBlob.Diagnostic is not null || headBlob.Diagnostic is not null ||
            !baseBlob.Found || !headBlob.Found || baseBlob.Bytes is null || headBlob.Bytes is null)
        {
            return baseBlob.Diagnostic ?? headBlob.Diagnostic ?? "contract_diff_openapi_read_failed";
        }

        if (!JsonElementCanonicalizer.TryParseObject(baseBlob.Bytes, out JsonDocument? baseDoc))
        {
            return "contract_diff_openapi_malformed";
        }

        using (baseDoc)
        {
            if (!JsonElementCanonicalizer.TryParseObject(headBlob.Bytes, out JsonDocument? headDoc))
            {
                return "contract_diff_openapi_malformed";
            }

            using (headDoc)
            {
                if (!baseBlob.Bytes.AsSpan().SequenceEqual(headBlob.Bytes))
                {
                    DiffDocument(new DocumentContext(baseDoc.RootElement, headDoc.RootElement, collector));
                }
            }
        }

        return null;
    }

    private static void DiffDocument(DocumentContext context)
    {
        OpenApiReferenceResolver.DiffExtensions(context.BaseRoot, context.HeadRoot, "document", context.Collector);
        OpenApiSecurityPolicy.Diff(
            OpenApiSecurityPolicy.FromOperation(context.BaseRoot),
            OpenApiSecurityPolicy.FromOperation(context.HeadRoot),
            "document",
            context.Collector);
        OpenApiReferenceResolver.DiffUnhandledFields(context.BaseRoot, context.HeadRoot, "document", DocumentHandledKeys, context.Collector);
        DiffPaths(context);
        DiffComponents(context);
        DiffNamedSchemas(context, DefsKeyword, null, DefsKeyword);
    }

    private static void DiffComponents(DocumentContext context)
    {
        DiffNamedSchemas(context, ComponentsKeyword, SchemasKeyword, $"{ComponentsKeyword}.{SchemasKeyword}");

        bool hasBase = context.BaseRoot.TryGetProperty(ComponentsKeyword, out JsonElement baseComponents) && baseComponents.ValueKind == JsonValueKind.Object;
        bool hasHead = context.HeadRoot.TryGetProperty(ComponentsKeyword, out JsonElement headComponents) && headComponents.ValueKind == JsonValueKind.Object;
        if (!hasBase && !hasHead)
        {
            return;
        }

        OpenApiReferenceResolver.DiffExtensions(
            hasBase ? baseComponents : default,
            hasHead ? headComponents : default,
            ComponentsKeyword,
            context.Collector);

        Dictionary<string, JsonElement> baseSections = JsonElementCanonicalizer.ReadObjectMap(context.BaseRoot, ComponentsKeyword);
        Dictionary<string, JsonElement> headSections = JsonElementCanonicalizer.ReadObjectMap(context.HeadRoot, ComponentsKeyword);

        foreach (string section in baseSections.Keys.Union(headSections.Keys, StringComparer.Ordinal)
            .Where(name => !string.Equals(name, SchemasKeyword, StringComparison.Ordinal) &&
                !name.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            DiffComponentSection(section, baseSections, headSections, context.Collector);
        }
    }

    private static void DiffComponentSection(
        string section,
        Dictionary<string, JsonElement> baseSections,
        Dictionary<string, JsonElement> headSections,
        ContractChangeCollector collector)
    {
        bool hasBase = baseSections.TryGetValue(section, out JsonElement baseSection);
        bool hasHead = headSections.TryGetValue(section, out JsonElement headSection);

        if ((hasBase && baseSection.ValueKind == JsonValueKind.Object) ||
            (hasHead && headSection.ValueKind == JsonValueKind.Object))
        {
            Dictionary<string, JsonElement> baseEntries = hasBase && baseSection.ValueKind == JsonValueKind.Object
                ? JsonElementCanonicalizer.EnumerateObjectMap(baseSection)
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            Dictionary<string, JsonElement> headEntries = hasHead && headSection.ValueKind == JsonValueKind.Object
                ? JsonElementCanonicalizer.EnumerateObjectMap(headSection)
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            foreach (string entryName in baseEntries.Keys.Union(headEntries.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                bool entryInBase = baseEntries.TryGetValue(entryName, out JsonElement baseEntry);
                bool entryInHead = headEntries.TryGetValue(entryName, out JsonElement headEntry);
                if (!entryInBase || !entryInHead || !JsonElementCanonicalizer.AreCanonicalEqual(baseEntry, headEntry))
                {
                    string target = $"{ComponentsKeyword}.{section}.{entryName}";
                    collector.ReviewRequired(
                        ContractChangeKinds.OpenApiComponentChanged,
                        target,
                        $"OpenAPI component '{target}' changed; manual contract review required.");
                }
            }

            return;
        }

        if (!hasBase || !hasHead || !JsonElementCanonicalizer.AreCanonicalEqual(baseSection, headSection))
        {
            string target = $"{ComponentsKeyword}.{section}";
            collector.ReviewRequired(
                ContractChangeKinds.OpenApiComponentChanged,
                target,
                $"OpenAPI component section '{target}' changed; manual contract review required.");
        }
    }

    private static void DiffPaths(DocumentContext context)
    {
        Dictionary<string, JsonElement> basePaths = JsonElementCanonicalizer.ReadObjectMap(context.BaseRoot, PathsKeyword);
        Dictionary<string, JsonElement> headPaths = JsonElementCanonicalizer.ReadObjectMap(context.HeadRoot, PathsKeyword);

        foreach (string route in basePaths.Keys.Union(headPaths.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            DiffRoute(context, route, basePaths, headPaths);
        }
    }

    private static void DiffRoute(
        DocumentContext context,
        string route,
        Dictionary<string, JsonElement> basePaths,
        Dictionary<string, JsonElement> headPaths)
    {
        bool hasBase = basePaths.TryGetValue(route, out JsonElement basePathItem) && basePathItem.ValueKind == JsonValueKind.Object;
        bool hasHead = headPaths.TryGetValue(route, out JsonElement headPathItem) && headPathItem.ValueKind == JsonValueKind.Object;

        if (hasBase && !hasHead)
        {
            RecordWholeRouteChange(route, basePathItem, added: false, context.Collector);
            return;
        }

        if (!hasBase && hasHead)
        {
            RecordWholeRouteChange(route, headPathItem, added: true, context.Collector);
            return;
        }

        if (!hasBase)
        {
            return;
        }

        string pathTarget = $"path:{route}";
        OpenApiReferenceResolver.DiffExtensions(basePathItem, headPathItem, pathTarget, context.Collector);
        DiffParameters(context, basePathItem, headPathItem, pathTarget);
        OpenApiReferenceResolver.DiffUnhandledFields(basePathItem, headPathItem, pathTarget, PathItemHandledKeys, context.Collector);

        foreach (string method in HttpMethods)
        {
            DiffRouteMethod(context, route, method, basePathItem, headPathItem);
        }
    }

    private static void RecordWholeRouteChange(string route, JsonElement pathItem, bool added, ContractChangeCollector collector)
    {
        bool emitted = false;
        foreach (string method in HttpMethods.Where(m => pathItem.TryGetProperty(m, out _)))
        {
            emitted = true;
            string label = $"{method.ToUpperInvariant()} {route}";
            if (added)
            {
                collector.Additive(ContractChangeKinds.OperationAdded, label, $"Added API operation {label}.");
            }
            else
            {
                collector.Breaking(ContractChangeKinds.OperationRemoved, label, $"Removed API operation {label}.");
            }
        }

        if (!emitted)
        {
            string verb = added ? "Added" : "Removed";
            collector.ReviewRequired(
                ContractChangeKinds.OpenApiReviewRequired,
                $"path:{route}",
                $"{verb} path item '{route}' with non-standard or referenced structure; manual review required.");
        }
    }

    private static void DiffRouteMethod(
        DocumentContext context,
        string route,
        string method,
        JsonElement basePathItem,
        JsonElement headPathItem)
    {
        bool hasBaseOp = basePathItem.TryGetProperty(method, out JsonElement baseOp) && baseOp.ValueKind == JsonValueKind.Object;
        bool hasHeadOp = headPathItem.TryGetProperty(method, out JsonElement headOp) && headOp.ValueKind == JsonValueKind.Object;
        string opLabel = $"{method.ToUpperInvariant()} {route}";

        if (hasBaseOp && !hasHeadOp)
        {
            context.Collector.Breaking(ContractChangeKinds.OperationRemoved, opLabel, $"Removed API operation {opLabel}.");
        }
        else if (!hasBaseOp && hasHeadOp)
        {
            context.Collector.Additive(ContractChangeKinds.OperationAdded, opLabel, $"Added API operation {opLabel}.");
        }
        else if (hasBaseOp)
        {
            DiffOperation(context, baseOp, headOp, opLabel);
        }
    }

    private static void DiffOperation(DocumentContext context, JsonElement baseOp, JsonElement headOp, string opLabel)
    {
        OpenApiReferenceResolver.DiffExtensions(baseOp, headOp, opLabel, context.Collector);
        OpenApiSecurityPolicy.Diff(
            OpenApiSecurityPolicy.FromOperation(baseOp, context.BaseRoot),
            OpenApiSecurityPolicy.FromOperation(headOp, context.HeadRoot),
            opLabel,
            context.Collector);
        DiffParameters(context, baseOp, headOp, opLabel);
        DiffRequestBody(context, baseOp, headOp, opLabel);
        DiffResponses(context, baseOp, headOp, opLabel);
        OpenApiReferenceResolver.DiffUnhandledFields(baseOp, headOp, opLabel, OperationHandledKeys, context.Collector);
    }

    private static void DiffParameters(DocumentContext context, JsonElement baseContainer, JsonElement headContainer, string containerLabel)
    {
        Dictionary<string, ResolvedOpenApiObject> baseParams = ReadParameterMap(context.BaseRoot, baseContainer);
        Dictionary<string, ResolvedOpenApiObject> headParams = ReadParameterMap(context.HeadRoot, headContainer);

        foreach (string key in baseParams.Keys.Union(headParams.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            DiffSingleParameter(context, containerLabel, key, baseParams, headParams);
        }
    }

    private static void DiffSingleParameter(
        DocumentContext context,
        string containerLabel,
        string key,
        Dictionary<string, ResolvedOpenApiObject> baseParams,
        Dictionary<string, ResolvedOpenApiObject> headParams)
    {
        bool hasBase = baseParams.TryGetValue(key, out ResolvedOpenApiObject baseNode);
        bool hasHead = headParams.TryGetValue(key, out ResolvedOpenApiObject headNode);
        string target = $"{containerLabel} param:{key}";

        if (hasBase && !hasHead)
        {
            RecordRemovedParameter(baseNode, key, target, containerLabel, context.Collector);
            return;
        }

        if (!hasBase && hasHead)
        {
            RecordAddedParameter(headNode, key, target, containerLabel, context.Collector);
            return;
        }

        if (baseNode.IsUnresolvedReference || headNode.IsUnresolvedReference)
        {
            context.Collector.ReviewRequired(
                ContractChangeKinds.UnresolvedRef,
                target,
                $"Unresolved referenced parameter '{headNode.Reference ?? baseNode.Reference}' on {containerLabel}; manual review required.");
            return;
        }

        DiffMatchedParameter(context, baseNode, headNode, key, target, containerLabel);
    }

    private static void RecordRemovedParameter(
        ResolvedOpenApiObject baseNode,
        string key,
        string target,
        string containerLabel,
        ContractChangeCollector collector)
    {
        if (baseNode.IsUnresolvedReference)
        {
            collector.ReviewRequired(
                ContractChangeKinds.UnresolvedRef,
                target,
                $"Removed unresolved referenced parameter '{baseNode.Reference}' from {containerLabel}; manual review required.");
        }
        else
        {
            collector.Breaking(ContractChangeKinds.ParameterRemoved, target, $"Removed parameter '{key}' from {containerLabel}.");
        }
    }

    private static void RecordAddedParameter(
        ResolvedOpenApiObject headNode,
        string key,
        string target,
        string containerLabel,
        ContractChangeCollector collector)
    {
        if (headNode.IsUnresolvedReference)
        {
            collector.ReviewRequired(
                ContractChangeKinds.UnresolvedRef,
                target,
                $"Added unresolved referenced parameter '{headNode.Reference}' to {containerLabel}; manual review required.");
            return;
        }

        bool required = IsParameterRequired(headNode.Target);
        if (!required && headNode.HasNonProseSiblings)
        {
            collector.ReviewRequired(
                ContractChangeKinds.OpenApiReviewRequired,
                target,
                $"Added referenced parameter '{key}' with sibling overrides to {containerLabel}; manual review required.");
            return;
        }

        if (required)
        {
            collector.Breaking(ContractChangeKinds.RequiredParameterAdded, target, $"Added required parameter '{key}' to {containerLabel}.");
        }
        else
        {
            collector.Additive(ContractChangeKinds.OptionalParameterAdded, target, $"Added optional parameter '{key}' to {containerLabel}.");
        }
    }

    private static void DiffMatchedParameter(
        DocumentContext context,
        ResolvedOpenApiObject baseNode,
        ResolvedOpenApiObject headNode,
        string key,
        string target,
        string containerLabel)
    {
        bool baseRequired = IsParameterRequired(baseNode.Target);
        bool headRequired = IsParameterRequired(headNode.Target);
        if (!baseRequired && headRequired)
        {
            context.Collector.Breaking(ContractChangeKinds.ParameterNarrowedRequired, target, $"Parameter '{key}' on {containerLabel} changed from optional to required.");
        }
        else if (baseRequired && !headRequired)
        {
            context.Collector.Additive(ContractChangeKinds.ParameterWidenedOptional, target, $"Parameter '{key}' on {containerLabel} changed from required to optional.");
        }

        OpenApiReferenceResolver.DiffRawReferenceFields(baseNode, headNode, target, context.Collector);
        OpenApiReferenceResolver.DiffExtensions(baseNode.Target, headNode.Target, target, context.Collector);
        DiffOptionalChildSchema(context, baseNode.Target, headNode.Target, target, $"parameter '{key}' of {containerLabel}");
        OpenApiReferenceResolver.DiffUnhandledFields(baseNode.Target, headNode.Target, target, ParameterHandledKeys, context.Collector);
    }

    private static void DiffRequestBody(DocumentContext context, JsonElement baseOp, JsonElement headOp, string opLabel)
    {
        bool hasBase = baseOp.TryGetProperty(RequestBodyKeyword, out JsonElement rawBase) && rawBase.ValueKind == JsonValueKind.Object;
        bool hasHead = headOp.TryGetProperty(RequestBodyKeyword, out JsonElement rawHead) && rawHead.ValueKind == JsonValueKind.Object;
        string target = $"{opLabel} {RequestBodyKeyword}";

        if (hasBase && !hasHead)
        {
            context.Collector.Breaking(ContractChangeKinds.RequestBodyRemoved, target, $"Removed request body from {opLabel}.");
            return;
        }

        if (!hasBase && hasHead)
        {
            RecordAddedRequestBody(OpenApiReferenceResolver.Resolve(context.HeadRoot, rawHead), target, opLabel, context.Collector);
            return;
        }

        if (!hasBase)
        {
            return;
        }

        ResolvedOpenApiObject baseNode = OpenApiReferenceResolver.Resolve(context.BaseRoot, rawBase);
        ResolvedOpenApiObject headNode = OpenApiReferenceResolver.Resolve(context.HeadRoot, rawHead);
        if (baseNode.IsUnresolvedReference || headNode.IsUnresolvedReference)
        {
            context.Collector.ReviewRequired(
                ContractChangeKinds.UnresolvedRef,
                target,
                $"Unresolved referenced request body '{headNode.Reference ?? baseNode.Reference}' on {opLabel}; manual review required.");
            return;
        }

        DiffMatchedRequestBody(context, baseNode, headNode, target, opLabel);
    }

    private static void RecordAddedRequestBody(
        ResolvedOpenApiObject headNode,
        string target,
        string opLabel,
        ContractChangeCollector collector)
    {
        if (headNode.IsUnresolvedReference)
        {
            collector.ReviewRequired(
                ContractChangeKinds.UnresolvedRef,
                target,
                $"Added unresolved referenced request body '{headNode.Reference}' to {opLabel}; manual review required.");
            return;
        }

        bool required = IsRequiredFlag(headNode.Target);
        if (!required && headNode.HasNonProseSiblings)
        {
            collector.ReviewRequired(
                ContractChangeKinds.OpenApiReviewRequired,
                target,
                $"Added referenced request body with sibling overrides to {opLabel}; manual review required.");
            return;
        }

        if (required)
        {
            collector.Breaking(ContractChangeKinds.RequiredRequestBodyAdded, target, $"Added required request body to {opLabel}.");
        }
        else
        {
            collector.Additive(ContractChangeKinds.OptionalRequestBodyAdded, target, $"Added optional request body to {opLabel}.");
        }
    }

    private static void DiffMatchedRequestBody(
        DocumentContext context,
        ResolvedOpenApiObject baseNode,
        ResolvedOpenApiObject headNode,
        string target,
        string opLabel)
    {
        bool baseRequired = IsRequiredFlag(baseNode.Target);
        bool headRequired = IsRequiredFlag(headNode.Target);
        if (!baseRequired && headRequired)
        {
            context.Collector.Breaking(ContractChangeKinds.RequestBodyNarrowedRequired, target, $"Request body on {opLabel} became required.");
        }
        else if (baseRequired && !headRequired)
        {
            context.Collector.Additive(ContractChangeKinds.RequestBodyWidenedOptional, target, $"Request body on {opLabel} changed from required to optional.");
        }

        OpenApiReferenceResolver.DiffRawReferenceFields(baseNode, headNode, target, context.Collector);
        OpenApiReferenceResolver.DiffExtensions(baseNode.Target, headNode.Target, target, context.Collector);
        DiffContentMediaTypes(context, baseNode.Target, headNode.Target, target);
        OpenApiReferenceResolver.DiffUnhandledFields(baseNode.Target, headNode.Target, target, RequestBodyHandledKeys, context.Collector);
    }

    private static void DiffResponses(DocumentContext context, JsonElement baseOp, JsonElement headOp, string opLabel)
    {
        Dictionary<string, JsonElement> baseResponses = JsonElementCanonicalizer.ReadObjectMap(baseOp, ResponsesKeyword);
        Dictionary<string, JsonElement> headResponses = JsonElementCanonicalizer.ReadObjectMap(headOp, ResponsesKeyword);

        foreach (string status in baseResponses.Keys.Union(headResponses.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            DiffSingleResponse(context, opLabel, status, baseResponses, headResponses);
        }
    }

    private static void DiffSingleResponse(
        DocumentContext context,
        string opLabel,
        string status,
        Dictionary<string, JsonElement> baseResponses,
        Dictionary<string, JsonElement> headResponses)
    {
        bool hasBase = baseResponses.TryGetValue(status, out JsonElement rawBase) && rawBase.ValueKind == JsonValueKind.Object;
        bool hasHead = headResponses.TryGetValue(status, out JsonElement rawHead) && rawHead.ValueKind == JsonValueKind.Object;
        string target = $"{opLabel} response:{status}";

        if (hasBase && !hasHead)
        {
            context.Collector.Breaking(ContractChangeKinds.ResponseRemoved, target, $"Removed HTTP {status} response from {opLabel}.");
            return;
        }

        if (!hasBase && hasHead)
        {
            ResolvedOpenApiObject added = OpenApiReferenceResolver.Resolve(context.HeadRoot, rawHead);
            if (added.IsUnresolvedReference)
            {
                context.Collector.ReviewRequired(ContractChangeKinds.UnresolvedRef, target, $"Added unresolved referenced HTTP {status} response '{added.Reference}' to {opLabel}; manual review required.");
            }
            else
            {
                context.Collector.Additive(ContractChangeKinds.ResponseAdded, target, $"Added HTTP {status} response to {opLabel}.");
            }

            return;
        }

        if (!hasBase)
        {
            return;
        }

        ResolvedOpenApiObject baseNode = OpenApiReferenceResolver.Resolve(context.BaseRoot, rawBase);
        ResolvedOpenApiObject headNode = OpenApiReferenceResolver.Resolve(context.HeadRoot, rawHead);
        if (baseNode.IsUnresolvedReference || headNode.IsUnresolvedReference)
        {
            context.Collector.ReviewRequired(
                ContractChangeKinds.UnresolvedRef,
                target,
                $"Unresolved referenced HTTP {status} response '{headNode.Reference ?? baseNode.Reference}' on {opLabel}; manual review required.");
            return;
        }

        OpenApiReferenceResolver.DiffRawReferenceFields(baseNode, headNode, target, context.Collector);
        OpenApiReferenceResolver.DiffExtensions(baseNode.Target, headNode.Target, target, context.Collector);
        DiffContentMediaTypes(context, baseNode.Target, headNode.Target, target);
        OpenApiReferenceResolver.DiffUnhandledFields(baseNode.Target, headNode.Target, target, ResponseHandledKeys, context.Collector);
    }

    private static void DiffContentMediaTypes(DocumentContext context, JsonElement baseContainer, JsonElement headContainer, string parentTarget)
    {
        Dictionary<string, JsonElement> baseContent = JsonElementCanonicalizer.ReadObjectMap(baseContainer, ContentKeyword);
        Dictionary<string, JsonElement> headContent = JsonElementCanonicalizer.ReadObjectMap(headContainer, ContentKeyword);

        foreach (string mediaType in baseContent.Keys.Union(headContent.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseContent.TryGetValue(mediaType, out JsonElement baseMedia) && baseMedia.ValueKind == JsonValueKind.Object;
            bool hasHead = headContent.TryGetValue(mediaType, out JsonElement headMedia) && headMedia.ValueKind == JsonValueKind.Object;
            string target = $"{parentTarget} [{mediaType}]";

            if (hasBase && !hasHead)
            {
                context.Collector.Breaking(ContractChangeKinds.MediaTypeRemoved, target, $"Removed media type '{mediaType}' from {parentTarget}.");
            }
            else if (!hasBase && hasHead)
            {
                context.Collector.Additive(ContractChangeKinds.MediaTypeAdded, target, $"Added media type '{mediaType}' to {parentTarget}.");
            }
            else if (hasBase)
            {
                OpenApiReferenceResolver.DiffExtensions(baseMedia, headMedia, target, context.Collector);
                DiffOptionalChildSchema(context, baseMedia, headMedia, target, $"media type '{mediaType}' for {parentTarget}");
                OpenApiReferenceResolver.DiffUnhandledFields(baseMedia, headMedia, target, MediaTypeHandledKeys, context.Collector);
            }
        }
    }

    private static void DiffOptionalChildSchema(
        DocumentContext context,
        JsonElement baseParent,
        JsonElement headParent,
        string target,
        string description)
    {
        bool baseHasSchema = baseParent.TryGetProperty(SchemaKeyword, out JsonElement baseSchema);
        bool headHasSchema = headParent.TryGetProperty(SchemaKeyword, out JsonElement headSchema);
        if (baseHasSchema && headHasSchema)
        {
            ConservativeJsonSchemaDiffer.DiffSchema(
                context.Collector,
                target,
                context.BaseRoot,
                context.HeadRoot,
                baseSchema,
                headSchema);
        }
        else if (baseHasSchema != headHasSchema)
        {
            context.Collector.ReviewRequired(
                ContractChangeKinds.OpenApiReviewRequired,
                $"{target}.{SchemaKeyword}",
                $"Schema presence changed on {description}; manual review required.");
        }
    }

    private static void DiffNamedSchemas(DocumentContext context, string containerKey, string? nestedKey, string labelPrefix)
    {
        Dictionary<string, JsonElement> baseSchemas = JsonElementCanonicalizer.ReadNestedObjectMap(context.BaseRoot, containerKey, nestedKey);
        Dictionary<string, JsonElement> headSchemas = JsonElementCanonicalizer.ReadNestedObjectMap(context.HeadRoot, containerKey, nestedKey);

        foreach (string name in baseSchemas.Keys.Union(headSchemas.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseSchemas.TryGetValue(name, out JsonElement baseSchema);
            bool hasHead = headSchemas.TryGetValue(name, out JsonElement headSchema);
            string target = $"{labelPrefix}.{name}";

            if (hasBase && !hasHead)
            {
                context.Collector.Breaking(ContractChangeKinds.SchemaRemoved, target, $"Removed schema '{target}'.");
            }
            else if (!hasBase && hasHead)
            {
                context.Collector.Additive(ContractChangeKinds.SchemaAdded, target, $"Added schema '{target}'.");
            }
            else
            {
                ConservativeJsonSchemaDiffer.DiffSchema(
                    context.Collector,
                    target,
                    context.BaseRoot,
                    context.HeadRoot,
                    baseSchema,
                    headSchema);
            }
        }
    }

    private static Dictionary<string, ResolvedOpenApiObject> ReadParameterMap(JsonElement root, JsonElement container)
    {
        var result = new Dictionary<string, ResolvedOpenApiObject>(StringComparer.Ordinal);
        if (!container.TryGetProperty(ParametersKeyword, out JsonElement parameters) || parameters.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement param in parameters.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
        {
            ResolvedOpenApiObject resolved = OpenApiReferenceResolver.Resolve(root, param);
            if (resolved.IsUnresolvedReference)
            {
                result[$"$ref:{resolved.Reference}"] = resolved;
                continue;
            }

            string name = resolved.Target.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty;
            string location = resolved.Target.TryGetProperty("in", out JsonElement i) ? i.GetString() ?? string.Empty : string.Empty;
            if (name.Length > 0)
            {
                result[$"{location}:{name}"] = resolved;
            }
            else if (resolved.Reference is not null)
            {
                result[$"$ref:{resolved.Reference}"] = resolved with { IsUnresolvedReference = true };
            }
        }

        return result;
    }

    private static bool IsParameterRequired(JsonElement parameter) =>
        IsRequiredFlag(parameter) ||
        (parameter.TryGetProperty("in", out JsonElement location) &&
         location.ValueKind == JsonValueKind.String &&
         string.Equals(location.GetString(), "path", StringComparison.Ordinal));

    private static bool IsRequiredFlag(JsonElement element) =>
        element.TryGetProperty(RequiredKeyword, out JsonElement required) && required.ValueKind == JsonValueKind.True;

    private static bool IsOpenApiJsonPath(string path) =>
        path.StartsWith("schemas/openapi_", StringComparison.Ordinal) &&
        path.EndsWith(".json", StringComparison.Ordinal);

    private ContractDifferResult Failure(string? diagnostic) =>
        new(false, Family, [], Diagnostic: diagnostic);
}
