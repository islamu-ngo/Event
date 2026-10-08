namespace ISLAMU.Event.DefinitionExporter;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;

internal sealed class DefinitionBuilder(HostArtifacts host)
{
    internal const string FamilyId = "a4bcfaa6-9979-43e5-b1f5-00d552618cdf";
    internal static readonly string[] ArtifactIds =
        ["environment", "configuration-manifest", "tenant-package", "operator-identity", "legal-drafts"];
    private readonly JsonArray _fields = [];
    private readonly JsonArray _artifacts = [];
    private readonly Dictionary<string, JsonObject> _answers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectors = new(StringComparer.Ordinal);
    private int _fieldNumber;

    internal IReadOnlyDictionary<string, JsonObject> Answers => _answers;

    internal JsonObject Build(JsonElement catalogue, IReadOnlyDictionary<string, byte[]> sources, long revision, string version,
        int trustedIssuerCount = 1)
    {
        JsonObject operation = Field("operation", "enum", "Artifact to prepare", "public");
        operation["required"] = true;
        operation["choices"] = JsonSerializer.SerializeToNode(ArtifactIds);
        _fields.Add(operation);
        AddEnvironment(catalogue, trustedIssuerCount);
        foreach (string artifact in ArtifactIds.Where(id => id != "environment"))
        {
            byte[] validated = host.Validate(artifact, sources[artifact]);
            using JsonDocument input = JsonDocument.Parse(validated);
            var answers = new JsonObject { ["operation"] = artifact };
            _answers.Add(artifact, answers);
            JsonObject projection = Project(input.RootElement, artifact, "$", answers);
            projection["when"] = Selected(artifact);
            _artifacts.Add(new JsonObject
            {
                ["id"] = artifact, ["format"] = "json", ["fileName"] = artifact + ".json",
                ["applySemantics"] = "generationOnly", ["restart"] = "none", ["projection"] = projection
            });
        }
        if (_fields.Count > 1024)
            throw new InvalidDataException("definition-field-limit");
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["familyId"] = FamilyId, ["revision"] = revision,
            ["applicationId"] = "islamu.event", ["applicationVersions"] = new JsonArray(version),
            ["publisher"] = "ISLAMU", ["title"] = "ISLAMU Event offline configuration",
            ["entryPoints"] = JsonSerializer.SerializeToNode(ArtifactIds), ["locales"] = new JsonArray("en"),
            ["fields"] = _fields, ["artifacts"] = _artifacts
        };
    }

    private void AddEnvironment(JsonElement catalogue, int trustedIssuerCount)
    {
        JsonElement definitions = catalogue.GetProperty("definitions");
        var metadata = definitions.EnumerateArray().ToDictionary(item => item.GetProperty("key").GetString()!, StringComparer.Ordinal);
        foreach (SecretDefinition secret in SecretDefinitionRegistry.All.Values)
            if (!metadata.TryGetValue(secret.DefaultEnvironmentVariableName, out JsonElement item)
                || item.GetProperty("sensitivity").GetString() != "secret")
                throw new InvalidDataException("environment-secret-registry-drift");
        JsonObject members = [];
        var indexedDefinitions = definitions.EnumerateArray().Select(item => item.Clone()).ToList();
        JsonElement issuer = metadata["IDENTITYCORRELATION__TRUSTEDISSUERS__0"];
        for (int index = 1; index < trustedIssuerCount; index++)
        {
            JsonObject indexed = JsonNode.Parse(issuer.GetRawText())!.AsObject();
            indexed["key"] = "IDENTITYCORRELATION__TRUSTEDISSUERS__" + index.ToString(CultureInfo.InvariantCulture);
            using JsonDocument document = JsonDocument.Parse(indexed.ToJsonString());
            indexedDefinitions.Add(document.RootElement.Clone());
        }
        foreach (JsonElement item in indexedDefinitions.OrderBy(item => item.GetProperty("key").GetString(), StringComparer.Ordinal))
        {
            string key = item.GetProperty("key").GetString()!;
            string validator = item.GetProperty("validatorId").GetString()!;
            string type = validator switch
            {
                "boolean" => "boolean",
                "port-number" or "positive-integer" => "integer",
                _ => "string"
            };
            string id = "env." + key;
            JsonObject field = Field(id, type, key, item.GetProperty("sensitivity").GetString()!);
            field["group"] = item.GetProperty("category").GetString();
            field["disclosure"] = item.GetProperty("generation").TryGetProperty("dotenvOrder", out _) ? "guided" : "advanced";
            field["required"] = item.GetProperty("requirement").GetString() == "required";
            field["activeWhen"] = All(Selected("environment"), Activation(item.GetProperty("activation")));
            field["consequence"] = "Host restart: " + item.GetProperty("restartBehavior").GetString()
                + "; startup-owned: " + ((item.GetProperty("generation").GetProperty("surfaces").GetInt32() & 4) != 0
                    ? "true" : "false");
            field["help"] = "Host validator: " + validator
                + ". Host validation remains required.";
            if (item.GetProperty("sensitivity").GetString() == "public"
                && item.TryGetProperty("safeDefault", out JsonElement approvedDefault))
            {
                string value = approvedDefault.GetString()!;
                field["default"] = type switch
                {
                    "boolean" => JsonValue.Create(bool.Parse(value)),
                    "integer" => JsonValue.Create(long.Parse(value, CultureInfo.InvariantCulture)),
                    _ => JsonValue.Create(value)
                };
            }
            if (validator is "port-number" or "positive-integer" or "absolute-uri" or "email-address")
                field["validator"] = validator;
            _fields.Add(field);
            members[key] = Copy(id);
        }
        // The profile declares a finite set of canonical indices, never wildcard authority.
        _artifacts.Add(new JsonObject
        {
            ["id"] = "environment", ["format"] = "dotenv", ["fileName"] = "event.env",
            ["applySemantics"] = "generationOnly", ["restart"] = "deployment",
            ["projection"] = new JsonObject { ["op"] = "object", ["members"] = members, ["when"] = Selected("environment") }
        });
        _answers.Add("environment", new JsonObject
        {
            ["operation"] = "environment", ["select.feature.deployment-config"] = true,
            ["env.API_HTTP_PORT"] = 8080, ["env.UI_HTTP_PORT"] = 8081
        });
    }

    private JsonObject Activation(JsonElement activation)
    {
        string kind = activation.GetProperty("kind").GetString()!;
        if (kind is "all" or "any" or "not")
        {
            var operands = new JsonArray();
            foreach (JsonElement child in activation.GetProperty("operands").EnumerateArray())
                operands.Add(Activation(child));
            return new JsonObject { ["op"] = kind, ["operands"] = operands };
        }
        if (kind is not ("feature" or "capability" or "topology" or "provider"))
            throw new InvalidDataException("environment-activation-unsupported");
        string id = "select." + kind + "." + activation.GetProperty("identifier").GetString();
        if (_selectors.Add(id))
        {
            JsonObject selector = Field(id, "boolean", id, "public");
            selector["default"] = false;
            selector["activeWhen"] = Selected("environment");
            selector["help"] = "Explicit offline selection, not a detected deployment capability.";
            _fields.Add(selector);
        }
        return new JsonObject { ["op"] = "equals", ["field"] = id, ["value"] = true };
    }

    private JsonObject Project(JsonElement value, string artifact, string path, JsonObject answers)
    {
        string leaf = path.Split('.').Last();
        if (value.ValueKind == JsonValueKind.Object && leaf == "settings")
        {
            bool tenant = artifact == "tenant-package" || path.Contains(".tenants.", StringComparison.Ordinal);
            string id = NextId(artifact);
            JsonObject field = Field(id, "object", tenant ? "Tenant settings" : "Instance settings", "sensitive");
            field["properties"] = SettingFields(tenant);
            field["activeWhen"] = Selected(artifact);
            field["required"] = true;
            field["help"] = path + "; only the host portable catalogue is declared.";
            _fields.Add(field);
            answers[id] = JsonNode.Parse(value.GetRawText());
            return Copy(id);
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            JsonObject members = [];
            foreach (JsonProperty property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                members[property.Name] = Project(property.Value, artifact, path + "." + property.Name, answers);
            return new JsonObject { ["op"] = "object", ["members"] = members };
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var items = new JsonArray();
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
                items.Add(Project(item, artifact, path + "." + (index++).ToString(CultureInfo.InvariantCulture), answers));
            return new JsonObject { ["op"] = "list", ["items"] = items };
        }
        bool contractConstant = path is "$.$schema" or "$.apiVersion" or "$.kind" or "$.settingKey"
            || leaf == "kind" && path.Contains(".legalDocuments.", StringComparison.Ordinal)
            || leaf == "schemaVersion" && path.Contains(".documents.", StringComparison.Ordinal);
        if (value.ValueKind == JsonValueKind.Null || contractConstant)
            return new JsonObject { ["op"] = "literal", ["value"] = JsonNode.Parse(value.GetRawText()) };
        string fieldId = NextId(artifact);
        string type = value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number => value.TryGetInt64(out _) ? "integer" : "number",
            JsonValueKind.String => "string",
            _ => throw new InvalidDataException("projection-value-unsupported")
        };
        JsonObject scalar = Field(fieldId, type, leaf, "sensitive");
        scalar["required"] = true;
        scalar["activeWhen"] = Selected(artifact);
        scalar["help"] = path;
        if (leaf == "lifecycleIntent")
        {
            scalar["type"] = "enum";
            scalar["choices"] = new JsonArray("Draft", "ReviewRequired");
        }
        if (leaf == "audience")
        {
            scalar["type"] = "enum";
            scalar["choices"] = JsonSerializer.SerializeToNode(Enum.GetNames<LegalDocumentAudience>());
        }
        if (leaf == "sourceKind" && path.Contains(".templateProvenance.", StringComparison.Ordinal))
        {
            scalar["type"] = "enum";
            scalar["choices"] = new JsonArray("ProjectOwned", "ApprovedFoss");
        }
        if (leaf is "title" or "summary" or "displayName" or "languageTag")
        {
            scalar["minLength"] = 1;
            scalar["maxLength"] = leaf switch { "title" => 200, "languageTag" => 35, _ => 500 };
        }
        if (artifact == "operator-identity")
            scalar["consequence"] = "Prepare integrity through Event; edits require a fresh content digest. Revision is server-owned.";
        _fields.Add(scalar);
        answers[fieldId] = JsonNode.Parse(value.GetRawText());
        return Copy(fieldId);
    }

    private JsonArray SettingFields(bool tenant)
    {
        JsonArray fields = [];
        foreach (JsonProperty entry in host.SettingsSchema(tenant).EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            SettingDefinition definition = SettingRegistry.Get(entry.Name)
                ?? throw new InvalidDataException("portable-setting-registry-drift");
            if (definition.IsSensitive)
                throw new InvalidDataException("portable-setting-sensitive");
            string type = entry.Value.GetProperty("type").GetString()!;
            JsonObject field = Field(entry.Name, type == "array" ? "list" : type, entry.Name, "public");
            field["group"] = definition.Category;
            field["help"] = definition.Description;
            if (type == "array")
            {
                field["itemType"] = entry.Value.GetProperty("items").GetProperty("type").GetString();
                field["maxItems"] = 4096;
                field["consequence"] = "Per-item choices and business rules are validated by Event.";
            }
            if (entry.Value.TryGetProperty("enum", out JsonElement choices))
            {
                field["type"] = "enum";
                field["choices"] = JsonNode.Parse(choices.GetRawText());
            }
            foreach (string constraint in new[] { "minLength", "maxLength", "minimum", "maximum" })
                if (entry.Value.TryGetProperty(constraint, out JsonElement bound))
                    field[constraint] = JsonNode.Parse(bound.GetRawText());
            fields.Add(field);
        }
        return fields;
    }

    private string NextId(string artifact) => artifact + ".f" + (_fieldNumber++).ToString("D4", CultureInfo.InvariantCulture);
    private static JsonObject Field(string id, string type, string title, string sensitivity) => new()
    {
        ["id"] = id, ["type"] = type, ["title"] = title,
        ["sensitivity"] = sensitivity, ["disclosure"] = "guided"
    };
    private static JsonObject Copy(string field) => new() { ["op"] = "copy", ["field"] = field };
    private static JsonObject Selected(string artifact) => new() { ["op"] = "equals", ["field"] = "operation", ["value"] = artifact };
    private static JsonObject All(JsonObject first, JsonObject second) => new() { ["op"] = "all", ["operands"] = new JsonArray(first, second) };
}
