namespace Explore.API.OpenApi;

using Explore.API.Controllers;
using Explore.API.Hateoas;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

public sealed class ConfigurationManifestExportResponseTransformer
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        string? mediaType = operation.OperationId switch
        {
            RouteNames.ExportConfigurationManifest =>
                ConfigurationManifestExportApiContract.MediaType,
            RouteNames.ExportTenantConfigurationPackage =>
                TenantConfigurationPackageContractMetadata.MediaType,
            _ => null
        };
        if (mediaType is null || operation.Responses is null
            || !operation.Responses.TryGetValue("200", out IOpenApiResponse? response)
            || response.Content is null)
        {
            return Task.CompletedTask;
        }

        response.Content.Clear();
        response.Content[mediaType] =
            new OpenApiMediaType
            {
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "binary"
                }
            };

        return Task.CompletedTask;
    }
}
