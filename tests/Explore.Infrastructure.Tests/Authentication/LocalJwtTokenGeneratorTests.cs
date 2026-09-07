using System.Security.Claims;
using System.Security.Cryptography;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using System.IdentityModel.Tokens.Jwt;

namespace Explore.Infrastructure.Tests.Authentication;

public sealed class LocalJwtTokenGeneratorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task GeneratedTokenIsSignedAndCarriesBoundedLocalIdentityClaims()
    {
        byte[] key = RandomNumberGenerator.GetBytes(64);
        var resolver = CreateResolver(Convert.ToBase64String(key));
        var generator = new LocalJwtTokenGenerator(
            resolver,
            Options.Create(new LocalIdentityOptions
            {
                AccessTokenLifetimeMinutes = 30
            }),
            new FixedTimeProvider(Now));
        var subject = new LocalJwtTokenSubject(
            Guid.Parse("01990aa7-4c67-7fb8-a303-8b301cc615af"),
            "admin@example.test",
            "Site",
            "Administrator",
            true,
            ["Admin", "Organizer"]);

        LocalIssuedToken issued = await generator.GenerateAsync(
            subject,
            CancellationToken.None);

        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        ClaimsPrincipal principal = handler.ValidateToken(
            issued.Token,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = LocalIdentityOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = LocalIdentityOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (_, expires, _, _) =>
                    expires == Now.AddMinutes(30).UtcDateTime
            },
            out _);

        await Assert.That(principal.FindFirstValue(JwtRegisteredClaimNames.Sub))
            .IsEqualTo(subject.UserId.ToString("D"));
        await Assert.That(principal.FindFirstValue("auth_provider"))
            .IsEqualTo(AuthenticationProviderKind.Local.ToString().ToLowerInvariant());
        await Assert.That(principal.FindFirstValue("email_verified")).IsEqualTo("true");
        await Assert.That(principal.FindAll("roles").Select(claim => claim.Value))
            .IsEquivalentTo(["Admin", "Organizer"]);
        await Assert.That(issued.ExpiresAt).IsEqualTo(Now.AddMinutes(30));
    }

    [Test]
    public async Task MissingSigningSecretFailsClosedWithoutProducingToken()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unavailable);
        var generator = new LocalJwtTokenGenerator(
            resolver,
            Options.Create(new LocalIdentityOptions()),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(
                new LocalJwtTokenSubject(
                    Guid.CreateVersion7(),
                    "admin@example.test",
                    "Site",
                    "Administrator",
                    false,
                    []),
                CancellationToken.None));
    }

    private static ISecretResolver CreateResolver(string value)
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
                null,
                Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
                value,
                SecretSourceType.EnvironmentVariable,
                SecretScope.Instance,
                null,
                Now)));
        return resolver;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
