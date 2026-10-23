using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Explore.API.Extensions;
using Explore.Application.Configuration;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class LocalSigningAuthorityConfigurationTests
{
    [Test]
    [Arguments("present", false)]
    [Arguments("present", true)]
    [Arguments("whitespace", false)]
    [Arguments("whitespace", true)]
    [Arguments("absent", false)]
    [Arguments("absent", true)]
    public async Task CanonicalDeploymentKeyOwnsIssuerAndActualApiValidationParameters(string declaration, bool replacement)
    {
        const string canonicalName = "Authentication__Local__JwtKey";
        const string aliasName = "AUTHENTICATION_LOCAL_JWT_KEY";
        string? previousCanonical = Environment.GetEnvironmentVariable(canonicalName);
        string? previousAlias = Environment.GetEnvironmentVariable(aliasName);
        string alias = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        string? canonical = declaration switch
        {
            "present" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            "whitespace" => " ",
            _ => null
        };
        try
        {
            Environment.SetEnvironmentVariable(canonicalName, canonical);
            Environment.SetEnvironmentVariable(aliasName, alias);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Provider"] = "Environment",
                ["Authentication:Provider"] = "local"
            });
            builder.Configuration.AddSecretAuthorityConfiguration("Testing");
            builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment, skipAuthorityWarmup: true);
            await using var application = builder.Build();
            var configured = application.Services.GetRequiredService<IOptions<LocalIdentityOptions>>();
            var signer = new LocalJwtTokenGenerator(configured, TimeProvider.System);
            string scheme = replacement ? ApiAuthenticationSchemeNames.LocalCredentialReplacement
                : ApiAuthenticationSchemeNames.LocalIdentity;
            var parameters = application.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
                .Get(scheme).TokenValidationParameters;
            var validator = new JwtSecurityTokenHandler { MapInboundClaims = false };
            Guid subject = Guid.CreateVersion7();
            string stamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            Task<string> IssueAsync(LocalJwtTokenGenerator issuer) => replacement
                ? ChallengeAsync(issuer)
                : SessionAsync(issuer);
            async Task<string> ChallengeAsync(LocalJwtTokenGenerator issuer) =>
                (await issuer.GenerateReplacementChallengeAsync(new(subject, Guid.CreateVersion7(), stamp), CancellationToken.None)).Token;
            async Task<string> SessionAsync(LocalJwtTokenGenerator issuer) =>
                (await issuer.GenerateAsync(new(new(subject, stamp, true), null, "Local", "Authority", []), CancellationToken.None)).Token;

            if (declaration == "whitespace")
            {
                await Assert.That(configured.Value.JwtKey == canonical).IsTrue();
                await Assert.ThrowsAsync<InvalidOperationException>(() => IssueAsync(signer));
                string aliasToken = await IssueAsync(new LocalJwtTokenGenerator(
                    Options.Create(new LocalIdentityOptions { JwtKey = alias }), TimeProvider.System));
                await Assert.That(() => validator.ValidateToken(aliasToken, parameters, out _))
                    .Throws<SecurityTokenSignatureKeyNotFoundException>();
            }
            else
            {
                string token = await IssueAsync(signer);
                var principal = validator.ValidateToken(token, parameters, out _);
                await Assert.That(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value).IsEqualTo(subject.ToString("D"));
                if (declaration == "present")
                {
                    var aliasParameters = parameters.Clone();
                    aliasParameters.IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(alias));
                    await Assert.That(() => validator.ValidateToken(token, aliasParameters, out _))
                        .Throws<SecurityTokenSignatureKeyNotFoundException>();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(canonicalName, previousCanonical);
            Environment.SetEnvironmentVariable(aliasName, previousAlias);
        }
    }
}
