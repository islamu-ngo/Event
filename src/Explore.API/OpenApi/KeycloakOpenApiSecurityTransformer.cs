using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Explore.API.OpenApi;

internal sealed class KeycloakOpenApiSecurityTransformer(IConfiguration configuration) :
    IOpenApiDocumentTransformer,
    IOpenApiOperationTransformer
{
    internal const string SecuritySchemeName = "Keycloak";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!TryResolveAuthorizationUri(configuration, out Uri? authorizationUri))
        {
            return Task.CompletedTask;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SecuritySchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                Implicit = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = authorizationUri,
                    Scopes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["openid"] = "openid",
                        ["profile"] = "profile"
                    }
                }
            }
        };

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!TryResolveAuthorizationUri(configuration, out _))
            return Task.CompletedTask;

        ApplyOperationSecurity(
            operation,
            context.Description.ActionDescriptor,
            context.Document!);
        return Task.CompletedTask;
    }

    internal static void ApplyOperationSecurity(
        OpenApiOperation operation,
        Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor actionDescriptor,
        OpenApiDocument document)
    {
        if (operation.Security is { Count: > 0 })
        {
            return;
        }

        var metadata = actionDescriptor.EndpointMetadata;
        IAuthorizeData[] authorization = metadata.OfType<IAuthorizeData>().ToArray();
        if (metadata.OfType<IAllowAnonymous>().Any()
            || authorization.Length == 0
            || authorization.Any(item => !string.IsNullOrWhiteSpace(item.AuthenticationSchemes)))
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

    internal static bool TryResolveAuthorizationUri(
        IConfiguration configuration,
        out Uri? authorizationUri)
    {
        string? configuredUrl = configuration["Keycloak:AuthorizationUrl"];
        if (TryCreateHttpUri(configuredUrl, out authorizationUri))
            return true;

        string? authority = configuration["Keycloak:Authority"];
        return TryCreateHttpUri(
            string.IsNullOrWhiteSpace(authority)
                ? null
                : $"{authority.TrimEnd('/')}/protocol/openid-connect/auth",
            out authorizationUri);
    }

    private static bool TryCreateHttpUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate)
            || (!string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}

internal sealed class KeycloakSwaggerOpenApiSecurityFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context) =>
        KeycloakOpenApiSecurityTransformer.ApplyOperationSecurity(
            operation,
            context.ApiDescription.ActionDescriptor,
            context.Document);
}
