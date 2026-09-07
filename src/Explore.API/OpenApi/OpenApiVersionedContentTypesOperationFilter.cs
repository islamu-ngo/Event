using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

internal sealed class OpenApiVersionedContentTypesOperationFilter : IOperationFilter
{
    private const string ApiVersion = "0.1";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses is not null)
        {
            foreach (var response in operation.Responses.Values)
            {
                AddVersionedContentAlias(response.Content, "application/json");
                AddVersionedContentAlias(response.Content, "application/hal+json");
                AddVersionedContentAlias(response.Content, "application/*+json");
            }
        }

        if (operation.RequestBody is not null)
        {
            AddVersionedContentAlias(operation.RequestBody.Content, "application/json");
            AddVersionedContentAlias(operation.RequestBody.Content, "application/hal+json");
            AddVersionedContentAlias(operation.RequestBody.Content, "application/*+json", fallbackMediaType: "application/json");
        }
    }

    private static void AddVersionedContentAlias(
        IDictionary<string, OpenApiMediaType>? content,
        string mediaType,
        string? fallbackMediaType = null)
    {
        if (content is null)
        {
            return;
        }

        if (!content.TryGetValue(mediaType, out var mediaTypeSchema))
        {
            if (fallbackMediaType is null || !content.TryGetValue(fallbackMediaType, out mediaTypeSchema))
            {
                return;
            }
        }

        content.TryAdd($"{mediaType}; v={ApiVersion}", mediaTypeSchema);
    }
}
