using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Explore.API.Extensions;
using Explore.Application.Configuration;
using Explore.Application.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Event.API.IntegrationTests.Authentication;

public sealed class LocalIdentitySchemeIsolationTests
{
    [Test]
    public async Task LocalAndKeycloakSchemesAreAlwaysRegisteredAndRejectCrossAuthorityTokens()
    {
        byte[] localKey = RandomNumberGenerator.GetBytes(64);
        byte[] keycloakKey = RandomNumberGenerator.GetBytes(64);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LocalIdentityOptions.SectionName}:JwtKey"] =
                    Convert.ToBase64String(localKey)
            })
            .Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton(environment);
        services.AddApiAuthentication(
            configuration,
            environment,
            skipAuthorityWarmup: true);
        await using ServiceProvider provider = services.BuildServiceProvider();

        var schemes = await provider
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        await Assert.That(schemes.Select(scheme => scheme.Name))
            .Contains(ApiAuthenticationSchemeNames.LocalIdentity);
        await Assert.That(schemes.Select(scheme => scheme.Name))
            .Contains(JwtBearerDefaults.AuthenticationScheme);

        JwtBearerOptions localOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(ApiAuthenticationSchemeNames.LocalIdentity);
        string localToken = CreateToken(
            localKey,
            LocalIdentityOptions.Issuer,
            LocalIdentityOptions.Audience);
        string keycloakToken = CreateToken(
            keycloakKey,
            "https://keycloak.example.test/realms/event",
            "islamu-event-api");
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        TokenValidationParameters localValidation =
            localOptions.TokenValidationParameters.Clone();
        localValidation.ValidateLifetime = false;

        _ = handler.ValidateToken(
            localToken,
            localValidation,
            out _);
        await Assert.ThrowsAsync<SecurityTokenException>(() =>
            ValidateAsync(
                handler,
                keycloakToken,
                localValidation));
        await Assert.ThrowsAsync<SecurityTokenException>(() =>
            ValidateAsync(
                handler,
                localToken,
                CreateKeycloakValidationParameters(keycloakKey)));
    }

    private static TokenValidationParameters CreateKeycloakValidationParameters(
        byte[] key) =>
        new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = "https://keycloak.example.test/realms/event",
            ValidateAudience = true,
            ValidAudience = "islamu-event-api",
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero
        };

    private static string CreateToken(byte[] key, string issuer, string audience)
    {
        DateTime now = DateTime.UnixEpoch;
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString("D"))],
            now.AddMinutes(-1),
            now.AddMinutes(5),
            new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Task ValidateAsync(
        JwtSecurityTokenHandler handler,
        string token,
        TokenValidationParameters validation)
    {
        handler.ValidateToken(token, validation, out _);
        return Task.CompletedTask;
    }
}
