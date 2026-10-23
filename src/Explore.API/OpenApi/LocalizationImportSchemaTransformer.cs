using Explore.Application.DTOs.Localization;
using Explore.Domain.Common.Localization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

/// <summary>Publishes the bounded flat import shape hidden by its strict JSON converter.</summary>
public sealed class LocalizationImportSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(ImportLocalizationBundleDto))
        {
            schema.Type = JsonSchemaType.Object;
            schema.AdditionalPropertiesAllowed = false;
            schema.Required = new HashSet<string>(["languageCode", "translations"], StringComparer.Ordinal);
            schema.Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["languageCode"] = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 35 },
                ["translations"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    MaxProperties = LocalizationBundlePolicy.MaximumEntries,
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        MaxLength = LocalizationBundlePolicy.MaximumValueCharacters
                    }
                }
            };
        }
        return Task.CompletedTask;
    }
}
