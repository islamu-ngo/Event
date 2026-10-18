using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Supplies ephemeral issuer metadata to the production Bearer handler without replacing
/// its validation parameters, token events, or authentication lifetime observer.
/// </summary>
internal sealed class LocalizationNativeJwtAuthority : IDisposable
{
    private readonly RSA authority = RSA.Create(2048);
    private const string KeyId = "localization-authority";
    internal string Issuer { get; } = $"https://localization-{Guid.NewGuid():N}.invalid/realms/ISLAMU";

    internal void ConfigureServices(IServiceCollection services)
    {
        // Run after AuthenticatedWebApplicationFactory's TestScheme post-configuration.
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
            metadata.SigningKeys.Add(new RsaSecurityKey(authority.ExportParameters(false)) { KeyId = KeyId });
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
        });
    }

    internal UserExternalLogin Binding(User user) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = user.Id,
        User = user,
        AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
        AuthenticationProvider = null!,
        ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(Issuer, user.Id.ToString("D")).Value,
        CreatedAt = DateTime.UtcNow
    };

    internal string Token(Guid userId, bool forgedAdmin = false)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = Issuer,
            ["sub"] = userId.ToString("D"),
            ["aud"] = "islamu-event-api",
            ["azp"] = "islamu-event-blazor",
            ["typ"] = "Bearer",
            ["iat"] = now - 60,
            ["nbf"] = now - 60,
            ["exp"] = now + 3600
        };
        if (forgedAdmin)
            payload["explore:admin:instance"] = "true";

        // Match the independent RS256 signing pattern in KeycloakTokenContextFixture.
        string header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256",
            kid = KeyId,
            typ = "JWT"
        }));
        string input = $"{header}.{Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        byte[] signature = authority.SignData(
            Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{input}.{Base64UrlEncoder.Encode(signature)}";
    }

    public void Dispose() => authority.Dispose();
}
