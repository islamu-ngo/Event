using Explore.API.Hateoas;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

public sealed class StorageUploadRequestBodyTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                operation.OperationId,
                RouteNames.UploadStorageUploadSessionContent,
                StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/octet-stream"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "binary"
                    }
                }
            }
        };

        return Task.CompletedTask;
    }
}
