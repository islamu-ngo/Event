using ISLAMU.Wire.Contracts.Learning;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

/// <summary>Closes only learning input objects, mirroring their strict wire deserialization.</summary>
public sealed class LearningRequestSchemaTransformer : IOpenApiSchemaTransformer, ISchemaFilter
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        Apply(schema, context.JsonTypeInfo.Type);
        return Task.CompletedTask;
    }

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is OpenApiSchema concrete)
            Apply(concrete, context.Type);
    }

    private static void Apply(OpenApiSchema schema, Type type)
    {
        if (type == typeof(LearningClaimTourRequest)
            || type == typeof(LearningRecordTourOutcomeRequest)
            || type == typeof(LearningUpdatePreferencesRequest)
            || type == typeof(LearningApplyPracticeActionRequest)
            || type == typeof(LearningManageSpaceRequest))
        {
            schema.AdditionalPropertiesAllowed = false;
            schema.AdditionalProperties = null;
        }
    }
}
