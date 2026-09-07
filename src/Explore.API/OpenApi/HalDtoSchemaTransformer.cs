// ABOUTME: OpenAPI document transformer that registers inner DTO schemas from HAL wrapper types.
// ABOUTME: Ensures NSwag generates proper DTO classes by adding them as explicit schemas in the OpenAPI document.

using System.Text.Json.Serialization;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

/// <summary>
/// Document transformer that ensures inner DTO types from HAL wrappers are registered as schemas.
/// The HAL wrapper types (HalResource{T}, HalCollectionResource{T}) have custom JSON serialization
/// that flattens properties, but the OpenAPI generator doesn't create schemas for the inner T types.
/// This transformer adds those schemas explicitly.
/// </summary>
public class HalDtoSchemaTransformer : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

        foreach (var dtoType in HalOpenApiSchemaCatalog.RegisteredDtoTypes)
        {
            var schemaName = dtoType.Name;

            // Skip if schema already exists
            if (document.Components.Schemas.ContainsKey(schemaName))
                continue;

            // Generate schema for this DTO type using the context's method
            // GetOrCreateSchemaAsync returns OpenApiSchema directly
            var schema = await context.GetOrCreateSchemaAsync(dtoType, cancellationToken: cancellationToken);

            // Use AddComponent to properly register the schema in the document
            document.AddComponent(schemaName, schema);
        }

        if (!document.Components.Schemas.ContainsKey(nameof(HalLink)))
        {
            var halLinkSchema = await context.GetOrCreateSchemaAsync(typeof(HalLink), cancellationToken: cancellationToken);
            document.AddComponent(nameof(HalLink), halLinkSchema);
        }

        // Also populate the empty HAL wrapper schemas with flattened DTO properties + HAL links
        await PopulateHalResourceSchemas(document, context, cancellationToken);

        // Type the HAL collection embedded items as HAL resource references instead of object arrays.
        PopulateHalCollectionEmbeddedSchemas(document);
        PopulateHalDetailEmbeddedSchemas(document);

        // Fix inline array item schemas that should be $ref references to component schemas.
        // GetOrCreateSchemaAsync inlines nested DTO types (e.g., EventDto.Tags = List<TagListDto>)
        // which causes NSwag to generate duplicate types with conflicting names.
        ReplaceInlineArrayItemsWithReferences(document);
        ReplaceInlineObjectPropertiesWithReferences(document);
        ReplaceInlineHalLinkDictionaryPropertiesWithReferences(document);
        ReplaceInlineHalLinkDictionaryArrayItemsWithReferences(document);

        // Enums carrying their own converter are inlined as an untyped `{}` by GetOrCreateSchemaAsync,
        // which degrades generated clients to `object`. Point them at their component schema instead.
        ReplaceEnumPropertiesWithReferences(document);
    }

    private async Task PopulateHalResourceSchemas(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemas = document.Components?.Schemas;
        if (schemas == null)
            return;

        foreach (var (halSchemaName, dtoType) in HalOpenApiSchemaCatalog.DetailResourceMappings)
        {
            if (!schemas.TryGetValue(halSchemaName, out var halSchemaInterface))
            {
                halSchemaInterface = new OpenApiSchema();
                schemas[halSchemaName] = halSchemaInterface;
            }

            // Cast to concrete type for mutation (OpenApi 2.x pattern)
            if (halSchemaInterface is not OpenApiSchema halSchema)
                continue;

            // Get or create schema for the inner DTO
            var dtoSchema = await context.GetOrCreateSchemaAsync(dtoType, cancellationToken: cancellationToken);

            HalOpenApiSchemaMutator.FlattenDtoIntoHalResource(
                halSchema,
                dtoSchema,
                linkValueSchema: new OpenApiSchemaReference(nameof(HalLink), document));
        }
    }

    private static void PopulateHalCollectionEmbeddedSchemas(OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;
        if (schemas == null)
            return;

        foreach (var (embeddedSchemaName, itemResourceSchemaName) in HalOpenApiSchemaCatalog.CollectionEmbeddedItemResourceMappings)
        {
            if (!schemas.TryGetValue(embeddedSchemaName, out var embeddedSchemaInterface))
            {
                embeddedSchemaInterface = new OpenApiSchema();
                schemas[embeddedSchemaName] = embeddedSchemaInterface;
            }

            if (embeddedSchemaInterface is not OpenApiSchema embeddedSchema)
                continue;

            HalOpenApiSchemaMutator.EnsureEmbeddedItemsArrayType(
                embeddedSchema,
                new OpenApiSchemaReference(itemResourceSchemaName, document));
        }
    }

    private static void PopulateHalDetailEmbeddedSchemas(OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;
        if (schemas == null)
            return;

        foreach (var (detailSchemaName, embeddedSchemaName) in
            HalOpenApiSchemaCatalog.DetailResourceEmbeddedMappings)
        {
            if (!schemas.TryGetValue(detailSchemaName, out var detailSchemaInterface)
                || detailSchemaInterface is not OpenApiSchema detailSchema
                || !schemas.ContainsKey(embeddedSchemaName))
            {
                continue;
            }

            HalOpenApiSchemaMutator.SetEmbeddedReference(
                detailSchema,
                new OpenApiSchemaReference(embeddedSchemaName, document));
        }
    }

    /// <summary>
    /// Replaces inline array item schemas with $ref references to registered component schemas.
    /// When GetOrCreateSchemaAsync generates a schema for a DTO with List&lt;T&gt; properties
    /// (e.g., EventDto.Tags = List&lt;TagListDto&gt;), the inner T schema is inlined.
    /// This causes NSwag to generate duplicate types with conflicting names (Tags/tags).
    /// This method uses reflection to find such properties and replaces inline schemas
    /// with proper $ref references to the component schemas registered above.
    /// </summary>
    private void ReplaceInlineArrayItemsWithReferences(OpenApiDocument document)
    {
        var registeredDtoNames = new HashSet<string>(HalOpenApiSchemaCatalog.RegisteredDtoTypes.Select(t => t.Name));

        foreach (var dtoType in HalOpenApiSchemaCatalog.RegisteredDtoTypes)
        {
            foreach (var prop in dtoType.GetProperties())
            {
                if (!TryGetCollectionItemType(prop.PropertyType, out var itemType))
                    continue;
                if (!registeredDtoNames.Contains(itemType.Name))
                    continue;

                // Found a collection of a known DTO — fix only the owning DTO schema and its cataloged HAL wrapper.
                var camelCase = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];

                ReplaceInlineArrayItemsWithReference(document, dtoType.Name, camelCase, itemType.Name);

                foreach (var halSchemaName in HalOpenApiSchemaCatalog.DetailResourceMappings
                    .Where(mapping => mapping.Value == dtoType)
                    .Select(mapping => mapping.Key))
                {
                    ReplaceInlineArrayItemsWithReference(document, halSchemaName, camelCase, itemType.Name);
                }
            }
        }
    }

    private static bool TryGetCollectionItemType(Type propertyType, out Type itemType)
    {
        itemType = typeof(object);

        if (!propertyType.IsGenericType)
            return false;

        var genericDefinition = propertyType.GetGenericTypeDefinition();
        if (genericDefinition != typeof(List<>)
            && genericDefinition != typeof(IReadOnlyList<>)
            && genericDefinition != typeof(IList<>))
        {
            return false;
        }

        itemType = propertyType.GetGenericArguments()[0];
        return true;
    }

    private static void ReplaceInlineArrayItemsWithReference(OpenApiDocument document, string schemaName, string propertyName, string itemSchemaName)
    {
        if (document.Components?.Schemas?.TryGetValue(schemaName, out var schemaI) != true)
            return;

        if (schemaI is not OpenApiSchema schema || schema.Properties == null)
            return;

        if (!schema.Properties.TryGetValue(propertyName, out var arraySchemaI))
            return;

        if (arraySchemaI is OpenApiSchema arraySchema
            && arraySchema.Items != null
            && arraySchema.Items is not OpenApiSchemaReference)
        {
            arraySchema.Items = new OpenApiSchemaReference(itemSchemaName, document);
        }
    }

    private static void ReplaceInlineObjectPropertiesWithReferences(OpenApiDocument document)
    {
        var registeredDtoTypes = new HashSet<Type>(HalOpenApiSchemaCatalog.RegisteredDtoTypes);
        var registeredDtoNames = new HashSet<string>(HalOpenApiSchemaCatalog.RegisteredDtoTypes.Select(t => t.Name));

        foreach (var dtoType in HalOpenApiSchemaCatalog.RegisteredDtoTypes)
        {
            foreach (var prop in dtoType.GetProperties())
            {
                var propertyType = UnwrapNullableType(prop.PropertyType);

                if (propertyType == dtoType || !registeredDtoTypes.Contains(propertyType))
                    continue;

                if (TryGetCollectionItemType(propertyType, out _))
                    continue;

                var camelCase = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];

                ReplaceInlineObjectPropertyWithReference(document, dtoType.Name, camelCase, propertyType.Name);

                foreach (var halSchemaName in HalOpenApiSchemaCatalog.DetailResourceMappings
                    .Where(mapping => mapping.Value == dtoType)
                    .Select(mapping => mapping.Key))
                {
                    ReplaceInlineObjectPropertyWithReference(document, halSchemaName, camelCase, propertyType.Name);
                }
            }
        }
    }

    /// <summary>
    /// Points every cataloged string-enum property at its component schema, on both the DTO schema and
    /// each HAL wrapper that flattens it. Without this a converter-backed enum reaches NSwag as an
    /// untyped schema and generates an <c>object</c> property that no UI can switch on.
    /// </summary>
    private static void ReplaceEnumPropertiesWithReferences(OpenApiDocument document)
    {
        if (document.Components?.Schemas is not { } schemas)
            return;

        foreach (var dtoType in HalOpenApiSchemaCatalog.RegisteredDtoTypes)
        {
            foreach (var prop in dtoType.GetProperties())
            {
                var propertyType = UnwrapNullableType(prop.PropertyType);
                if (!OpenApiStringEnumSchemaCatalog.IsStringEnum(propertyType))
                {
                    continue;
                }

                if (!schemas.ContainsKey(propertyType.Name))
                {
                    if (!OpenApiStringEnumSchemaCatalog.RequiresEagerNestedComponent(propertyType))
                    {
                        continue;
                    }

                    var enumSchema = new OpenApiSchema();
                    OpenApiStringEnumSchemaMutator.Apply(enumSchema, propertyType);
                    schemas[propertyType.Name] = enumSchema;
                }

                var jsonName = GetJsonPropertyName(prop);

                var isNullable = Nullable.GetUnderlyingType(prop.PropertyType) is not null;
                ReplacePropertyWithReference(document, dtoType.Name, jsonName, propertyType.Name, isNullable);

                foreach (var halSchemaName in HalOpenApiSchemaCatalog.DetailResourceMappings
                    .Where(mapping => mapping.Value == dtoType)
                    .Select(mapping => mapping.Key))
                {
                    ReplacePropertyWithReference(document, halSchemaName, jsonName, propertyType.Name, isNullable);
                }
            }
        }
    }

    private static void ReplacePropertyWithReference(
        OpenApiDocument document,
        string schemaName,
        string propertyName,
        string targetSchemaName,
        bool isNullable)
    {
        if (document.Components?.Schemas?.TryGetValue(schemaName, out var schemaI) != true)
            return;

        if (schemaI is not OpenApiSchema schema || schema.Properties == null)
            return;

        if (!schema.Properties.TryGetValue(propertyName, out var propertySchema)
            || (!isNullable && propertySchema is OpenApiSchemaReference))
        {
            return;
        }

        schema.Properties[propertyName] = isNullable
            ? new OpenApiSchema
            {
                OneOf =
                [
                    new OpenApiSchema { Type = JsonSchemaType.Null },
                    new OpenApiSchemaReference(targetSchemaName, document)
                ]
            }
            : new OpenApiSchemaReference(targetSchemaName, document);
    }

    private static Type UnwrapNullableType(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
            return underlyingType;

        return type;
    }

    private static void ReplaceInlineObjectPropertyWithReference(OpenApiDocument document, string schemaName, string propertyName, string targetSchemaName)
    {
        if (document.Components?.Schemas?.TryGetValue(schemaName, out var schemaI) != true)
            return;

        if (schemaI is not OpenApiSchema schema || schema.Properties == null)
            return;

        if (!schema.Properties.TryGetValue(propertyName, out var propertySchemaI))
            return;

        if (propertySchemaI is OpenApiSchema propertySchema
            && propertySchema.Type == JsonSchemaType.Object
            && propertySchema.Properties?.Count > 0)
        {
            schema.Properties[propertyName] = new OpenApiSchemaReference(targetSchemaName, document);
        }
    }

    private static void ReplaceInlineHalLinkDictionaryPropertiesWithReferences(OpenApiDocument document)
    {
        foreach (var dtoType in HalOpenApiSchemaCatalog.RegisteredDtoTypes)
        {
            foreach (var prop in dtoType.GetProperties())
            {
                if (!TryGetDictionaryValueType(prop.PropertyType, out var valueType) || valueType != typeof(HalLink))
                    continue;

                var jsonName = GetJsonPropertyName(prop);

                ReplaceInlineDictionaryValueWithReference(document, dtoType.Name, jsonName, nameof(HalLink));

                foreach (var halSchemaName in HalOpenApiSchemaCatalog.DetailResourceMappings
                    .Where(mapping => mapping.Value == dtoType)
                    .Select(mapping => mapping.Key))
                {
                    ReplaceInlineDictionaryValueWithReference(document, halSchemaName, jsonName, nameof(HalLink));
                }
            }
        }
    }

    private static bool TryGetDictionaryValueType(Type propertyType, out Type valueType)
    {
        valueType = typeof(object);

        var dictionaryType = propertyType.IsGenericType
            && IsSupportedDictionaryType(propertyType.GetGenericTypeDefinition())
                ? propertyType
                : propertyType.GetInterfaces().FirstOrDefault(type =>
                    type.IsGenericType && IsSupportedDictionaryType(type.GetGenericTypeDefinition()));

        if (dictionaryType == null)
            return false;

        valueType = dictionaryType.GetGenericArguments()[1];
        return true;
    }

    private static bool IsSupportedDictionaryType(Type genericDefinition) =>
        genericDefinition == typeof(Dictionary<,>)
        || genericDefinition == typeof(IDictionary<,>)
        || genericDefinition == typeof(IReadOnlyDictionary<,>);

    private static void ReplaceInlineHalLinkDictionaryArrayItemsWithReferences(OpenApiDocument document)
    {
        foreach (var dtoType in HalOpenApiSchemaCatalog.RegisteredDtoTypes)
        {
            foreach (var collectionProp in dtoType.GetProperties())
            {
                if (!TryGetCollectionItemType(collectionProp.PropertyType, out var itemType))
                    continue;

                var collectionJsonName = GetJsonPropertyName(collectionProp);

                foreach (var itemProp in itemType.GetProperties())
                {
                    if (!TryGetDictionaryValueType(itemProp.PropertyType, out var valueType) || valueType != typeof(HalLink))
                        continue;

                    var itemJsonName = GetJsonPropertyName(itemProp);
                    ReplaceInlineArrayItemDictionaryValueWithReference(
                        document,
                        dtoType.Name,
                        collectionJsonName,
                        itemJsonName,
                        nameof(HalLink));

                    foreach (var halSchemaName in HalOpenApiSchemaCatalog.DetailResourceMappings
                        .Where(mapping => mapping.Value == dtoType)
                        .Select(mapping => mapping.Key))
                    {
                        ReplaceInlineArrayItemDictionaryValueWithReference(
                            document,
                            halSchemaName,
                            collectionJsonName,
                            itemJsonName,
                            nameof(HalLink));
                    }
                }
            }
        }
    }

    private static string GetJsonPropertyName(System.Reflection.PropertyInfo prop) =>
        prop.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: true)
            .OfType<JsonPropertyNameAttribute>()
            .FirstOrDefault()?.Name
        ?? char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];

    private static void ReplaceInlineDictionaryValueWithReference(OpenApiDocument document, string schemaName, string propertyName, string targetSchemaName)
    {
        if (document.Components?.Schemas?.TryGetValue(schemaName, out var schemaI) != true)
            return;

        if (schemaI is not OpenApiSchema schema || schema.Properties == null)
            return;

        if (!schema.Properties.TryGetValue(propertyName, out var propertySchemaI))
            return;

        if (propertySchemaI is OpenApiSchema propertySchema
            && propertySchema.AdditionalProperties is not OpenApiSchemaReference)
        {
            propertySchema.AdditionalProperties = new OpenApiSchemaReference(targetSchemaName, document);
        }
    }

    private static void ReplaceInlineArrayItemDictionaryValueWithReference(
        OpenApiDocument document,
        string schemaName,
        string arrayPropertyName,
        string dictionaryPropertyName,
        string targetSchemaName)
    {
        if (document.Components?.Schemas?.TryGetValue(schemaName, out var schemaI) != true)
            return;

        if (schemaI is not OpenApiSchema schema || schema.Properties == null)
            return;

        if (!schema.Properties.TryGetValue(arrayPropertyName, out var arraySchemaI))
            return;

        if (arraySchemaI is not OpenApiSchema { Items: OpenApiSchema itemSchema })
            return;

        if (itemSchema.Properties == null)
            return;

        if (!itemSchema.Properties.TryGetValue(dictionaryPropertyName, out var dictionarySchemaI))
            return;

        if (dictionarySchemaI is OpenApiSchema dictionarySchema
            && dictionarySchema.AdditionalProperties is not OpenApiSchemaReference)
        {
            dictionarySchema.AdditionalProperties = new OpenApiSchemaReference(targetSchemaName, document);
        }
    }
}
