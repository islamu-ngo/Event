using System.Text.Json.Nodes;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

/// <summary>Describes factory-preserving JSON shapes identically on both documentation surfaces.</summary>
public sealed class ModerationIntentSchemaTransformer :
    IOpenApiSchemaTransformer, IOpenApiDocumentTransformer, ISchemaFilter
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (IsIntent(context.JsonTypeInfo.Type))
        {
            // The document phase adds recursive wire references after the SDK finishes its CLR-property traversal.
            schema.Type = JsonSchemaType.Object;
            schema.Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            schema.Required = new HashSet<string>(StringComparer.Ordinal);
        }
        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        foreach (var type in new[]
        {
            typeof(AuthorityScope), typeof(SubjectReference),
            typeof(ModerationRuleReference), typeof(ModerationProposal), typeof(ModerationClassificationMaterial)
        })
        {
            var schema = new OpenApiSchema();
            ApplyWireShape(schema, type, dependency => new OpenApiSchemaReference(dependency.Name, document));
            document.Components.Schemas[type.Name] = schema;
        }
        return Task.CompletedTask;
    }

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete || !IsIntent(context.Type)) return;
        ApplyWireShape(concrete, context.Type,
            dependency => context.SchemaGenerator.GenerateSchema(dependency, context.SchemaRepository));
    }

    internal static bool IsIntent(Type type) =>
        type == typeof(AuthorityScope) || type == typeof(SubjectReference)
        || type == typeof(ModerationRuleReference) || type == typeof(ModerationProposal)
        || type == typeof(ModerationClassificationMaterial);

    private static void ApplyWireShape(OpenApiSchema schema, Type type, Func<Type, IOpenApiSchema> reference)
    {
        schema.Type = JsonSchemaType.Object;
        schema.AdditionalPropertiesAllowed = false;
        schema.ReadOnly = false;
        schema.Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        if (type == typeof(AuthorityScope))
        {
            schema.Required = new HashSet<string>(["kind", "id"], StringComparer.Ordinal);
            schema.Properties["kind"] = Strings(Enum.GetNames<ModerationScopeKind>());
            schema.Properties["id"] = Identifier();
            schema.Properties["parent"] = new OpenApiSchema
            {
                OneOf = [reference(typeof(AuthorityScope)), new OpenApiSchema { Type = JsonSchemaType.Null }]
            };
        }
        else if (type == typeof(SubjectReference))
        {
            schema.Required = new HashSet<string>(["kind", "id", "origin"], StringComparer.Ordinal);
            schema.Properties["kind"] = Strings(Enum.GetNames<ModerationSubjectKind>());
            schema.Properties["id"] = Identifier();
            schema.Properties["origin"] = reference(typeof(AuthorityScope));
        }
        else if (type == typeof(ModerationRuleReference))
        {
            schema.Required = new HashSet<string>(["ownerId", "code", "revision"], StringComparer.Ordinal);
            schema.Properties["ownerId"] = Identifier();
            schema.Properties["code"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String, MinLength = 1, MaxLength = 100, Pattern = "^[A-Za-z0-9._-]+$"
            };
            schema.Properties["revision"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" };
        }
        else if (type == typeof(ModerationClassificationMaterial))
        {
            schema.Required = new HashSet<string>(["eventId", "eventStamp"], StringComparer.Ordinal);
            schema.Properties["eventId"] = Identifier();
            schema.Properties["eventStamp"] = Identifier();
            foreach (var name in new[] { "sessionId", "sessionStamp", "resourceId", "resourceStamp" })
                schema.Properties[name] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Format = "uuid" };
        }
        else
        {
            schema.Required = new HashSet<string>(
                ["caseId", "rule", "capabilities", "startsAt", "requiredApprovals"], StringComparer.Ordinal);
            schema.Properties["caseId"] = Identifier();
            schema.Properties["rule"] = reference(typeof(ModerationRuleReference));
            schema.Properties["capabilities"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array, MinItems = 1,
                Items = Strings(Enum.GetValues<ModerationCapability>()
                    .Where(capability => !EffectiveRestrictions.IsProtectedRemedy(capability))
                    .Select(capability => capability.ToString()))
            };
            schema.Properties["startsAt"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" };
            schema.Properties["endsAt"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null, Format = "date-time"
            };
            schema.Properties["reviewAt"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null, Format = "date-time"
            };
            schema.Properties["requiredApprovals"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer, Enum = [JsonValue.Create(1)!, JsonValue.Create(2)!]
            };
        }
    }

    private static OpenApiSchema Identifier() => new() { Type = JsonSchemaType.String, Format = "uuid" };

    private static OpenApiSchema Strings(IEnumerable<string> values) => new()
    {
        Type = JsonSchemaType.String, Enum = values.Select(value => (JsonNode)JsonValue.Create(value)!).ToList()
    };
}
