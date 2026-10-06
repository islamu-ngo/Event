using Explore.Application.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

    /// <summary>Publishes default credential schemes and adds OAuth metadata only for a configured HTTP authority.</summary>
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        AddDefaultSecuritySchemes(document);
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

    /// <summary>Derives operation requirements from authorization metadata without protecting anonymous operations.</summary>
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ApplyOperationSecurity(
            operation,
            context.Description.ActionDescriptor,
            context.Document!,
            includeKeycloak: TryResolveAuthorizationUri(configuration, out _));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds independent default-authentication alternatives while preserving explicit requirements and purpose-bound schemes.
    /// </summary>
    internal static void ApplyOperationSecurity(
        OpenApiOperation operation,
        Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor actionDescriptor,
        OpenApiDocument document,
        bool includeKeycloak = true)
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

        AddDefaultSecuritySchemes(document);
        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = []
            },
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiAuthenticationSchemeNames.ApiKey, document)] = []
            }
        ];
        if (includeKeycloak)
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SecuritySchemeName, document)] = []
            });
        }
    }

    /// <summary>Defines JWT and API-key credentials independently of the configured identity provider.</summary>
    private static void AddDefaultSecuritySchemes(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes.TryAdd(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
        document.Components.SecuritySchemes.TryAdd(ApiAuthenticationSchemeNames.ApiKey, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiAuthenticationHeaderNames.ApiKey
        });
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

internal sealed class KeycloakSwaggerOpenApiSecurityFilter(IConfiguration configuration) : IOperationFilter
{
    /// <summary>Applies the same metadata-derived alternatives to the transitional Swagger document.</summary>
    public void Apply(OpenApiOperation operation, OperationFilterContext context) =>
        KeycloakOpenApiSecurityTransformer.ApplyOperationSecurity(
            operation,
            context.ApiDescription.ActionDescriptor,
            context.Document,
            includeKeycloak: KeycloakOpenApiSecurityTransformer.TryResolveAuthorizationUri(configuration, out _));
}
