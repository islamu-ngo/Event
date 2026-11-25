using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Conservative structural differ for OpenAPI 3.x / JSON Schema 2020-12 documents including
/// operation routing, security requirements, parameters, request/response bodies, and HAL <c>_links</c>.
/// Unknown structural mutations, vendor extensions, defaults, and unresolved <c>$ref</c> targets
/// fail conservatively to <see cref="ContractChangeSeverity.ReviewRequired"/>.
/// </summary>
public sealed class OpenApiContractDiffer : IReleaseContractDiffer
{
    private const int MaxJsonDepth = 64;
    private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete", "head", "options", "trace"];
    private static readonly HashSet<string> DocumentHandledKeys = new(StringComparer.Ordinal)
    {
        "paths",
        "components",
        "$defs",
        "security",
        "info",
        "tags",
        "externalDocs",
    };
    private static readonly HashSet<string> PathItemHandledKeys = new(
        HttpMethods.Concat(["parameters", "summary", "description"]),
        StringComparer.Ordinal);
    private static readonly HashSet<string> OperationHandledKeys = new(StringComparer.Ordinal)
    {
        "security",
        "parameters",
        "requestBody",
        "responses",
        "tags",
        "summary",
        "description",
        "externalDocs",
        "operationId",
    };
    private static readonly HashSet<string> ParameterHandledKeys = new(StringComparer.Ordinal)
    {
        "name",
        "in",
        "required",
        "schema",
        "description",
        "example",
        "examples",
    };
    private static readonly HashSet<string> RequestBodyHandledKeys = new(StringComparer.Ordinal)
    {
        "required",
        "content",
        "description",
    };
    private static readonly HashSet<string> ResponseHandledKeys = new(StringComparer.Ordinal)
    {
        "content",
        "description",
        "summary",
    };
    private static readonly HashSet<string> MediaTypeHandledKeys = new(StringComparer.Ordinal)
    {
        "schema",
        "example",
        "examples",
    };
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        MaxDepth = MaxJsonDepth,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    public string Family => "openapi";

    public int Order => 10;

    public ContractDifferResult Diff(IGitObjectReader reader, string baseOid, string headOid)
    {
        ArgumentNullException.ThrowIfNull(reader);

        GitTreeListResult baseTree = reader.ListFiles(baseOid, "schemas");
        if (!baseTree.IsValid)
        {
            return new ContractDifferResult(false, Family, [], Diagnostic: baseTree.Diagnostic);
        }

        GitTreeListResult headTree = reader.ListFiles(headOid, "schemas");
        if (!headTree.IsValid)
        {
            return new ContractDifferResult(false, Family, [], Diagnostic: headTree.Diagnostic);
        }

        HashSet<string> basePaths = baseTree.Paths.Where(IsOpenApiJsonPath).ToHashSet(StringComparer.Ordinal);
        HashSet<string> headPaths = headTree.Paths.Where(IsOpenApiJsonPath).ToHashSet(StringComparer.Ordinal);
        string[] allPaths = basePaths.Union(headPaths, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        var changes = new List<ContractDiffChange>();
        foreach (string path in allPaths)
        {
            bool inBase = basePaths.Contains(path);
            bool inHead = headPaths.Contains(path);

            if (inBase && !inHead)
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Breaking,
                    "contract_file_removed",
                    path,
                    $"Removed OpenAPI contract file '{path}'.",
                    path));
                continue;
            }

            if (!inBase && inHead)
            {
                GitBlobReadResult addedBlob = reader.ReadBlob(headOid, path);
                if (addedBlob.Diagnostic is not null || !addedBlob.Found || addedBlob.Bytes is null)
                {
                    return new ContractDifferResult(false, Family, [], Diagnostic: addedBlob.Diagnostic ?? "contract_diff_openapi_read_failed");
                }

                if (!TryParseObject(addedBlob.Bytes, out JsonDocument? addedDoc))
                {
                    return new ContractDifferResult(false, Family, [], Diagnostic: "contract_diff_openapi_malformed");
                }

                addedDoc.Dispose();
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "contract_file_added",
                    path,
                    $"Added new OpenAPI contract file '{path}'; review initial public contract surface.",
                    path));
                continue;
            }

            GitBlobReadResult baseBlob = reader.ReadBlob(baseOid, path);
            GitBlobReadResult headBlob = reader.ReadBlob(headOid, path);
            if (baseBlob.Diagnostic is not null || headBlob.Diagnostic is not null ||
                !baseBlob.Found || !headBlob.Found || baseBlob.Bytes is null || headBlob.Bytes is null)
            {
                return new ContractDifferResult(
                    false,
                    Family,
                    [],
                    Diagnostic: baseBlob.Diagnostic ?? headBlob.Diagnostic ?? "contract_diff_openapi_read_failed");
            }

            if (!TryParseObject(baseBlob.Bytes, out JsonDocument? baseDoc))
            {
                return new ContractDifferResult(false, Family, [], Diagnostic: "contract_diff_openapi_malformed");
            }

            using (baseDoc)
            {
                if (!TryParseObject(headBlob.Bytes, out JsonDocument? headDoc))
                {
                    return new ContractDifferResult(false, Family, [], Diagnostic: "contract_diff_openapi_malformed");
                }

                using (headDoc)
                {
                    if (baseBlob.Bytes.AsSpan().SequenceEqual(headBlob.Bytes))
                    {
                        continue;
                    }

                    DiffDocument(baseDoc.RootElement, headDoc.RootElement, path, changes);
                }
            }
        }

        return new ContractDifferResult(true, Family, changes);
    }

    private void DiffDocument(JsonElement baseRoot, JsonElement headRoot, string artifactPath, List<ContractDiffChange> changes)
    {
        DiffExtensions(baseRoot, headRoot, "document", artifactPath, changes);
        DiffSecurity(baseRoot, headRoot, "document", artifactPath, changes);
        DiffUnhandledFields(baseRoot, headRoot, "document", DocumentHandledKeys, artifactPath, changes);
        DiffPaths(baseRoot, headRoot, artifactPath, changes);
        DiffComponents(baseRoot, headRoot, artifactPath, changes);
        DiffNamedSchemas(baseRoot, headRoot, "$defs", null, "$defs", artifactPath, changes);
    }

    private void DiffComponents(JsonElement baseRoot, JsonElement headRoot, string artifactPath, List<ContractDiffChange> changes)
    {
        DiffNamedSchemas(baseRoot, headRoot, "components", "schemas", "components.schemas", artifactPath, changes);

        bool hasBaseComponents = baseRoot.TryGetProperty("components", out JsonElement baseComponents) && baseComponents.ValueKind == JsonValueKind.Object;
        bool hasHeadComponents = headRoot.TryGetProperty("components", out JsonElement headComponents) && headComponents.ValueKind == JsonValueKind.Object;
        if (!hasBaseComponents && !hasHeadComponents)
        {
            return;
        }

        if (hasBaseComponents && hasHeadComponents)
        {
            DiffExtensions(baseComponents, headComponents, "components", artifactPath, changes);
        }

        Dictionary<string, JsonElement> baseSections = ReadObjectMap(baseRoot, "components");
        Dictionary<string, JsonElement> headSections = ReadObjectMap(headRoot, "components");

        foreach (string section in baseSections.Keys.Union(headSections.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (string.Equals(section, "schemas", StringComparison.Ordinal) ||
                section.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool hasBase = baseSections.TryGetValue(section, out JsonElement baseSection);
            bool hasHead = headSections.TryGetValue(section, out JsonElement headSection);

            if ((hasBase && baseSection.ValueKind == JsonValueKind.Object) ||
                (hasHead && headSection.ValueKind == JsonValueKind.Object))
            {
                Dictionary<string, JsonElement> baseEntries = hasBase && baseSection.ValueKind == JsonValueKind.Object
                    ? EnumerateObjectMap(baseSection)
                    : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                Dictionary<string, JsonElement> headEntries = hasHead && headSection.ValueKind == JsonValueKind.Object
                    ? EnumerateObjectMap(headSection)
                    : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

                foreach (string entryName in baseEntries.Keys.Union(headEntries.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    bool entryInBase = baseEntries.TryGetValue(entryName, out JsonElement baseEntry);
                    bool entryInHead = headEntries.TryGetValue(entryName, out JsonElement headEntry);
                    string target = $"components.{section}.{entryName}";

                    if (!entryInBase || !entryInHead ||
                        !string.Equals(
                            ConservativeJsonSchemaDiffer.CanonicalJson(baseEntry),
                            ConservativeJsonSchemaDiffer.CanonicalJson(headEntry),
                            StringComparison.Ordinal))
                    {
                        changes.Add(new ContractDiffChange(
                            Family,
                            ContractChangeSeverity.ReviewRequired,
                            "openapi_component_changed",
                            target,
                            $"OpenAPI component '{target}' changed; manual contract review required.",
                            artifactPath));
                    }
                }
            }
            else if (!hasBase || !hasHead ||
                !string.Equals(
                    ConservativeJsonSchemaDiffer.CanonicalJson(baseSection),
                    ConservativeJsonSchemaDiffer.CanonicalJson(headSection),
                    StringComparison.Ordinal))
            {
                string target = $"components.{section}";
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "openapi_component_changed",
                    target,
                    $"OpenAPI component section '{target}' changed; manual contract review required.",
                    artifactPath));
            }
        }
    }

    private void DiffPaths(JsonElement baseRoot, JsonElement headRoot, string artifactPath, List<ContractDiffChange> changes)
    {
        Dictionary<string, JsonElement> basePaths = ReadObjectMap(baseRoot, "paths");
        Dictionary<string, JsonElement> headPaths = ReadObjectMap(headRoot, "paths");

        foreach (string route in basePaths.Keys.Union(headPaths.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = basePaths.TryGetValue(route, out JsonElement basePathItem) && basePathItem.ValueKind == JsonValueKind.Object;
            bool hasHead = headPaths.TryGetValue(route, out JsonElement headPathItem) && headPathItem.ValueKind == JsonValueKind.Object;

            if (hasBase && !hasHead)
            {
                bool emittedOperation = false;
                foreach (string method in HttpMethods)
                {
                    if (basePathItem.TryGetProperty(method, out _))
                    {
                        emittedOperation = true;
                        string label = $"{method.ToUpperInvariant()} {route}";
                        changes.Add(new ContractDiffChange(
                            Family,
                            ContractChangeSeverity.Breaking,
                            "operation_removed",
                            label,
                            $"Removed API operation {label}.",
                            artifactPath));
                    }
                }

                if (!emittedOperation)
                {
                    changes.Add(new ContractDiffChange(
                        Family,
                        ContractChangeSeverity.ReviewRequired,
                        "openapi_review_required",
                        $"path:{route}",
                        $"Removed path item '{route}' with non-standard or referenced structure; manual review required.",
                        artifactPath));
                }

                continue;
            }

            if (!hasBase && hasHead)
            {
                bool emittedOperation = false;
                foreach (string method in HttpMethods)
                {
                    if (headPathItem.TryGetProperty(method, out _))
                    {
                        emittedOperation = true;
                        string label = $"{method.ToUpperInvariant()} {route}";
                        changes.Add(new ContractDiffChange(
                            Family,
                            ContractChangeSeverity.Additive,
                            "operation_added",
                            label,
                            $"Added API operation {label}.",
                            artifactPath));
                    }
                }

                if (!emittedOperation)
                {
                    changes.Add(new ContractDiffChange(
                        Family,
                        ContractChangeSeverity.ReviewRequired,
                        "openapi_review_required",
                        $"path:{route}",
                        $"Added path item '{route}' with non-standard or referenced structure; manual review required.",
                        artifactPath));
                }

                continue;
            }

            if (!hasBase || !hasHead)
            {
                continue;
            }

            string pathTarget = $"path:{route}";
            DiffExtensions(basePathItem, headPathItem, pathTarget, artifactPath, changes);
            DiffParameters(baseRoot, headRoot, basePathItem, headPathItem, pathTarget, artifactPath, changes);
            DiffUnhandledFields(basePathItem, headPathItem, pathTarget, PathItemHandledKeys, artifactPath, changes);
            foreach (string method in HttpMethods)
            {
                bool hasBaseOp = basePathItem.TryGetProperty(method, out JsonElement baseOp) && baseOp.ValueKind == JsonValueKind.Object;
                bool hasHeadOp = headPathItem.TryGetProperty(method, out JsonElement headOp) && headOp.ValueKind == JsonValueKind.Object;
                string opLabel = $"{method.ToUpperInvariant()} {route}";

                if (hasBaseOp && !hasHeadOp)
                {
                    changes.Add(new ContractDiffChange(
                        Family,
                        ContractChangeSeverity.Breaking,
                        "operation_removed",
                        opLabel,
                        $"Removed API operation {opLabel}.",
                        artifactPath));
                    continue;
                }

                if (!hasBaseOp && hasHeadOp)
                {
                    changes.Add(new ContractDiffChange(
                        Family,
                        ContractChangeSeverity.Additive,
                        "operation_added",
                        opLabel,
                        $"Added API operation {opLabel}.",
                        artifactPath));
                    continue;
                }

                if (hasBaseOp && hasHeadOp)
                {
                    DiffOperation(baseRoot, headRoot, baseOp, headOp, opLabel, artifactPath, changes);
                }
            }
        }
    }

    private void DiffOperation(
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseOp,
        JsonElement headOp,
        string opLabel,
        string artifactPath,
        List<ContractDiffChange> changes)
    {
        DiffExtensions(baseOp, headOp, opLabel, artifactPath, changes);
        DiffSecurity(baseOp, headOp, opLabel, artifactPath, changes);
        DiffParameters(baseRoot, headRoot, baseOp, headOp, opLabel, artifactPath, changes);
        DiffRequestBody(baseRoot, headRoot, baseOp, headOp, opLabel, artifactPath, changes);
        DiffResponses(baseRoot, headRoot, baseOp, headOp, opLabel, artifactPath, changes);
        DiffUnhandledFields(baseOp, headOp, opLabel, OperationHandledKeys, artifactPath, changes);
    }

    private void DiffSecurity(JsonElement baseOp, JsonElement headOp, string opLabel, string artifactPath, List<ContractDiffChange> changes)
    {
        HashSet<string> baseReq = ReadSecurityOptions(baseOp);
        HashSet<string> headReq = ReadSecurityOptions(headOp);
        if (baseReq.SetEquals(headReq))
        {
            return;
        }

        // Anonymous/unrestricted is represented by an empty security array (Count == 0).
        if (baseReq.Count == 0 && headReq.Count > 0)
        {
            changes.Add(new ContractDiffChange(
                Family,
                ContractChangeSeverity.Breaking,
                "security_narrowed",
                opLabel,
                $"Operation {opLabel} now requires authentication schemes where anonymous access was previously allowed.",
                artifactPath));
            return;
        }

        if (baseReq.Count > 0 && headReq.Count == 0)
        {
            changes.Add(new ContractDiffChange(
                Family,
                ContractChangeSeverity.ReviewRequired,
                "security_changed",
                opLabel,
                $"Operation {opLabel} removed explicit security requirements; verify authorization policy.",
                artifactPath));
            return;
        }

        bool anyRemoved = baseReq.Except(headReq, StringComparer.Ordinal).Any();
        bool anyAdded = headReq.Except(baseReq, StringComparer.Ordinal).Any();
        if (anyRemoved)
        {
            changes.Add(new ContractDiffChange(
                Family,
                ContractChangeSeverity.Breaking,
                "security_narrowed",
                opLabel,
                $"Operation {opLabel} removed previously accepted security scheme requirements.",
                artifactPath));
        }
        else if (anyAdded)
        {
            changes.Add(new ContractDiffChange(
                Family,
                ContractChangeSeverity.Additive,
                "security_widened",
                opLabel,
                $"Operation {opLabel} added an alternative security scheme option.",
                artifactPath));
        }
    }

    private static HashSet<string> ReadSecurityOptions(JsonElement operation)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!operation.TryGetProperty("security", out JsonElement security) || security.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement requirement in security.EnumerateArray())
        {
            if (requirement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string canonical = string.Join(
                "+",
                requirement.EnumerateObject()
                    .OrderBy(prop => prop.Name, StringComparer.Ordinal)
                    .Select(prop =>
                    {
                        string scopes = prop.Value.ValueKind == JsonValueKind.Array
                            ? string.Join(",", prop.Value.EnumerateArray().Select(v => v.ToString()).Order(StringComparer.Ordinal))
                            : string.Empty;
                        return $"{prop.Name}[{scopes}]";
                    }));
            result.Add(canonical);
        }

        return result;
    }

    private void DiffParameters(
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseOp,
        JsonElement headOp,
        string opLabel,
        string artifactPath,
        List<ContractDiffChange> changes)
    {
        Dictionary<string, JsonElement> baseParams = ReadParameterMap(baseOp);
        Dictionary<string, JsonElement> headParams = ReadParameterMap(headOp);

        foreach (string key in baseParams.Keys.Union(headParams.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseParams.TryGetValue(key, out JsonElement baseParam);
            bool hasHead = headParams.TryGetValue(key, out JsonElement headParam);
            string target = $"{opLabel} param:{key}";

            if (hasBase && !hasHead)
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Breaking,
                    "parameter_removed",
                    target,
                    $"Removed parameter '{key}' from {opLabel}.",
                    artifactPath));
                continue;
            }

            if (!hasBase && hasHead)
            {
                bool required = IsRequiredFlag(headParam);
                changes.Add(new ContractDiffChange(
                    Family,
                    required ? ContractChangeSeverity.Breaking : ContractChangeSeverity.Additive,
                    required ? "required_parameter_added" : "optional_parameter_added",
                    target,
                    required
                        ? $"Added required parameter '{key}' to {opLabel}."
                        : $"Added optional parameter '{key}' to {opLabel}.",
                    artifactPath));
                continue;
            }

            bool baseRequired = IsRequiredFlag(baseParam);
            bool headRequired = IsRequiredFlag(headParam);
            if (!baseRequired && headRequired)
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Breaking,
                    "parameter_narrowed_required",
                    target,
                    $"Parameter '{key}' on {opLabel} changed from optional to required.",
                    artifactPath));
            }
            else if (baseRequired && !headRequired)
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Additive,
                    "parameter_widened_optional",
                    target,
                    $"Parameter '{key}' on {opLabel} changed from required to optional.",
                    artifactPath));
            }

            DiffExtensions(baseParam, headParam, target, artifactPath, changes);
            bool baseHasSchema = baseParam.TryGetProperty("schema", out JsonElement baseSchema);
            bool headHasSchema = headParam.TryGetProperty("schema", out JsonElement headSchema);
            if (baseHasSchema && headHasSchema)
            {
                ConservativeJsonSchemaDiffer.DiffSchema(
                    Family,
                    artifactPath,
                    target,
                    baseRoot,
                    headRoot,
                    baseSchema,
                    headSchema,
                    changes);
            }
            else if (baseHasSchema != headHasSchema)
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "openapi_review_required",
                    $"{target}.schema",
                    $"Schema presence changed on parameter '{key}' of {opLabel}; manual review required.",
                    artifactPath));
            }

            DiffUnhandledFields(baseParam, headParam, target, ParameterHandledKeys, artifactPath, changes);
        }
    }

    private void DiffRequestBody(
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseOp,
        JsonElement headOp,
        string opLabel,
        string artifactPath,
        List<ContractDiffChange> changes)
    {
        bool hasBase = baseOp.TryGetProperty("requestBody", out JsonElement baseBody) && baseBody.ValueKind == JsonValueKind.Object;
        bool hasHead = headOp.TryGetProperty("requestBody", out JsonElement headBody) && headBody.ValueKind == JsonValueKind.Object;
        string target = $"{opLabel} requestBody";

        if (hasBase && !hasHead)
        {
            changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Breaking, "request_body_removed", target, $"Removed request body from {opLabel}.", artifactPath));
            return;
        }

        if (!hasBase && hasHead)
        {
            bool required = IsRequiredFlag(headBody);
            changes.Add(new ContractDiffChange(
                Family,
                required ? ContractChangeSeverity.Breaking : ContractChangeSeverity.Additive,
                required ? "required_request_body_added" : "optional_request_body_added",
                target,
                required ? $"Added required request body to {opLabel}." : $"Added optional request body to {opLabel}.",
                artifactPath));
            return;
        }

        if (!hasBase || !hasHead)
        {
            return;
        }

        bool baseRequired = IsRequiredFlag(baseBody);
        bool headRequired = IsRequiredFlag(headBody);
        if (!baseRequired && headRequired)
        {
            changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Breaking, "request_body_narrowed_required", target, $"Request body on {opLabel} became required.", artifactPath));
        }
        else if (baseRequired && !headRequired)
        {
            changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Additive, "request_body_widened_optional", target, $"Request body on {opLabel} changed from required to optional.", artifactPath));
        }

        DiffExtensions(baseBody, headBody, target, artifactPath, changes);
        DiffContentMediaTypes(baseRoot, headRoot, baseBody, headBody, target, artifactPath, changes);
        DiffUnhandledFields(baseBody, headBody, target, RequestBodyHandledKeys, artifactPath, changes);
    }

    private void DiffResponses(
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseOp,
        JsonElement headOp,
        string opLabel,
        string artifactPath,
        List<ContractDiffChange> changes)
    {
        Dictionary<string, JsonElement> baseResponses = ReadObjectMap(baseOp, "responses");
        Dictionary<string, JsonElement> headResponses = ReadObjectMap(headOp, "responses");

        foreach (string status in baseResponses.Keys.Union(headResponses.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseResponses.TryGetValue(status, out JsonElement baseResp) && baseResp.ValueKind == JsonValueKind.Object;
            bool hasHead = headResponses.TryGetValue(status, out JsonElement headResp) && headResp.ValueKind == JsonValueKind.Object;
            string target = $"{opLabel} response:{status}";

            if (hasBase && !hasHead)
            {
                changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Breaking, "response_removed", target, $"Removed HTTP {status} response from {opLabel}.", artifactPath));
                continue;
            }

            if (!hasBase && hasHead)
            {
                changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Additive, "response_added", target, $"Added HTTP {status} response to {opLabel}.", artifactPath));
                continue;
            }

            if (hasBase && hasHead)
            {
                DiffExtensions(baseResp, headResp, target, artifactPath, changes);
                DiffContentMediaTypes(baseRoot, headRoot, baseResp, headResp, target, artifactPath, changes);
                DiffUnhandledFields(baseResp, headResp, target, ResponseHandledKeys, artifactPath, changes);
            }
        }
    }

    private void DiffContentMediaTypes(
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseContainer,
        JsonElement headContainer,
        string parentTarget,
        string artifactPath,
        List<ContractDiffChange> changes)
    {
        Dictionary<string, JsonElement> baseContent = ReadObjectMap(baseContainer, "content");
        Dictionary<string, JsonElement> headContent = ReadObjectMap(headContainer, "content");

        foreach (string mediaType in baseContent.Keys.Union(headContent.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseContent.TryGetValue(mediaType, out JsonElement baseMedia) && baseMedia.ValueKind == JsonValueKind.Object;
            bool hasHead = headContent.TryGetValue(mediaType, out JsonElement headMedia) && headMedia.ValueKind == JsonValueKind.Object;
            string target = $"{parentTarget} [{mediaType}]";

            if (hasBase && !hasHead)
            {
                changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Breaking, "media_type_removed", target, $"Removed media type '{mediaType}' from {parentTarget}.", artifactPath));
                continue;
            }

            if (!hasBase && hasHead)
            {
                changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Additive, "media_type_added", target, $"Added media type '{mediaType}' to {parentTarget}.", artifactPath));
                continue;
            }

            if (hasBase && hasHead)
            {
                DiffExtensions(baseMedia, headMedia, target, artifactPath, changes);
                bool baseHasSchema = baseMedia.TryGetProperty("schema", out JsonElement baseSchema);
                bool headHasSchema = headMedia.TryGetProperty("schema", out JsonElement headSchema);
                if (baseHasSchema && headHasSchema)
                {
                    ConservativeJsonSchemaDiffer.DiffSchema(
                        Family,
                        artifactPath,
                        target,
                        baseRoot,
                        headRoot,
                        baseSchema,
                        headSchema,
                        changes);
                }
                else if (baseHasSchema != headHasSchema)
                {
                    changes.Add(new ContractDiffChange(
                        Family,
                        ContractChangeSeverity.ReviewRequired,
                        "openapi_review_required",
                        $"{target}.schema",
                        $"Schema presence changed on media type '{mediaType}' for {parentTarget}; manual review required.",
                        artifactPath));
                }

                DiffUnhandledFields(baseMedia, headMedia, target, MediaTypeHandledKeys, artifactPath, changes);
            }
        }
    }

    private void DiffNamedSchemas(
        JsonElement baseRoot,
        JsonElement headRoot,
        string containerKey,
        string? nestedKey,
        string labelPrefix,
        string artifactPath,
        List<ContractDiffChange> changes)
    {
        Dictionary<string, JsonElement> baseSchemas = ReadNestedObjectMap(baseRoot, containerKey, nestedKey);
        Dictionary<string, JsonElement> headSchemas = ReadNestedObjectMap(headRoot, containerKey, nestedKey);

        foreach (string name in baseSchemas.Keys.Union(headSchemas.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseSchemas.TryGetValue(name, out JsonElement baseSchema);
            bool hasHead = headSchemas.TryGetValue(name, out JsonElement headSchema);
            string target = $"{labelPrefix}.{name}";

            if (hasBase && !hasHead)
            {
                changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Breaking, "schema_removed", target, $"Removed schema '{target}'.", artifactPath));
                continue;
            }

            if (!hasBase && hasHead)
            {
                changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Additive, "schema_added", target, $"Added schema '{target}'.", artifactPath));
                continue;
            }

            ConservativeJsonSchemaDiffer.DiffSchema(
                Family,
                artifactPath,
                target,
                baseRoot,
                headRoot,
                baseSchema,
                headSchema,
                changes);
        }
    }

    private void DiffExtensions(JsonElement baseNode, JsonElement headNode, string target, string artifactPath, List<ContractDiffChange> changes)
    {
        Dictionary<string, string> baseExt = ReadExtensions(baseNode);
        Dictionary<string, string> headExt = ReadExtensions(headNode);

        foreach (string key in baseExt.Keys.Union(headExt.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            baseExt.TryGetValue(key, out string? baseVal);
            headExt.TryGetValue(key, out string? headVal);
            if (!string.Equals(baseVal, headVal, StringComparison.Ordinal))
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "extension_changed",
                    $"{target} {key}",
                    $"Vendor extension '{key}' changed on {target}; manual contract review required.",
                    artifactPath));
            }
        }
    }

    private static Dictionary<string, string> ReadExtensions(JsonElement node)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty prop in node.EnumerateObject())
        {
            if (prop.Name.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
            {
                result[prop.Name] = ConservativeJsonSchemaDiffer.CanonicalJson(prop.Value);
            }
        }

        return result;
    }

    private void DiffUnhandledFields(
        JsonElement baseNode,
        JsonElement headNode,
        string target,
        HashSet<string> handledKeys,
        string artifactPath,
        List<ContractDiffChange> changes)
    {
        Dictionary<string, string> baseExtra = ReadUnhandledFields(baseNode, handledKeys);
        Dictionary<string, string> headExtra = ReadUnhandledFields(headNode, handledKeys);

        foreach (string key in baseExtra.Keys.Union(headExtra.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            baseExtra.TryGetValue(key, out string? baseVal);
            headExtra.TryGetValue(key, out string? headVal);
            if (!string.Equals(baseVal, headVal, StringComparison.Ordinal))
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "openapi_review_required",
                    $"{target}.{key}",
                    $"OpenAPI structural field '{key}' changed on {target}; manual contract review required.",
                    artifactPath));
            }
        }
    }

    private static Dictionary<string, string> ReadUnhandledFields(JsonElement node, HashSet<string> handledKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty prop in node.EnumerateObject())
        {
            if (!handledKeys.Contains(prop.Name) && !prop.Name.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
            {
                result[prop.Name] = ConservativeJsonSchemaDiffer.CanonicalJson(prop.Value);
            }
        }

        return result;
    }

    private static Dictionary<string, JsonElement> EnumerateObjectMap(JsonElement obj)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in obj.EnumerateObject())
            {
                result[prop.Name] = prop.Value;
            }
        }

        return result;
    }

    private static Dictionary<string, JsonElement> ReadParameterMap(JsonElement operation)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!operation.TryGetProperty("parameters", out JsonElement parameters) || parameters.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement param in parameters.EnumerateArray())
        {
            if (param.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string name = param.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty;
            string location = param.TryGetProperty("in", out JsonElement i) ? i.GetString() ?? string.Empty : string.Empty;
            if (name.Length > 0)
            {
                result[$"{location}:{name}"] = param;
            }
            else if (param.TryGetProperty("$ref", out JsonElement refProp) &&
                refProp.ValueKind == JsonValueKind.String &&
                refProp.GetString() is { Length: > 0 } reference)
            {
                result[$"$ref:{reference}"] = param;
            }
        }

        return result;
    }

    private static bool IsRequiredFlag(JsonElement element) =>
        element.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.True;

    private static Dictionary<string, JsonElement> ReadObjectMap(JsonElement parent, string propertyName)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(propertyName, out JsonElement child) &&
            child.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in child.EnumerateObject())
            {
                result[prop.Name] = prop.Value;
            }
        }

        return result;
    }

    private static Dictionary<string, JsonElement> ReadNestedObjectMap(JsonElement parent, string firstKey, string? secondKey)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(firstKey, out JsonElement first) || first.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        if (secondKey is null)
        {
            var direct = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty prop in first.EnumerateObject())
            {
                direct[prop.Name] = prop.Value;
            }

            return direct;
        }

        return ReadObjectMap(first, secondKey);
    }

    private static bool IsOpenApiJsonPath(string path) =>
        path.StartsWith("schemas/openapi_", StringComparison.Ordinal) &&
        path.EndsWith(".json", StringComparison.Ordinal);

    internal static bool TryParseObject(byte[] bytes, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            JsonDocument parsed = JsonDocument.Parse(bytes, ParseOptions);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                document = null;
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }
}

/// <summary>
/// Shared conservative JSON Schema / HAL structural differ used across OpenAPI and Configuration contracts.
/// </summary>
internal static class ConservativeJsonSchemaDiffer
{
    private const int MaximumDepth = 24;
    private static readonly HashSet<string> HandledKeywords = new(StringComparer.Ordinal)
    {
        "$ref",
        "type",
        "required",
        "properties",
        "items",
        "enum",
        "const",
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
        "$ref",
        "title",
        "description",
        "$comment",
        "example",
        "examples",
    };

    public static void DiffSchema(
        string family,
        string artifactPath,
        string target,
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseSchema,
        JsonElement headSchema,
        List<ContractDiffChange> changes)
    {
        DiffSchemaCore(
            family,
            artifactPath,
            target,
            baseRoot,
            headRoot,
            baseSchema,
            headSchema,
            changes,
            depth: 0,
            visitedRefs: new HashSet<string>(StringComparer.Ordinal));
    }

    private static void DiffSchemaCore(
        string family,
        string artifactPath,
        string target,
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseSchema,
        JsonElement headSchema,
        List<ContractDiffChange> changes,
        int depth,
        HashSet<string> visitedRefs)
    {
        if (depth > MaximumDepth)
        {
            changes.Add(new ContractDiffChange(
                family,
                ContractChangeSeverity.ReviewRequired,
                "schema_review_required",
                target,
                $"Schema comparison depth exceeded limit at '{target}'; manual review required.",
                artifactPath));
            return;
        }

        if (baseSchema.ValueKind != JsonValueKind.Object || headSchema.ValueKind != JsonValueKind.Object)
        {
            if (!string.Equals(CanonicalJson(baseSchema), CanonicalJson(headSchema), StringComparison.Ordinal))
            {
                changes.Add(new ContractDiffChange(
                    family,
                    ContractChangeSeverity.ReviewRequired,
                    "schema_review_required",
                    target,
                    $"Structural schema node changed at '{target}'; manual review required.",
                    artifactPath));
            }

            return;
        }

        bool baseHasRef = TryGetStringProperty(baseSchema, "$ref", out string? baseRef);
        bool headHasRef = TryGetStringProperty(headSchema, "$ref", out string? headRef);
        if (baseHasRef || headHasRef)
        {
            if (baseHasRef && headHasRef && string.Equals(baseRef, headRef, StringComparison.Ordinal))
            {
                if (!TryResolveLocalRef(baseRoot, baseRef!, out _) || !TryResolveLocalRef(headRoot, headRef!, out _))
                {
                    changes.Add(new ContractDiffChange(
                        family,
                        ContractChangeSeverity.ReviewRequired,
                        "unresolved_ref",
                        target,
                        $"Unresolved or external $ref '{headRef}' at '{target}'; manual review required.",
                        artifactPath));
                }

                // Do not return early: JSON Schema 2020-12 / OpenAPI 3.1 allow sibling constraints beside $ref.
            }
            else
            {
                JsonElement resolvedBase = baseSchema;
                JsonElement resolvedHead = headSchema;
                bool baseResolved = true;
                bool headResolved = true;
                if (baseHasRef && !TryResolveLocalRef(baseRoot, baseRef!, out resolvedBase))
                {
                    baseResolved = false;
                    changes.Add(new ContractDiffChange(
                        family,
                        ContractChangeSeverity.ReviewRequired,
                        "unresolved_ref",
                        target,
                        $"Unresolved or external base $ref '{baseRef}' at '{target}'; manual review required.",
                        artifactPath));
                }

                if (headHasRef && !TryResolveLocalRef(headRoot, headRef!, out resolvedHead))
                {
                    headResolved = false;
                    changes.Add(new ContractDiffChange(
                        family,
                        ContractChangeSeverity.ReviewRequired,
                        "unresolved_ref",
                        target,
                        $"Unresolved or external head $ref '{headRef}' at '{target}'; manual review required.",
                        artifactPath));
                }

                if (baseResolved && headResolved)
                {
                    string cycleKey = $"{baseRef ?? "<inline>"}->{headRef ?? "<inline>"}";
                    if (visitedRefs.Add(cycleKey))
                    {
                        DiffSchemaCore(family, artifactPath, target, baseRoot, headRoot, resolvedBase, resolvedHead, changes, depth + 1, visitedRefs);
                        visitedRefs.Remove(cycleKey);
                    }
                }

                if (baseHasRef != headHasRef)
                {
                    JsonElement refNode = baseHasRef ? baseSchema : headSchema;
                    if (HasConstraintSiblingsBesideRef(refNode))
                    {
                        changes.Add(new ContractDiffChange(
                            family,
                            ContractChangeSeverity.ReviewRequired,
                            "schema_review_required",
                            target,
                            $"Sibling schema constraints beside $ref changed across inline/reference refactor at '{target}'; manual review required.",
                            artifactPath));
                    }

                    return;
                }

                // Both sides carry $ref (different targets): fall through to compare any sibling constraints on the wrapper nodes.
            }
        }

        DiffTypes(family, artifactPath, target, baseSchema, headSchema, changes);
        DiffConstAndEnum(family, artifactPath, target, baseSchema, headSchema, changes);
        DiffRequiredAndProperties(family, artifactPath, target, baseRoot, headRoot, baseSchema, headSchema, changes, depth, visitedRefs);

        if (baseSchema.TryGetProperty("items", out JsonElement baseItems) &&
            headSchema.TryGetProperty("items", out JsonElement headItems))
        {
            DiffSchemaCore(family, artifactPath, $"{target}[]", baseRoot, headRoot, baseItems, headItems, changes, depth + 1, visitedRefs);
        }
        else if (baseSchema.TryGetProperty("items", out _) != headSchema.TryGetProperty("items", out _))
        {
            changes.Add(new ContractDiffChange(
                family,
                ContractChangeSeverity.ReviewRequired,
                "schema_review_required",
                $"{target}[]",
                $"Array item schema presence changed at '{target}'; manual review required.",
                artifactPath));
        }

        DiffUnhandledKeywords(family, artifactPath, target, baseSchema, headSchema, changes);
    }

    private static void DiffTypes(string family, string artifactPath, string target, JsonElement baseSchema, JsonElement headSchema, List<ContractDiffChange> changes)
    {
        HashSet<string> baseTypes = ReadTypeSet(baseSchema);
        HashSet<string> headTypes = ReadTypeSet(headSchema);
        if (baseTypes.SetEquals(headTypes))
        {
            return;
        }

        string[] removed = baseTypes.Except(headTypes, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (removed.Length > 0)
        {
            changes.Add(new ContractDiffChange(
                family,
                ContractChangeSeverity.Breaking,
                "schema_type_narrowed",
                target,
                $"Schema type narrowed at '{target}' (removed: {string.Join(", ", removed)}).",
                artifactPath));
        }
        else
        {
            string[] added = headTypes.Except(baseTypes, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            changes.Add(new ContractDiffChange(
                family,
                ContractChangeSeverity.ReviewRequired,
                "schema_type_changed",
                target,
                $"Schema type widened at '{target}' (added: {string.Join(", ", added)}); verify consumer compatibility.",
                artifactPath));
        }
    }

    private static void DiffConstAndEnum(string family, string artifactPath, string target, JsonElement baseSchema, JsonElement headSchema, List<ContractDiffChange> changes)
    {
        bool baseHasConst = baseSchema.TryGetProperty("const", out JsonElement baseConst);
        bool headHasConst = headSchema.TryGetProperty("const", out JsonElement headConst);
        if (baseHasConst || headHasConst)
        {
            string baseText = baseHasConst ? CanonicalJson(baseConst) : "<none>";
            string headText = headHasConst ? CanonicalJson(headConst) : "<none>";
            if (!string.Equals(baseText, headText, StringComparison.Ordinal))
            {
                changes.Add(new ContractDiffChange(
                    family,
                    ContractChangeSeverity.Breaking,
                    "schema_const_changed",
                    target,
                    $"Constant contract value changed at '{target}'.",
                    artifactPath));
            }
        }

        HashSet<string> baseEnum = ReadEnumSet(baseSchema);
        HashSet<string> headEnum = ReadEnumSet(headSchema);
        if (!baseEnum.SetEquals(headEnum))
        {
            if (baseEnum.Except(headEnum, StringComparer.Ordinal).Any())
            {
                changes.Add(new ContractDiffChange(
                    family,
                    ContractChangeSeverity.Breaking,
                    "schema_enum_narrowed",
                    target,
                    $"Removed one or more enum values at '{target}'.",
                    artifactPath));
            }
            else if (headEnum.Except(baseEnum, StringComparer.Ordinal).Any())
            {
                changes.Add(new ContractDiffChange(
                    family,
                    ContractChangeSeverity.ReviewRequired,
                    "schema_enum_expanded",
                    target,
                    $"Added enum values at '{target}'; verify closed consumer switches.",
                    artifactPath));
            }
        }
    }

    private static void DiffRequiredAndProperties(
        string family,
        string artifactPath,
        string target,
        JsonElement baseRoot,
        JsonElement headRoot,
        JsonElement baseSchema,
        JsonElement headSchema,
        List<ContractDiffChange> changes,
        int depth,
        HashSet<string> visitedRefs)
    {
        HashSet<string> baseRequired = ReadStringArray(baseSchema, "required");
        HashSet<string> headRequired = ReadStringArray(headSchema, "required");
        Dictionary<string, JsonElement> baseProps = ReadProperties(baseSchema);
        Dictionary<string, JsonElement> headProps = ReadProperties(headSchema);

        bool isHalLinksObject = target.EndsWith("._links", StringComparison.Ordinal) ||
            string.Equals(target, "_links", StringComparison.Ordinal);

        foreach (string propName in baseProps.Keys.Union(headProps.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseProps.TryGetValue(propName, out JsonElement baseProp);
            bool hasHead = headProps.TryGetValue(propName, out JsonElement headProp);
            string propTarget = $"{target}.{propName}";

            if (hasBase && !hasHead)
            {
                changes.Add(new ContractDiffChange(
                    family,
                    ContractChangeSeverity.Breaking,
                    isHalLinksObject ? "hal_link_removed" : "property_removed",
                    propTarget,
                    isHalLinksObject
                        ? $"Removed HAL link relation '{propName}' from '{target}'."
                        : $"Removed property '{propName}' from '{target}'.",
                    artifactPath));
                continue;
            }

            if (!hasBase && hasHead)
            {
                bool required = headRequired.Contains(propName);
                changes.Add(new ContractDiffChange(
                    family,
                    required ? ContractChangeSeverity.Breaking : ContractChangeSeverity.Additive,
                    isHalLinksObject
                        ? "hal_link_added"
                        : required ? "required_property_added" : "optional_property_added",
                    propTarget,
                    isHalLinksObject
                        ? $"Added HAL link relation '{propName}' to '{target}'."
                        : required
                            ? $"Added required property '{propName}' to '{target}'."
                            : $"Added optional property '{propName}' to '{target}'.",
                    artifactPath));
                continue;
            }

            if (!baseRequired.Contains(propName) && headRequired.Contains(propName))
            {
                changes.Add(new ContractDiffChange(
                    family,
                    ContractChangeSeverity.Breaking,
                    "required_property_added",
                    propTarget,
                    $"Property '{propName}' on '{target}' became required.",
                    artifactPath));
            }
            else if (baseRequired.Contains(propName) && !headRequired.Contains(propName))
            {
                changes.Add(new ContractDiffChange(
                    family,
                    ContractChangeSeverity.ReviewRequired,
                    "required_property_removed",
                    propTarget,
                    $"Property '{propName}' on '{target}' is no longer marked required; verify consumer assumptions.",
                    artifactPath));
            }

            DiffSchemaCore(family, artifactPath, propTarget, baseRoot, headRoot, baseProp, headProp, changes, depth + 1, visitedRefs);
        }

        // Catch required entries not present in properties map.
        foreach (string newlyRequired in headRequired.Except(baseRequired, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!headProps.ContainsKey(newlyRequired) && !baseProps.ContainsKey(newlyRequired))
            {
                changes.Add(new ContractDiffChange(
                    family,
                    ContractChangeSeverity.Breaking,
                    "required_property_added",
                    $"{target}.{newlyRequired}",
                    $"Added required field '{newlyRequired}' to '{target}'.",
                    artifactPath));
            }
        }
    }

    private static void DiffUnhandledKeywords(string family, string artifactPath, string target, JsonElement baseSchema, JsonElement headSchema, List<ContractDiffChange> changes)
    {
        var baseExtra = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty prop in baseSchema.EnumerateObject())
        {
            if (!HandledKeywords.Contains(prop.Name))
            {
                baseExtra[prop.Name] = CanonicalJson(prop.Value);
            }
        }

        var headExtra = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty prop in headSchema.EnumerateObject())
        {
            if (!HandledKeywords.Contains(prop.Name))
            {
                headExtra[prop.Name] = CanonicalJson(prop.Value);
            }
        }

        string[] changedKeys = baseExtra.Keys.Union(headExtra.Keys, StringComparer.Ordinal)
            .Where(key =>
            {
                baseExtra.TryGetValue(key, out string? b);
                headExtra.TryGetValue(key, out string? h);
                return !string.Equals(b, h, StringComparison.Ordinal);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (changedKeys.Length > 0)
        {
            changes.Add(new ContractDiffChange(
                family,
                ContractChangeSeverity.ReviewRequired,
                "schema_review_required",
                target,
                $"Structural schema constraints or defaults changed at '{target}' ({string.Join(", ", changedKeys)}); manual review required.",
                artifactPath));
        }
    }

    private static bool HasConstraintSiblingsBesideRef(JsonElement schema)
    {
        foreach (JsonProperty prop in schema.EnumerateObject())
        {
            if (!RefNonConstraintMetadataKeywords.Contains(prop.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveLocalRef(JsonElement root, string reference, out JsonElement resolved)
    {
        resolved = default;
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        JsonElement current = root;
        foreach (string rawSegment in reference[2..].Split('/'))
        {
            string segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out JsonElement next))
            {
                return false;
            }

            current = next;
        }

        resolved = current;
        return true;
    }

    private static HashSet<string> ReadTypeSet(JsonElement schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!schema.TryGetProperty("type", out JsonElement typeProp))
        {
            return set;
        }

        if (typeProp.ValueKind == JsonValueKind.String && typeProp.GetString() is { Length: > 0 } single)
        {
            set.Add(single);
        }
        else if (typeProp.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in typeProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                {
                    set.Add(value);
                }
            }
        }

        return set;
    }

    private static HashSet<string> ReadEnumSet(JsonElement schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("enum", out JsonElement enumProp) && enumProp.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in enumProp.EnumerateArray())
            {
                set.Add(CanonicalJson(item));
            }
        }

        return set;
    }

    private static HashSet<string> ReadStringArray(JsonElement schema, string key)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty(key, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                {
                    set.Add(value);
                }
            }
        }

        return set;
    }

    private static Dictionary<string, JsonElement> ReadProperties(JsonElement schema)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (schema.TryGetProperty("properties", out JsonElement props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in props.EnumerateObject())
            {
                map[prop.Name] = prop.Value;
            }
        }

        return map;
    }

    private static bool TryGetStringProperty(JsonElement node, string property, out string? value)
    {
        if (node.TryGetProperty(property, out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return !string.IsNullOrEmpty(value);
        }

        value = null;
        return false;
    }

    internal static string CanonicalJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(
                ",",
                element.EnumerateObject()
                    .OrderBy(prop => prop.Name, StringComparer.Ordinal)
                    .Select(prop => $"{JsonSerializer.Serialize(prop.Name)}:{CanonicalJson(prop.Value)}")) + "}",
            JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(CanonicalJson)) + "]",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "null",
        };
    }
}
