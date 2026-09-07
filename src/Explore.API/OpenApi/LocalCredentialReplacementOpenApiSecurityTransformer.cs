// ABOUTME: Describes the isolated Local replacement bearer scheme in native and transitional OpenAPI documents.
// ABOUTME: Applies challenge-only security exclusively to endpoints selecting the replacement authentication scheme.

using Explore.Application.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

public sealed class LocalCredentialReplacementOpenApiSecurityTransformer :
    IOpenApiDocumentTransformer, IOpenApiOperationTransformer, IOperationFilter
{
    public const string SecuritySchemeName = ApiAuthenticationSchemeNames.LocalCredentialReplacement;

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
        ApplyOperationSecurity(operation, context.Description.ActionDescriptor, context.Document!);
        return Task.CompletedTask;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context) =>
        ApplyOperationSecurity(operation, context.ApiDescription.ActionDescriptor, context.Document);

    internal static OpenApiSecurityScheme CreateSecurityScheme() => new()
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "Local credential replacement JWT",
        Description = "Short-lived first-use replacement challenge only. Ordinary access tokens are not accepted."
    };

    private static void ApplyOperationSecurity(OpenApiOperation operation,
        Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor actionDescriptor, OpenApiDocument document)
    {
        if (actionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()
            || !actionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any(authorize =>
                authorize.AuthenticationSchemes?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Contains(SecuritySchemeName, StringComparer.Ordinal) == true))
        {
            return;
        }
        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = []
            }
        ];
    }
}
