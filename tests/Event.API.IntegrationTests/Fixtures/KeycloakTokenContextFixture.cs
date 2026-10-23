using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.API.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// An independent signing authority and protected HTTP state using production
/// authentication registration. No TestAuth handler or validation bypass is installed.
/// Controlled metadata supplies only the issuer and public signing key.
/// </summary>
public sealed class KeycloakTokenContextFixture : IAsyncDisposable
{
    private readonly RSA _authority = RSA.Create(2048);
    private readonly RSA _untrustedAuthority = RSA.Create(2048);
    private WebApplication _application = null!;
    private int _revision;
    private string? _representation;

    public string Issuer { get; } = $"https://issuer-{Guid.NewGuid():N}.invalid/realms/ISLAMU";
    public HttpClient Client { get; private set; } = null!;
    public int Revision => _revision;
    public string? Representation => _representation;

    public async Task InitializeAsync(
        bool legacyRepresentation = false,
        string[]? permittedClients = null,
        string? nativeAuthority = null,
        string? nativeMetadataAddress = null,
        string? audience = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        var configuration = new Dictionary<string, string?>
        {
            ["Keycloak:Authority"] = nativeAuthority ?? Issuer,
            ["Keycloak:RequireHttpsMetadata"] = "false",
            ["Keycloak:MetadataAddress"] = nativeMetadataAddress,
            ["Authentication:Local:JwtKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };
        if (audience is not null)
            configuration["Keycloak:Audience"] = audience;
        if (permittedClients is not null)
        {
            for (int index = 0; index < permittedClients.Length; index++)
                configuration[$"Keycloak:PermittedClients:{index}"] = permittedClients[index];
        }
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment, skipAuthorityWarmup: true);
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.UseSecurityTokenValidators = legacyRepresentation;
            if (nativeAuthority is null)
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(new RsaSecurityKey(_authority.ExportParameters(false)) { KeyId = "context-authority" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            }
            var validateContext = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                await validateContext(context);
                if (context.Result?.Failure is null)
                    _representation = context.SecurityToken.GetType().Name;
            };
        });
        _application = builder.Build();
        _application.UseAuthentication();
        _application.UseAuthorization();
        _application.MapPost("/protected-state", () => Results.Json(new { Revision = ++_revision }))
            .RequireAuthorization();
        await _application.StartAsync();
        Client = _application.GetTestClient();
    }

    public Dictionary<string, object?> ValidPayload()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new()
        {
            ["iss"] = Issuer,
            ["sub"] = Guid.NewGuid().ToString(),
            ["aud"] = "islamu-event-api",
            ["azp"] = "islamu-event-blazor",
            ["typ"] = "Bearer",
            ["iat"] = now - 60,
            ["nbf"] = now - 60,
            ["exp"] = now + 3600
        };
    }

    public string Sign(Dictionary<string, object?> payload, bool wrongKey = false, string headerType = "JWT") =>
        SignJson(JsonSerializer.Serialize(payload), wrongKey, headerType);

    public string SignJson(string payload, bool wrongKey = false, string headerType = "JWT")
    {
        string header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256",
            kid = "context-authority",
            typ = headerType
        }));
        string input = $"{header}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload))}";
        byte[] signature = (wrongKey ? _untrustedAuthority : _authority)
            .SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{input}.{Base64UrlEncoder.Encode(signature)}";
    }

    public async Task<HttpResponseMessage> PostAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/protected-state");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_application is not null)
            await _application.DisposeAsync();
        _authority.Dispose();
        _untrustedAuthority.Dispose();
    }
}
