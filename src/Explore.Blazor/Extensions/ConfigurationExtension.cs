using Explore.Blazor.Configuration;
using Explore.Blazor.Services.Auth;
using Explore.ServiceDefaults.Secrets;

namespace Explore.Blazor.Extensions;

public static class ConfigurationExtensions
{
    /// <summary>Stages complete frontend groups from the admitted owner before publishing any mapping.</summary>
    public static async ValueTask AddPreparedBffConfigurationAsync(
        this IConfigurationBuilder configBuilder,
        SecretAuthorityAcquisition owner,
        AuthenticationCredentialAcquisitionReceipts receipts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configBuilder);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(receipts);
        SecretAcquisitionEntry[][] groups =
        [
            [new("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT"),
             new("/runtime/bff/oidc/keycloak", "KEYCLOAK_REALM", "KEYCLOAK_REALM"),
             new("/runtime/bff/oidc/keycloak", "KEYCLOAK_CLIENT_ID", "KEYCLOAK_CLIENT_ID"),
             new("/runtime/bff/oidc/keycloak", "KEYCLOAK_BLAZOR_CLIENT_SECRET", "KEYCLOAK_BLAZOR_CLIENT_SECRET")],
            [new("/runtime/bff/oidc/google", "GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_ID"),
             new("/runtime/bff/oidc/google", "GOOGLE_CLIENT_SECRET", "GOOGLE_CLIENT_SECRET")],
            [new("/runtime/bff/atproto/oauth", "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS", "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS")]
        ];
        var staged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (SecretAcquisitionEntry[] group in groups)
        {
            var result = await owner.PrepareAvailableGroupAsync(group, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                if (result.Status == SecretKit.Core.SecretStatus.Absent)
                    continue;
                throw new InvalidOperationException("bff_secret_authority_unavailable");
            }
            using var lease = result.Value;
            if (group.Any(entry => string.IsNullOrWhiteSpace(lease.Configuration[entry.ConfigurationKey])
                || lease.Configuration[entry.ConfigurationKey]!.Contains("${", StringComparison.Ordinal)))
                throw new InvalidOperationException("bff_secret_authority_unavailable");
            foreach (SecretAcquisitionEntry entry in group)
                staged.Add(entry.ConfigurationKey, lease.Configuration[entry.ConfigurationKey]);
        }
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(staged).Build();
        cancellationToken.ThrowIfCancellationRequested();
        var authority = owner.Provider switch
        {
            SecretAuthorityKind.Environment => AuthenticationCredentialAcquisitionReceipts.Authority.Environment,
            SecretAuthorityKind.UserSecrets => AuthenticationCredentialAcquisitionReceipts.Authority.UserSecrets,
            SecretAuthorityKind.Infisical => AuthenticationCredentialAcquisitionReceipts.Authority.Infisical,
            _ => throw new InvalidOperationException("secret_authority_selection_invalid")
        };
        ApplyBlazorMapping(configBuilder, configuration, receipts, authority, configBuilder.Build());
    }

    /// <summary>
    /// Maps complete native frontend groups without importing raw authority configuration.
    /// </summary>
    /// <remarks>
    /// Standard Infisical keys:
    ///   /runtime/bff/oidc/keycloak: Keycloak confidential-client configuration.
    ///   /runtime/bff/oidc/google: Google confidential-client configuration.
    ///   /runtime/bff/atproto/oauth: ATProto frontend signing key only.
    /// </remarks>
    private static void ApplyBlazorMapping(
        IConfigurationBuilder configBuilder,
        IConfiguration config,
        AuthenticationCredentialAcquisitionReceipts receipts,
        AuthenticationCredentialAcquisitionReceipts.Authority authority,
        IConfiguration ordinary)
    {
        var rawRealm = config["KEYCLOAK_REALM"];
        var rawClientSecret = config["KEYCLOAK_BLAZOR_CLIENT_SECRET"];
        var rawGoogleClientSecret = config["GOOGLE_CLIENT_SECRET"];
        var rawApiUrl = ordinary["API_ENDPOINT"];
        var rawAuthProvider = ordinary["AUTHENTICATION_PROVIDER"]
            ?? ordinary["Authentication:Provider"];
        var rawAtprotoOAuthClientPrivateJwks = config["ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS"];
        var hasAspireApiReference =
            !string.IsNullOrWhiteSpace(GetAspireApiReference(ordinary, "http"))
            || !string.IsNullOrWhiteSpace(GetAspireApiReference(ordinary, "https"));
        var baseUrl = config["KEYCLOAK_ENDPOINT"];

        var mappedConfig = new Dictionary<string, string?>
        {
            ["Keycloak:ClientSecret"] = rawClientSecret,
            ["Google:ClientSecret"] = rawGoogleClientSecret,
        };

        if (!string.IsNullOrWhiteSpace(rawAtprotoOAuthClientPrivateJwks))
        {
            mappedConfig[AtprotoClientKeyProvider.ConfigurationKey] = rawAtprotoOAuthClientPrivateJwks;
        }

        if (rawClientSecret is not null)
        {
            string keycloakAuthority = $"{baseUrl!.TrimEnd('/')}/realms/{rawRealm}";
            mappedConfig["Keycloak:Realm"] = rawRealm;
            mappedConfig["Keycloak:Authority"] = keycloakAuthority;
            mappedConfig["Keycloak:MetadataAddress"] = $"{keycloakAuthority}/.well-known/openid-configuration";
            mappedConfig["Keycloak:ClientId"] = config["KEYCLOAK_CLIENT_ID"];
        }

        if (rawGoogleClientSecret is not null)
            mappedConfig["Google:ClientId"] = config["GOOGLE_CLIENT_ID"];

        if (!hasAspireApiReference && !string.IsNullOrWhiteSpace(rawApiUrl)
            && string.IsNullOrEmpty(ordinary["ExploreApi:BaseUrl"]))
        {
            mappedConfig["ExploreApi:BaseUrl"] = rawApiUrl;
        }

        if (!string.IsNullOrWhiteSpace(rawAuthProvider)
            && string.IsNullOrEmpty(ordinary["Authentication:Provider"]))
            mappedConfig["Authentication:Provider"] = rawAuthProvider;

        configBuilder.AddInMemoryCollection(mappedConfig);
        if (rawClientSecret is not null)
            receipts.RecordAcquired(AuthenticationCredentialAcquisitionReceipts.Consumer.KeycloakOidc, authority);
        if (rawGoogleClientSecret is not null)
            receipts.RecordAcquired(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc, authority);
    }

    private static string? GetAspireApiReference(IConfiguration config, string scheme) =>
        config[$"services:explore-api:{scheme}:0"]
        ?? config[$"services__explore-api__{scheme}__0"];
}
