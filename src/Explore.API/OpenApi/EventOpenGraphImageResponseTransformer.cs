using Explore.API.Hateoas;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

public sealed class EventOpenGraphImageResponseTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                operation.OperationId,
                RouteNames.GetEventOpenGraphImage,
                StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var responses = operation.Responses;
        if (responses is null || !responses.TryGetValue("200", out var response))
        {
            return Task.CompletedTask;
        }

        var content = response.Content;
        if (content is not null)
        {
            content.Clear();
            content["image/png"] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "binary"
                }
            };
        }

        return Task.CompletedTask;
    }
}
