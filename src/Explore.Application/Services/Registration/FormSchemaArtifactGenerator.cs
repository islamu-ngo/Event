using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Services.Registration;

public sealed class FormSchemaArtifactGenerator : IFormSchemaArtifactGenerator
{
    public const string SchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public FormSchemaArtifactBundle Generate(RegistrationFormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        RegistrationFormSection[] sections =
        [
            .. version.Sections.Where(section => !section.IsDeleted)
                .OrderBy(section => section.Ordinal)
                .ThenBy(section => section.Title, StringComparer.Ordinal)
        ];
        RegistrationFormField[] fields =
        [
            .. sections.SelectMany(section => section.Fields.Where(field => !field.IsDeleted)
                .OrderBy(field => field.Ordinal)
                .ThenBy(FieldKey, StringComparer.Ordinal))
        ];

        JsonObject data = DataArtifact(fields);
        JsonObject ui = UiArtifact(sections);
        JsonObject logic = LogicArtifact(version.Rules);
        JsonObject mapping = new()
        {
            ["fields"] = new JsonArray(),
            ["options"] = new JsonArray()
        };
        JsonObject bundle = new()
        {
            ["$schema"] = SchemaDialect,
            ["versionId"] = version.Id.ToString("D", CultureInfo.InvariantCulture),
            ["version"] = version.Version,
            ["languageTag"] = version.LanguageTag,
            ["data"] = data.DeepClone(),
            ["ui"] = ui.DeepClone(),
            ["logic"] = logic.DeepClone(),
            ["mapping"] = mapping.DeepClone()
        };
        string canonicalBundle = Serialize(bundle);

        return new FormSchemaArtifactBundle(
            Serialize(data),
            Serialize(ui),
            Serialize(logic),
            Serialize(mapping),
            canonicalBundle,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalBundle))));
    }

    private static JsonObject DataArtifact(IEnumerable<RegistrationFormField> fields)
    {
        JsonObject properties = [];
        JsonArray required = [];
        foreach (RegistrationFormField field in fields.OrderBy(FieldKey, StringComparer.Ordinal))
        {
            string key = FieldKey(field);
            properties.Add(key, FieldSchema(field));
            if (field.IsRequired)
            {
                required.Add(key);
            }
        }

        return new JsonObject
        {
            ["$schema"] = SchemaDialect,
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private static JsonObject FieldSchema(RegistrationFormField field)
    {
        RegistrationFieldTypeEnum fieldType = (RegistrationFieldTypeEnum)field.FieldTypeId;
        JsonObject schema = fieldType switch
        {
            RegistrationFieldTypeEnum.Integer or RegistrationFieldTypeEnum.Rating => new JsonObject { ["type"] = "integer" },
            RegistrationFieldTypeEnum.Decimal => new JsonObject { ["type"] = "number" },
            RegistrationFieldTypeEnum.Boolean or RegistrationFieldTypeEnum.Consent => new JsonObject { ["type"] = "boolean" },
            RegistrationFieldTypeEnum.MultipleChoice => new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" }
            },
            RegistrationFieldTypeEnum.OpaqueExternal => new JsonObject { ["type"] = "object" },
            _ => new JsonObject { ["type"] = "string" }
        };
        string? format = fieldType switch
        {
            RegistrationFieldTypeEnum.Date => "date",
            RegistrationFieldTypeEnum.Time => "time",
            RegistrationFieldTypeEnum.Instant => "date-time",
            RegistrationFieldTypeEnum.Email => "email",
            RegistrationFieldTypeEnum.Url => "uri",
            _ => null
        };
        if (format is not null)
        {
            schema["format"] = format;
        }

        AddIfPresent(schema, "minLength", field.MinLength);
        AddIfPresent(schema, "maxLength", field.MaxLength);
        AddIfPresent(schema, "pattern", field.RegexPattern);
        AddIfPresent(schema, "minimum", field.MinNumber);
        AddIfPresent(schema, "maximum", field.MaxNumber);
        AddIfPresent(schema, "formatMinimum", InvariantDateTime(field.MinDateTime));
        AddIfPresent(schema, "formatMaximum", InvariantDateTime(field.MaxDateTime));
        AddIfPresent(schema, "x-allowedUrlSchemes", field.AllowedUrlSchemes);
        schema["x-fieldType"] = fieldType.ToString();
        schema["x-isMulti"] = field.IsMulti;
        schema["x-retentionPolicyId"] = field.RetentionPolicyId;
        schema["x-organizerVisibility"] = ((RegistrationOrganizerVisibilityEnum)field.OrganizerVisibilityId).ToString();
        schema["x-requiresExplicitConsent"] = field.RequiresExplicitConsent;
        schema["x-providerTransferAllowed"] = field.IsProviderTransferAllowed;
        schema["x-isExportable"] = field.IsExportable;
        AddIfPresent(schema, "x-exportPurposeCode", field.ExportPurposeCode);
        AddIfPresent(schema, "x-consentPurposeCode", field.ConsentPurposeCode);
        AddIfPresent(schema, "x-consentTextVersion", field.ConsentTextVersion);
        AddIfPresent(schema, "x-consentText", field.ConsentText);
        schema["x-options"] = Options(field.Options);
        return schema;
    }

    private static JsonObject UiArtifact(IEnumerable<RegistrationFormSection> sections)
    {
        JsonArray sectionArray = [];
        foreach (RegistrationFormSection section in sections)
        {
            JsonArray fields = [];
            foreach (RegistrationFormField field in section.Fields.Where(field => !field.IsDeleted)
                         .OrderBy(field => field.Ordinal).ThenBy(FieldKey, StringComparer.Ordinal))
            {
                fields.Add(new JsonObject
                {
                    ["key"] = FieldKey(field),
                    ["ordinal"] = field.Ordinal,
                    ["label"] = field.Label,
                    ["renderer"] = ((RegistrationFieldTypeEnum)field.FieldTypeId).ToString(),
                    ["options"] = Options(field.Options)
                });
            }

            sectionArray.Add(new JsonObject
            {
                ["ordinal"] = section.Ordinal,
                ["title"] = section.Title,
                ["fields"] = fields
            });
        }

        return new JsonObject { ["sections"] = sectionArray };
    }

    private static JsonArray Options(IEnumerable<RegistrationFormFieldOption> options)
    {
        JsonArray values = [];
        foreach (RegistrationFormFieldOption option in options.Where(option => !option.IsDeleted)
                     .OrderBy(option => option.Ordinal).ThenBy(option => option.Key, StringComparer.Ordinal))
        {
            values.Add(new JsonObject
            {
                ["key"] = option.Key,
                ["ordinal"] = option.Ordinal,
                ["label"] = option.Label,
                ["retired"] = option.RetiredAt is not null
            });
        }

        return values;
    }

    private static JsonObject LogicArtifact(IEnumerable<RegistrationFormRule> rules)
    {
        JsonArray values = [];
        foreach (RegistrationFormRule rule in rules.Where(rule => !rule.IsDeleted)
                     .OrderBy(rule => rule.Ordinal)
                     .ThenBy(rule => FieldKey(rule.Target), StringComparer.Ordinal))
        {
            values.Add(new JsonObject
            {
                ["ordinal"] = rule.Ordinal,
                ["target"] = FieldKey(rule.Target),
                ["effect"] = rule.Effect.ToString(),
                ["condition"] = Condition(rule.Condition)
            });
        }

        return new JsonObject { ["rules"] = values };
    }

    private static JsonObject Condition(FormCondition condition) => condition switch
    {
        FormCondition.EqualsCondition value => Leaf("equals", value.Field, ("value", Scalar(value.Value))),
        FormCondition.NotEqualsCondition value => Leaf("notEquals", value.Field, ("value", Scalar(value.Value))),
        FormCondition.InCondition value => Leaf("in", value.Field,
            ("values", new JsonArray([.. value.Values.Select(Scalar)]))),
        FormCondition.ContainsCondition value => Leaf("contains", value.Field, ("value", Scalar(value.Value))),
        FormCondition.ExistsCondition value => Leaf("exists", value.Field),
        FormCondition.CompareCondition value => Leaf("compare", value.Field,
            ("comparison", JsonValue.Create(value.Comparison.ToString())), ("value", Scalar(value.Value))),
        FormCondition.AllCondition value => Group("all", value.Conditions),
        FormCondition.AnyCondition value => Group("any", value.Conditions),
        FormCondition.NotCondition value => new JsonObject
        {
            ["operator"] = "not",
            ["condition"] = Condition(value.Condition)
        },
        _ => throw new ArgumentOutOfRangeException(nameof(condition))
    };

    private static JsonObject Leaf(
        string @operator,
        FormFieldReference field,
        params (string Name, JsonNode? Value)[] properties)
    {
        JsonObject value = new()
        {
            ["operator"] = @operator,
            ["field"] = FieldKey(field)
        };
        foreach ((string name, JsonNode? property) in properties)
        {
            value[name] = property;
        }

        return value;
    }

    private static JsonObject Group(string @operator, IEnumerable<FormCondition> conditions) => new()
    {
        ["operator"] = @operator,
        ["conditions"] = new JsonArray([.. conditions.Select(Condition)])
    };

    private static JsonObject Scalar(FormScalarValue value) => value switch
    {
        FormScalarValue.Null => new JsonObject { ["type"] = "null", ["value"] = null },
        FormScalarValue.Text text => new JsonObject { ["type"] = "text", ["value"] = text.Value },
        FormScalarValue.Boolean boolean => new JsonObject { ["type"] = "boolean", ["value"] = boolean.Value },
        FormScalarValue.Number number => new JsonObject { ["type"] = "number", ["value"] = number.Value },
        FormScalarValue.Date date => new JsonObject
        {
            ["type"] = "date",
            ["value"] = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        },
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static void AddIfPresent(JsonObject target, string name, JsonNode? value)
    {
        if (value is not null)
        {
            target[name] = value;
        }
    }

    private static string? InvariantDateTime(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string FieldKey(RegistrationFormField field) => $"{field.Namespace}/{field.Key}";

    private static string FieldKey(FormFieldReference field) => $"{field.Namespace}/{field.Key}";

    private static string Serialize(JsonNode node) => JsonSerializer.Serialize(node, SerializerOptions);
}
