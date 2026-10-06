using Explore.API.OpenApi;
using Explore.Application.Constants;
using Microsoft.OpenApi;

namespace Explore.API.Extensions;

internal static class ServiceCollectionExtensions
{
    /// <summary>Mirrors native operation credentials without inventing an unavailable OAuth authority.</summary>
    internal static IServiceCollection AddSwaggerGenWithAuth(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v0.1", new OpenApiInfo
            {
                Title = "Explore API",
                Version = "v0.1"
            });

            options.CustomSchemaIds(id => id.FullName!.Replace('+', '-'));

            // Add schema filter for HAL wrapper types to properly expose inner DTOs
            options.SchemaFilter<HalSchemaFilter>();
            options.SchemaFilter<OpenApiStringEnumSchemaFilter>();
            options.DocumentFilter<HalSchemaDocumentFilter>();

            // Swashbuckle remains as a transition baseline until native OpenAPI parity is proven.
            // Mirror the native document's media-type version aliases so both documents describe
            // the same public content negotiation contract.
            options.OperationFilter<OpenApiVersionedContentTypesOperationFilter>();
            options.OperationFilter<ManagedControlPlaneOpenApiSecurityTransformer>();
            options.OperationFilter<PrivacyErasureReceiptOpenApiSecurityTransformer>();
            options.OperationFilter<LocalCredentialReplacementOpenApiSecurityTransformer>();
            options.OperationFilter<AdmissionScannerOpenApiSecurityTransformer>();
            options.OperationFilter<KeycloakSwaggerOpenApiSecurityFilter>();
            options.AddSecurityDefinition(
                ApiAuthenticationSchemeNames.ManagedControlPlane,
                ManagedControlPlaneOpenApiSecurityTransformer.CreateSecurityScheme());
            options.AddSecurityDefinition(
                PrivacyErasureReceiptOpenApiSecurityTransformer.SecuritySchemeName,
                PrivacyErasureReceiptOpenApiSecurityTransformer.CreateSecurityScheme());
            options.AddSecurityDefinition(
                LocalCredentialReplacementOpenApiSecurityTransformer.SecuritySchemeName,
                LocalCredentialReplacementOpenApiSecurityTransformer.CreateSecurityScheme());
            options.AddSecurityDefinition(
                ApiAuthenticationSchemeNames.AdmissionScanner,
                AdmissionScannerOpenApiSecurityTransformer.CreateSecurityScheme());

            // JWT and API-key requirements remain available without an OAuth authority.
            // Only add the OAuth alternative when its endpoint can be resolved.
            if (KeycloakOpenApiSecurityTransformer.TryResolveAuthorizationUri(
                    configuration,
                    out Uri? keycloakAuthorizationUri))
            {
                options.AddSecurityDefinition("Keycloak", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        Implicit = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = keycloakAuthorizationUri,
                            Scopes = new Dictionary<string, string>
                            {
                                { "openid", "openid" },
                                { "profile", "profile" }
                            }
                        }
                    }
                });
            }
        });

        return services;
    }
}
