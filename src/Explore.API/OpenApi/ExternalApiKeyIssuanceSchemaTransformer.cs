using Explore.Application.DTOs.ExternalApiKey;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

/// <summary>
/// Requires acknowledged identity and disclosure fields without making immutable getters JSON setters.
/// </summary>
public sealed class ExternalApiKeyIssuanceSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>Publishes success-field presence independently of runtime constructor validation.</summary>
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(ExternalApiKeyIssuanceDto))
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            schema.Required.UnionWith(["id", "keyId", "disclosureStatus", "apiKey"]);
        }
        return Task.CompletedTask;
    }
}
