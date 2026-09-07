// Ensures OpenAPI schema generation includes the inner DTO properties for HalResource<T> and HalCollectionResource<T>.
//
// STATUS: DISABLED - historical native schema-transformer experiment kept for reference only.
// This file is excluded from compilation. If this approach is revived:
// 1. Remove the #if false / #endif directives
// 2. Register with: options.AddSchemaTransformer<HalSchemaTransformer>() in AddOpenApi()
// 3. Keep the document name aligned with the active native endpoint: /openapi/event-api.json
//
// The active native path is HalDtoSchemaTransformer; Swashbuckle uses HalSchemaFilter.

#if false // Disabled until .NET 10 Preview 4+ is available

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Explore.API.OpenApi;

/// <summary>
/// Schema transformer that properly generates OpenAPI schemas for HAL wrapper types.
/// The generic HalResource{T} flattens T's properties to root level during JSON serialization,
/// but the default OpenAPI generator doesn't understand this. This transformer adds T's properties to the schema.
///
/// This is the ASP.NET Core native OpenAPI equivalent of the Swashbuckle ISchemaFilter.
/// It's used with AddOpenApi() instead of AddSwaggerGen().
/// </summary>
public class HalSchemaTransformer : IOpenApiSchemaTransformer
{
    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        // Handle HalResource<T> - flatten Data properties to root
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Explore.Application.Hateoas.HalResource<>))
        {
            var innerType = type.GetGenericArguments().FirstOrDefault();
            if (innerType != null)
            {
                // Generate schema for the inner DTO type
                var dtoSchema = await context.GetOrCreateSchemaAsync(innerType, cancellationToken: cancellationToken);

                // Flatten DTO properties into the HAL resource schema
                await FlattenDtoPropertiesAsync(schema, innerType, dtoSchema, context, cancellationToken);
                AddHalLinksProperty(schema);
                AddHalEmbeddedProperty(schema);
            }
        }

        // Handle HalCollectionResource<T> - ensure proper structure with typed items
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Explore.Application.Hateoas.HalCollectionResource<>))
        {
            var innerType = type.GetGenericArguments().FirstOrDefault();
            if (innerType != null)
            {
                // Ensure _embedded has properly typed items array
                await EnsureCollectionEmbeddedStructureAsync(schema, innerType, context, cancellationToken);
                AddHalLinksProperty(schema);
            }
        }

        // Handle HalCollectionEmbedded<T> - ensure items array has correct type
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Explore.Application.Hateoas.HalCollectionEmbedded<>))
        {
            var innerType = type.GetGenericArguments().FirstOrDefault();
            if (innerType != null)
            {
                await EnsureEmbeddedItemsArrayTypeAsync(schema, innerType, context, cancellationToken);
            }
        }
    }

    private async Task FlattenDtoPropertiesAsync(
        OpenApiSchema schema,
        Type dtoType,
        OpenApiSchema dtoSchema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Ensure the schema is set up as an object
        schema.Type = "object";
        schema.Properties ??= new Dictionary<string, OpenApiSchema>();
        schema.Required ??= new HashSet<string>();

        // Remove the "data" property if it exists - we're flattening it
        // The HalResourceJsonConverter flattens Data properties to root level
        schema.Properties.Remove("data");
        schema.Required.Remove("data");

        // If dtoSchema has properties, copy them to the root level
        if (dtoSchema.Properties != null)
        {
            foreach (var property in dtoSchema.Properties)
            {
                if (!schema.Properties.ContainsKey(property.Key))
                {
                    schema.Properties[property.Key] = property.Value;
                }
            }
        }

        // Copy required fields from the DTO schema
        if (dtoSchema.Required != null)
        {
            foreach (var required in dtoSchema.Required)
            {
                if (!schema.Required.Contains(required))
                {
                    schema.Required.Add(required);
                }
            }
        }

        // If the DTO schema is a reference, we need to resolve it and copy properties
        if (dtoSchema.Reference != null && context.Document != null)
        {
            // The referenced schema should already be in the document's components
            // We add a reference to make NSwag understand the relationship
            // But we also need the properties flattened at this level
            await Task.CompletedTask;
        }
    }

    private void AddHalLinksProperty(OpenApiSchema schema)
    {
        schema.Properties ??= new Dictionary<string, OpenApiSchema>();

        if (!schema.Properties.ContainsKey("_links"))
        {
            schema.Properties["_links"] = new OpenApiSchema
            {
                Type = "object",
                Description = "HAL hypermedia links",
                AdditionalPropertiesAllowed = true,
                AdditionalProperties = new OpenApiSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, OpenApiSchema>
                    {
                        ["href"] = new OpenApiSchema { Type = "string", Description = "Link URL" },
                        ["method"] = new OpenApiSchema { Type = "string", Description = "HTTP method" },
                        ["title"] = new OpenApiSchema { Type = "string", Description = "Link title", Nullable = true }
                    }
                }
            };
        }
    }

    private void AddHalEmbeddedProperty(OpenApiSchema schema)
    {
        schema.Properties ??= new Dictionary<string, OpenApiSchema>();

        if (!schema.Properties.ContainsKey("_embedded"))
        {
            schema.Properties["_embedded"] = new OpenApiSchema
            {
                Type = "object",
                Nullable = true,
                Description = "Embedded related resources",
                AdditionalPropertiesAllowed = true
            };
        }
    }

    private async Task EnsureCollectionEmbeddedStructureAsync(
        OpenApiSchema schema,
        Type itemType,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        schema.Type = "object";
        schema.Properties ??= new Dictionary<string, OpenApiSchema>();

        // Generate schema for HalResource<T> to use as item type
        var halResourceType = typeof(Explore.Application.Hateoas.HalResource<>).MakeGenericType(itemType);
        var itemSchema = await context.GetOrCreateSchemaAsync(halResourceType, cancellationToken: cancellationToken);

        // Create or update _embedded property with properly typed items
        if (!schema.Properties.ContainsKey("_embedded"))
        {
            schema.Properties["_embedded"] = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["items"] = new OpenApiSchema
                    {
                        Type = "array",
                        Items = itemSchema
                    }
                }
            };
        }
        else if (schema.Properties["_embedded"].Properties != null)
        {
            if (schema.Properties["_embedded"].Properties.TryGetValue("items", out var itemsProperty))
            {
                itemsProperty.Type = "array";
                itemsProperty.Items = itemSchema;
            }
        }

        // Add pagination metadata properties
        AddPaginationProperties(schema);
    }

    private void AddPaginationProperties(OpenApiSchema schema)
    {
        schema.Properties ??= new Dictionary<string, OpenApiSchema>();

        if (!schema.Properties.ContainsKey("page"))
        {
            schema.Properties["page"] = new OpenApiSchema { Type = "integer", Description = "Current page number" };
        }
        if (!schema.Properties.ContainsKey("pageSize"))
        {
            schema.Properties["pageSize"] = new OpenApiSchema { Type = "integer", Description = "Number of items per page" };
        }
        if (!schema.Properties.ContainsKey("totalCount"))
        {
            schema.Properties["totalCount"] = new OpenApiSchema { Type = "integer", Description = "Total number of items" };
        }
        if (!schema.Properties.ContainsKey("totalPages"))
        {
            schema.Properties["totalPages"] = new OpenApiSchema { Type = "integer", Description = "Total number of pages" };
        }
    }

    private async Task EnsureEmbeddedItemsArrayTypeAsync(
        OpenApiSchema schema,
        Type itemType,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        schema.Type = "object";
        schema.Properties ??= new Dictionary<string, OpenApiSchema>();

        // Generate schema reference for the item type wrapped in HalResource<T>
        var halResourceType = typeof(Explore.Application.Hateoas.HalResource<>).MakeGenericType(itemType);
        var itemSchema = await context.GetOrCreateSchemaAsync(halResourceType, cancellationToken: cancellationToken);

        if (schema.Properties.TryGetValue("items", out var itemsProperty))
        {
            // Ensure items is typed as an array with proper item reference
            itemsProperty.Type = "array";
            itemsProperty.Items = itemSchema;
        }
        else
        {
            // Add items property if it doesn't exist
            schema.Properties["items"] = new OpenApiSchema
            {
                Type = "array",
                Items = itemSchema
            };
        }
    }
}

#endif // .NET 10 Preview 4+
