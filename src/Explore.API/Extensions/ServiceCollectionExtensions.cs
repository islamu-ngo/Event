using Explore.API.OpenApi;
using Explore.Application.Constants;
using Microsoft.OpenApi;

namespace Explore.API.Extensions;

internal static class ServiceCollectionExtensions
{
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
            options.OperationFilter<AdmissionScannerOpenApiSecurityTransformer>();
            options.AddSecurityDefinition(
                ApiAuthenticationSchemeNames.ManagedControlPlane,
                ManagedControlPlaneOpenApiSecurityTransformer.CreateSecurityScheme());
            options.AddSecurityDefinition(
                PrivacyErasureReceiptOpenApiSecurityTransformer.SecuritySchemeName,
                PrivacyErasureReceiptOpenApiSecurityTransformer.CreateSecurityScheme());
            options.AddSecurityDefinition(
                ApiAuthenticationSchemeNames.AdmissionScanner,
                AdmissionScannerOpenApiSecurityTransformer.CreateSecurityScheme());

            // Resolve from an explicit endpoint or the configured authority. If neither is available,
            // omit both the definition and its requirements so build-time generation cannot emit
            // dangling Keycloak references.
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

                options.OperationFilter<KeycloakSwaggerOpenApiSecurityFilter>();
            }
        });

        return services;
    }
}
