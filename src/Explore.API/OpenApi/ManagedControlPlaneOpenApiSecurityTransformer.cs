using Explore.API.Authentication;
using Microsoft.AspNetCore.Authorization;
using Explore.Application.Constants;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

public sealed class ManagedControlPlaneOpenApiSecurityTransformer :
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
        document.Components.SecuritySchemes[ApiAuthenticationSchemeNames.ManagedControlPlane] = CreateSecurityScheme();

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Document is not null)
        {
            ApplyOperationSecurity(
                operation,
                context.Description.ActionDescriptor,
                context.Document);
        }
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
        Name = ManagedControlPlaneAuthenticationDefaults.HeaderName,
        Description = "Directional managed Control Plane credential."
    };

    private static void ApplyOperationSecurity(
        OpenApiOperation operation,
        Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor actionDescriptor,
        OpenApiDocument document)
    {
        if (!string.Equals(
                actionDescriptor.RouteValues["controller"],
                "Management",
                StringComparison.Ordinal))
        {
            return;
        }

        var metadata = actionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [];
            return;
        }

        bool usesManagedCredential = metadata
            .OfType<IAuthorizeData>()
            .Any(authorize => authorize.Policy is ManagedControlPlaneAuthorizationPolicies.Read
                or ManagedControlPlaneAuthorizationPolicies.Write);
        if (!usesManagedCredential)
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiAuthenticationSchemeNames.ManagedControlPlane, document)] = []
            }
        ];
    }
}
