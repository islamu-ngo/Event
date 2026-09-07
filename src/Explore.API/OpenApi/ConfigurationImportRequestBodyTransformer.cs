namespace Explore.API.OpenApi;

using Explore.API.Hateoas;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

public sealed class ConfigurationImportRequestBodyTransformer
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        string? mediaType = operation.OperationId switch
        {
            RouteNames.CreateInstanceConfigurationImportSession =>
                ConfigurationManifestContractMetadata.MediaType,
            RouteNames.CreateTenantConfigurationImportSession =>
                TenantConfigurationPackageContractMetadata.MediaType,
            _ => null
        };
        if (mediaType is null)
            return Task.CompletedTask;

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [mediaType] = new()
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
