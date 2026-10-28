using Explore.API.Hateoas;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

/// <summary>Documents the dedicated canonical-human boundary despite the required anonymous GET annotation.</summary>
public sealed class ModerationNoticeOpenApiSecurityTransformer : IOpenApiOperationTransformer, IOperationFilter
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        Apply(operation, context.Description.ActionDescriptor, context.Document!);
        return Task.CompletedTask;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context) =>
        Apply(operation, context.ApiDescription.ActionDescriptor, context.Document);

    private static void Apply(OpenApiOperation operation, ActionDescriptor action, OpenApiDocument document)
    {
        if (!action.EndpointMetadata.OfType<HttpMethodAttribute>().Any(route => route.Name is
            RouteNames.GetModerationNotices or RouteNames.GetModerationNotice or RouteNames.AcknowledgeModerationNotice
                or RouteNames.GetModerationNoticeStatus or RouteNames.GetModerationContentResponseAvailability
                or RouteNames.SubmitModerationContentResponse or RouteNames.GetModerationContentResponseStatement
                or RouteNames.GetModerationContentResponses))
            return;
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes.TryAdd(JwtBearerDefaults.AuthenticationScheme,
            new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = []
            }
        ];
    }
}
