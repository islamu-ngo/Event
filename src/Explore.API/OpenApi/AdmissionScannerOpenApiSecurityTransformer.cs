using Explore.API.Authentication;
using Microsoft.AspNetCore.Authorization;
using Explore.Application.Constants;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

public sealed class AdmissionScannerOpenApiSecurityTransformer :
    IOpenApiDocumentTransformer,
    IOpenApiOperationTransformer,
    IOperationFilter
{

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[ApiAuthenticationSchemeNames.AdmissionScanner] = CreateSecurityScheme();
        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ApplyOperationSecurity(operation, context.Description.ActionDescriptor, context.Document!);
        return Task.CompletedTask;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context) =>
        ApplyOperationSecurity(operation, context.ApiDescription.ActionDescriptor, context.Document);

    internal static OpenApiSecurityScheme CreateSecurityScheme() => new()
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = AdmissionScannerAuthenticationDefaults.HeaderName,
        Description = "Opaque, expiring, revocable admission-scanner capability. Do not log or persist plaintext."
    };

    private static void ApplyOperationSecurity(
        OpenApiOperation operation,
        Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor actionDescriptor,
        OpenApiDocument document)
    {
        if (actionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()
            || !actionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any(UsesAdmissionScanner))
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiAuthenticationSchemeNames.AdmissionScanner, document)] = []
            }
        ];
    }

    private static bool UsesAdmissionScanner(IAuthorizeData authorize) =>
        authorize.AuthenticationSchemes?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(ApiAuthenticationSchemeNames.AdmissionScanner, StringComparer.Ordinal) == true;
}
