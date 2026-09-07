// NOTE: This is for Swashbuckle (AddSwaggerGen). Native ASP.NET Core OpenAPI (AddOpenApi)
// uses HalDtoSchemaTransformer with the same catalog and mutation helpers.

using Explore.Application.Hateoas;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

/// <summary>
/// Swashbuckle schema filter that properly generates OpenAPI schemas for HAL wrapper types.
/// The generic HalResource{T} flattens T's properties to root level during JSON serialization,
/// but Swagger doesn't understand this. This filter adds T's properties to the schema.
///
/// <para>
/// <b>NOTE:</b> This is the Swashbuckle (ISchemaFilter) adapter. ASP.NET Core native OpenAPI
/// uses <see cref="HalDtoSchemaTransformer"/> with the same catalog and mutation helpers.
/// </para>
/// </summary>
public class HalSchemaFilter : ISchemaFilter
{
    // Swashbuckle 10.x uses IOpenApiSchema interface; cast to OpenApiSchema for mutation
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        // Cast to concrete type for mutation (Swashbuckle 10.x pattern)
        if (schema is not OpenApiSchema openApiSchema)
            return;

        var type = context.Type;

        // Handle HalResource<T> - flatten Data properties to root
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Explore.Application.Hateoas.HalResource<>))
        {
            var innerType = type.GetGenericArguments().FirstOrDefault();
            if (innerType != null)
            {
                if (!HalOpenApiSchemaCatalog.IsRegisteredDto(innerType))
                {
                    return;
                }

                // First, ensure the inner DTO is registered in the schema repository
                var dtoSchema = context.SchemaGenerator.GenerateSchema(innerType, context.SchemaRepository);

                // Add the DTO properties to the HAL resource schema
                var resolvedSchema = ResolveSchema(dtoSchema, context);
                if (resolvedSchema is not null)
                {
                    var linkSchema = context.SchemaGenerator.GenerateSchema(typeof(HalLink), context.SchemaRepository);
                    HalOpenApiSchemaMutator.FlattenDtoIntoHalResource(
                        openApiSchema,
                        resolvedSchema,
                        linkValueSchema: linkSchema);
                    if (HalOpenApiSchemaCatalog.DetailResourceEmbeddedTypeMappings
                        .TryGetValue(innerType, out Type? embeddedType))
                    {
                        HalOpenApiSchemaMutator.SetEmbeddedReference(
                            openApiSchema,
                            context.SchemaGenerator.GenerateSchema(
                                embeddedType,
                                context.SchemaRepository));
                    }
                }
            }
        }

        // Handle HalCollectionEmbedded<T> - ensure items array has correct type
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Explore.Application.Hateoas.HalCollectionEmbedded<>))
        {
            var innerType = type.GetGenericArguments().FirstOrDefault();
            if (innerType != null)
            {
                if (!HalOpenApiSchemaCatalog.IsRegisteredDto(innerType))
                {
                    return;
                }

                // Generate schema reference for the item type wrapped in HalResource<T>
                var halResourceType = typeof(Explore.Application.Hateoas.HalResource<>).MakeGenericType(innerType);
                var itemSchema = context.SchemaGenerator.GenerateSchema(halResourceType, context.SchemaRepository);
                HalOpenApiSchemaMutator.EnsureEmbeddedItemsArrayType(openApiSchema, itemSchema);
            }
        }
    }

    private static OpenApiSchema? ResolveSchema(IOpenApiSchema dtoSchema, SchemaFilterContext context)
    {
        // If the DTO schema is a reference, resolve it to get the actual properties
        OpenApiSchema? resolvedSchema = dtoSchema as OpenApiSchema;

        // In OpenApi 2.x, references are handled via OpenApiSchemaReference
        if (dtoSchema is OpenApiSchemaReference schemaRef)
        {
            var refId = schemaRef.Id;
            if (refId is not null && context.SchemaRepository.Schemas.TryGetValue(refId, out var referencedSchema))
            {
                resolvedSchema = referencedSchema as OpenApiSchema;
            }
        }

        return resolvedSchema;
    }
}

/// <summary>
/// Final Swashbuckle document pass that normalizes public HAL resource component schemas.
/// Some generic HAL wrapper components are materialized before <see cref="HalSchemaFilter" /> can
/// resolve their DTO component, so this pass uses the explicit API catalog against the completed
/// component dictionary.
/// </summary>
public sealed class HalSchemaDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (swaggerDoc.Components?.Schemas is null)
        {
            return;
        }

        foreach (var (schemaName, schemaInterface) in swaggerDoc.Components.Schemas)
        {
            if (schemaInterface is not OpenApiSchema halSchema)
            {
                continue;
            }

            var dtoType = ResolveHalResourceDtoType(schemaName);
            if (dtoType is null)
            {
                continue;
            }

            if (!TryGetSchema(swaggerDoc, dtoType.FullName!, out var dtoSchema))
            {
                continue;
            }

            var linkSchema = context.SchemaGenerator.GenerateSchema(typeof(HalLink), context.SchemaRepository);
            HalOpenApiSchemaMutator.FlattenDtoIntoHalResource(
                halSchema,
                dtoSchema,
                linkValueSchema: linkSchema);
        }
    }

    private static Type? ResolveHalResourceDtoType(string schemaName)
        => HalOpenApiSchemaCatalog.DetailResourceMappings
            .Select(mapping => mapping.Value)
            .FirstOrDefault(dtoType => schemaName.Contains("HalResource`1", StringComparison.Ordinal)
                && schemaName.Contains(dtoType.FullName!, StringComparison.Ordinal));

    private static bool TryGetSchema(OpenApiDocument document, string schemaName, out OpenApiSchema schema)
    {
        schema = new OpenApiSchema();
        if (document.Components?.Schemas?.TryGetValue(schemaName.Replace('+', '-'), out var schemaInterface) != true
            || schemaInterface is not OpenApiSchema openApiSchema)
        {
            return false;
        }

        schema = openApiSchema;
        return true;
    }
}
