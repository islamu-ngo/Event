using Explore.API.Hateoas;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

public sealed class EventResourceContentResponseTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.OperationId != RouteNames.GetEventResourceContent ||
            operation.Responses is null || !operation.Responses.TryGetValue("200", out var response) ||
            response.Content is not { } content)
            return Task.CompletedTask;

        content.Clear();
        foreach (string mediaType in new[]
        {
            EventResourceGovernancePolicy.PdfMediaType,
            EventResourceGovernancePolicy.WordDocumentMediaType,
            EventResourceGovernancePolicy.PowerPointPresentationMediaType
        })
            content[mediaType] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
            };
        return Task.CompletedTask;
    }
}
