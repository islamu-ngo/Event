using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Explore.Infrastructure.Authentication;

internal sealed class LocalJwtTokenGenerator(
    ISecretResolver secretResolver,
    IOptions<LocalIdentityOptions> options,
    TimeProvider timeProvider)
    : ILocalJwtTokenGenerator
{
    public async Task<LocalIssuedToken> GenerateAsync(
        LocalJwtTokenSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        SecretResolutionResult resolution = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
            tenantId: null,
            cancellationToken).ConfigureAwait(false);
        byte[] signingKey = DecodeSigningKey(resolution);

        DateTimeOffset issuedAt = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = issuedAt.AddMinutes(
            options.Value.AccessTokenLifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.UserId.ToString("D")),
            new(JwtRegisteredClaimNames.Email, subject.Email),
            new(JwtRegisteredClaimNames.GivenName, subject.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, subject.LastName),
            new("email_verified", subject.EmailVerified ? "true" : "false", ClaimValueTypes.Boolean),
            new("auth_provider", "local"),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("N")),
            new(
                JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };
        claims.AddRange(subject.Roles.Select(role => new Claim("roles", role)));

        var token = new JwtSecurityToken(
            issuer: LocalIdentityOptions.Issuer,
            audience: LocalIdentityOptions.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(signingKey),
                SecurityAlgorithms.HmacSha256));

        return new LocalIssuedToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }

    private static byte[] DecodeSigningKey(SecretResolutionResult resolution)
    {
        if (!resolution.IsResolved || string.IsNullOrWhiteSpace(resolution.Value))
        {
            throw new InvalidOperationException(
                "Local Identity JWT signing authority is unavailable.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(resolution.Value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Local Identity JWT signing authority is invalid.",
                exception);
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "Local Identity JWT signing authority does not meet the 256-bit minimum.");
        }

        return key;
    }
}
