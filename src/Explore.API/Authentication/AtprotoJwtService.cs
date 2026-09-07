using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain.Secrets;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Federation;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Explore.API.Authentication;

public sealed class AtprotoJwtService(
    ISecretResolver secretResolver,
    IOptions<AtprotoJwtOptions> configuredOptions,
    TimeProvider timeProvider) : IAtprotoSessionTokenIssuer
{
    private const string Es256 = SecurityAlgorithms.EcdsaSha256;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public async Task<AtprotoBootstrapIdentity?> ValidateBootstrapAsync(
        string token,
        Guid tenantId,
        string method,
        string path,
        CancellationToken cancellationToken)
    {
        if (!IsBoundedCompactJwt(token, AtprotoJwtOptions.MaximumBootstrapTokenBytes))
        {
            return null;
        }

        var ring = await ResolveRingAsync(
            SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks,
            cancellationToken).ConfigureAwait(false);
        var principal = Validate(token, ring, AtprotoJwtOptions.BootstrapIssuer, AtprotoJwtOptions.BootstrapAudience);
        var did = principal?.FindFirstValue(AtprotoJwtOptions.DidClaim);
        var classification = principal?.FindFirstValue(AtprotoJwtOptions.ClassificationClaim);
        var canonicalActorIdClaims = principal?.FindAll(AtprotoJwtOptions.CanonicalActorIdClaim).Take(2).ToArray() ?? [];
        var expectedConcurrencyStampClaims = principal?.FindAll(AtprotoJwtOptions.ExpectedCanonicalActorConcurrencyStampClaim).Take(2).ToArray() ?? [];
        if (principal is null
            || !Guid.TryParse(principal.FindFirstValue(AtprotoJwtOptions.TenantClaim), out var assertedTenant)
            || assertedTenant != tenantId
            || !IsBoundedDid(did)
            || classification is not ("person" or "organization" or "group")
            || !string.Equals(principal.FindFirstValue(AtprotoJwtOptions.MethodClaim), method, StringComparison.Ordinal)
            || !string.Equals(principal.FindFirstValue(AtprotoJwtOptions.PathClaim), path, StringComparison.Ordinal)
            || !string.Equals(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), "event-blazor-bff", StringComparison.Ordinal)
            || !long.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Iat), out var issuedAtSeconds)
            || !IsFreshBootstrapAssertion(issuedAtSeconds)
            || principal.FindFirstValue(JwtRegisteredClaimNames.Jti) is not { Length: > 0 and <= 64 } jti)
        {
            return null;
        }

        if (canonicalActorIdClaims.Length != expectedConcurrencyStampClaims.Length
            || canonicalActorIdClaims.Length > 1
            || (canonicalActorIdClaims.Length == 1
                && (!Guid.TryParseExact(canonicalActorIdClaims[0].Value, "D", out var canonicalActorId)
                    || canonicalActorId == Guid.Empty
                    || !Guid.TryParseExact(expectedConcurrencyStampClaims[0].Value, "D", out var expectedConcurrencyStamp)
                    || expectedConcurrencyStamp == Guid.Empty)))
        {
            return null;
        }

        return new(
            jti,
            assertedTenant,
            did!,
            classification,
            canonicalActorIdClaims.Length == 1 ? Guid.ParseExact(canonicalActorIdClaims[0].Value, "D") : null,
            canonicalActorIdClaims.Length == 1 ? Guid.ParseExact(expectedConcurrencyStampClaims[0].Value, "D") : null);
    }

    public async Task<AtprotoIssuedSessionToken> IssueAsync(
        Guid userId,
        Guid tenantId,
        AtprotoDid did,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || tenantId == Guid.Empty || string.IsNullOrEmpty(did.Value))
        {
            throw new ArgumentException("ATProto session identity is invalid.");
        }

        var ring = await ResolveRingAsync(
            SecretDefinitionRegistry.Keys.Atproto.SessionJwtPrivateJwks,
            cancellationToken).ConfigureAwait(false);
        var keyId = ring.ActiveKeyId
            ?? throw new InvalidOperationException("ATProto session signing is unavailable.");
        var sessionLifetime = configuredOptions.Value.SessionLifetime;
        if (sessionLifetime < TimeSpan.FromMinutes(1) || sessionLifetime > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("ATProto session lifetime must be between one minute and one hour.");
        }

        using var ecdsa = ring.CreateEcdsaKey(keyId, includePrivateKey: true);
        var key = CreateTransientKey(ecdsa, keyId);
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(sessionLifetime);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = AtprotoJwtOptions.SessionIssuer,
            Audience = AtprotoJwtOptions.SessionAudience,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
                new Claim(AtprotoJwtOptions.TenantClaim, tenantId.ToString("D")),
                new Claim(AtprotoJwtOptions.DidClaim, did.Value),
                new Claim("auth_provider", "atproto")
            ]),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, Es256),
            TokenType = "JWT"
        };

        return new(_handler.CreateEncodedJwt(descriptor), expiresAt);
    }

    public async Task<AtprotoSessionBridgeIdentity?> ValidateSessionBridgeAsync(
        string token,
        Guid tenantId,
        Guid userId,
        string did,
        string method,
        string path,
        CancellationToken cancellationToken)
    {
        if (!IsBoundedCompactJwt(token, AtprotoJwtOptions.MaximumSessionBridgeTokenBytes))
        {
            return null;
        }

        var ring = await ResolveRingAsync(
            SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks,
            cancellationToken).ConfigureAwait(false);
        var principal = Validate(
            token,
            ring,
            AtprotoJwtOptions.SessionBridgeIssuer,
            AtprotoJwtOptions.SessionBridgeAudience);
        if (principal is null
            || !HasSingleClaim(principal, JwtRegisteredClaimNames.Iss, AtprotoJwtOptions.SessionBridgeIssuer)
            || !HasSingleClaim(principal, JwtRegisteredClaimNames.Aud, AtprotoJwtOptions.SessionBridgeAudience)
            || !HasSingleClaim(principal, JwtRegisteredClaimNames.Sub, "event-blazor-bff")
            || !HasSingleClaim(principal, AtprotoJwtOptions.TenantClaim, tenantId.ToString("D"))
            || !HasSingleClaim(principal, AtprotoJwtOptions.UserClaim, userId.ToString("D"))
            || !HasSingleClaim(principal, AtprotoJwtOptions.DidClaim, did)
            || !HasSingleClaim(principal, AtprotoJwtOptions.MethodClaim, method)
            || !HasSingleClaim(principal, AtprotoJwtOptions.PathClaim, path)
            || !TryGetSingleClaim(principal, JwtRegisteredClaimNames.Iat, out var issuedAtValue)
            || !long.TryParse(issuedAtValue, out var issuedAtSeconds)
            || !TryGetSingleClaim(principal, JwtRegisteredClaimNames.Exp, out var expiresAtValue)
            || !long.TryParse(expiresAtValue, out var expiresAtSeconds)
            || !IsFreshBridgeAssertion(issuedAtSeconds, expiresAtSeconds, out var expiresAt)
            || !TryGetSingleClaim(principal, JwtRegisteredClaimNames.Jti, out var jti)
            || !Guid.TryParseExact(jti, "D", out _))
        {
            return null;
        }

        return new("session-bridge:" + jti, tenantId, userId, did, expiresAt);
    }

    public async Task<ClaimsPrincipal?> ValidateSessionAsync(
        string token,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!IsBoundedCompactJwt(token, AtprotoJwtOptions.MaximumSessionTokenBytes))
        {
            return null;
        }

        var ring = await ResolveRingAsync(
            SecretDefinitionRegistry.Keys.Atproto.SessionJwtPrivateJwks,
            cancellationToken).ConfigureAwait(false);
        var principal = Validate(token, ring, AtprotoJwtOptions.SessionIssuer, AtprotoJwtOptions.SessionAudience);
        return principal is not null
               && Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
               && userId != Guid.Empty
               && Guid.TryParse(principal.FindFirstValue(AtprotoJwtOptions.TenantClaim), out var assertedTenant)
               && assertedTenant == tenantId
               && principal.FindFirstValue(AtprotoJwtOptions.DidClaim) is { Length: > 4 and <= 2048 } did
               && did.StartsWith("did:", StringComparison.Ordinal)
               && string.Equals(principal.FindFirstValue("auth_provider"), "atproto", StringComparison.Ordinal)
            ? principal
            : null;
    }

    private bool IsFreshBootstrapAssertion(long issuedAtSeconds)
    {
        DateTimeOffset issuedAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        return issuedAt >= now.AddSeconds(-65) && issuedAt <= now.AddSeconds(5);
    }

    private bool IsFreshBridgeAssertion(
        long issuedAtSeconds,
        long expiresAtSeconds,
        out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        try
        {
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds);
            var now = timeProvider.GetUtcNow();
            return issuedAt >= now.AddSeconds(-65)
                   && issuedAt <= now.AddSeconds(5)
                   && expiresAt > now
                   && expiresAt > issuedAt
                   && expiresAt <= issuedAt.AddSeconds(65);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool HasSingleClaim(ClaimsPrincipal principal, string type, string expected) =>
        TryGetSingleClaim(principal, type, out var actual)
        && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool TryGetSingleClaim(ClaimsPrincipal principal, string type, out string value)
    {
        var claims = principal.FindAll(type).Take(2).ToArray();
        value = claims.Length == 1 ? claims[0].Value : string.Empty;
        return claims.Length == 1;
    }

    private ClaimsPrincipal? Validate(
        string token,
        InfrastructureAtprotoKeyRing ring,
        string issuer,
        string audience)
    {
        if (!ring.IsReady)
        {
            return null;
        }

        try
        {
            var header = _handler.ReadJwtToken(token).Header;
            if (!string.Equals(header.Alg, Es256, StringComparison.Ordinal)
                || !string.Equals(header.Typ, "JWT", StringComparison.Ordinal)
                || header.Kid is not { Length: > 0 and <= 128 } keyId
                || !ring.HasKey(keyId))
            {
                return null;
            }

            using var ecdsa = ring.CreateEcdsaKey(keyId, includePrivateKey: false);
            var result = _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidateIssuer = true,
                ValidAudience = audience,
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = CreateTransientKey(ecdsa, keyId),
                ValidAlgorithms = [Es256],
                ClockSkew = TimeSpan.FromSeconds(5),
                NameClaimType = JwtRegisteredClaimNames.Sub
            }, out _);
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or SecurityTokenException
                                          or FormatException
                                          or CryptographicException)
        {
            return null;
        }
    }

    private async Task<InfrastructureAtprotoKeyRing> ResolveRingAsync(
        string secretKey,
        CancellationToken cancellationToken)
    {
        var resolved = await secretResolver.ResolveAsync(secretKey, tenantId: null, cancellationToken).ConfigureAwait(false);
        return InfrastructureAtprotoKeyRing.Parse(resolved.Value);
    }

    private static ECDsaSecurityKey CreateTransientKey(ECDsa ecdsa, string keyId) => new(ecdsa)
    {
        KeyId = keyId,
        CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
    };

    private static bool IsBoundedDid(string? did) =>
        did is { Length: >= 5 and <= 2048 }
        && did.StartsWith("did:", StringComparison.Ordinal)
        && did.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character));

    internal static bool IsBoundedCompactJwt(string? token, int maximumBytes) =>
        !string.IsNullOrWhiteSpace(token)
        && token.Length <= maximumBytes
        && token.Count(character => character == '.') == 2
        && token.All(character => character is >= '!' and <= '~');
}

public sealed record AtprotoBootstrapIdentity(
    string Jti,
    Guid TenantId,
    string Did,
    string Classification,
    Guid? CanonicalActorId,
    Guid? ExpectedCanonicalActorConcurrencyStamp);

public sealed record AtprotoSessionBridgeIdentity(
    string ReplayKey,
    Guid TenantId,
    Guid UserId,
    string Did,
    DateTimeOffset ExpiresAt);
