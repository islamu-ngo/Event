using Explore.Application.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

public sealed class PrivacyErasureReceiptOpenApiSecurityTransformer :
    IOpenApiDocumentTransformer,
    IOpenApiOperationTransformer,
    IOperationFilter
{
    public const string SecuritySchemeName = ApiAuthenticationSchemeNames.PrivacyErasureReceipt;

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SecuritySchemeName] = CreateSecurityScheme();

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ApplyOperationSecurity(
            operation,
            context.Description.ActionDescriptor,
            context.Document!);
        return Task.CompletedTask;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context) =>
        ApplyOperationSecurity(
            operation,
            context.ApiDescription.ActionDescriptor,
            context.Document);

    internal static OpenApiSecurityScheme CreateSecurityScheme() => new()
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "Authorization",
        Description = "Short-lived privacy-erasure receipt. Send Authorization: ErasureReceipt <receipt>. Do not store the receipt."
    };

    private static void ApplyOperationSecurity(
        OpenApiOperation operation,
        Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor actionDescriptor,
        OpenApiDocument document)
    {
        if (actionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()
            || !actionDescriptor.EndpointMetadata
                .OfType<IAuthorizeData>()
                .Any(UsesPrivacyErasureReceipt))
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

    private static bool UsesPrivacyErasureReceipt(IAuthorizeData authorize) =>
        authorize.AuthenticationSchemes?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(SecuritySchemeName, StringComparer.Ordinal) == true;
}
