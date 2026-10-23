using Explore.API.Authentication;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

public sealed class ModerationRecognitionCorrectionOpenApiSecurityTransformer :
    IOpenApiDocumentTransformer, IOpenApiOperationTransformer, IOperationFilter
{
    public const string SecuritySchemeName = ModerationRecognitionCorrectionReceiptAuthenticationHandler.SchemeName;

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SecuritySchemeName] = CreateSecurityScheme();
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        Apply(operation, context.Description.ActionDescriptor, context.Document!);
        return Task.CompletedTask;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context) =>
        Apply(operation, context.ApiDescription.ActionDescriptor, context.Document);

    internal static OpenApiSecurityScheme CreateSecurityScheme() => new()
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "Authorization",
        Description = "Protected recognition correction only. Send Authorization: ErasureCorrectionReceipt <original-receipt>. Grants no account or ordinary erasure-status authority."
    };

    private static void Apply(OpenApiOperation operation,
        Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor action, OpenApiDocument document)
    {
        // The GET is AllowAnonymous by controller convention, but its explicit
        // purpose-bound authentication is still mandatory and must be documented.
        if (!action.EndpointMetadata.OfType<HttpMethodAttribute>()
            .Any(route => route.Name == RouteNames.GetRecognitionCorrectionProofs)) return;
        operation.Security = [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = [] }
        ];
    }
}
