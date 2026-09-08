using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

internal static class HalOpenApiSchemaMutator
{
    public static void FlattenDtoIntoHalResource(
        OpenApiSchema halSchema,
        OpenApiSchema dtoSchema,
        bool copyRequired = true,
        IOpenApiSchema? linkValueSchema = null)
    {
        EnsureObject(halSchema);

        var properties = halSchema.Properties ??= new Dictionary<string, IOpenApiSchema>();
        properties.Remove("data");
        halSchema.Required?.Remove("data");

        CopyProperties(halSchema, dtoSchema);

        if (copyRequired)
        {
            CopyRequired(halSchema, dtoSchema);
        }

        AddHalLinksProperty(halSchema, linkValueSchema);
        AddHalEmbeddedProperty(halSchema);
    }

    public static void EnsureObject(OpenApiSchema schema)
    {
        schema.Type = JsonSchemaType.Object;
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
    }

    public static void CopyProperties(OpenApiSchema target, OpenApiSchema source)
    {
        target.Properties ??= new Dictionary<string, IOpenApiSchema>();

        if (source.Properties == null)
        {
            return;
        }

        foreach (var property in source.Properties)
        {
            target.Properties.TryAdd(property.Key, property.Value);
        }
    }

    public static void CopyRequired(OpenApiSchema target, OpenApiSchema source)
    {
        if (source.Required == null)
        {
            return;
        }

        target.Required ??= new HashSet<string>();
        foreach (var required in source.Required)
        {
            target.Required.Add(required);
        }
    }

    public static void AddHalLinksProperty(OpenApiSchema schema, IOpenApiSchema? linkValueSchema = null)
    {
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();

        schema.Properties.TryAdd("_links", new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalPropertiesAllowed = true,
            Description = "HAL hypermedia links",
            AdditionalProperties = linkValueSchema ?? CreateInlineHalLinkValueSchema()
        });
    }

    public static void AddHalEmbeddedProperty(OpenApiSchema schema)
    {
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();

        schema.Properties.TryAdd("_embedded", new OpenApiSchema
        {
            Type = JsonSchemaType.Object | JsonSchemaType.Null,
            Description = "Embedded related resources",
            AdditionalPropertiesAllowed = true
        });
    }

    public static void EnsureEmbeddedItemsArrayType(OpenApiSchema schema, IOpenApiSchema itemSchema)
    {
        EnsureObject(schema);

        var properties = schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
        if (!properties.TryGetValue("items", out var itemsProperty)
            || itemsProperty is not OpenApiSchema itemsSchema)
        {
            itemsSchema = new OpenApiSchema();
            properties["items"] = itemsSchema;
        }

        itemsSchema.Type = JsonSchemaType.Array;
        itemsSchema.Items = itemSchema;
    }

    public static void SetEmbeddedReference(
        OpenApiSchema schema,
        IOpenApiSchema embeddedSchema)
    {
        EnsureObject(schema);
        schema.Properties!["_embedded"] = embeddedSchema;
    }

    private static OpenApiSchema CreateInlineHalLinkValueSchema()
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["href"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Link URL" },
                ["method"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "HTTP method" },
                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Description = "Link title" }
            }
        };
    }
}
