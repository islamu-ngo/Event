using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Explore.API.Authentication;

/// <summary>Request-local custody of metadata emitted only after native credential validation.</summary>
internal sealed class HumanAuthenticationLifetimeObserver(
    IHttpContextAccessor contexts, TimeProvider clock) : IHumanAuthenticationLifetimeObserver
{
    public async ValueTask<HumanAuthenticationLifetime?> ObserveAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        cancellationToken.ThrowIfCancellationRequested();
        HttpContext? context = contexts.HttpContext;
        if (context is null)
            return null;

        // A token-validated event precedes final ticket success. Never let a later failure, a
        // different selected scheme, or a same-claims replacement principal inherit that event.
        AuthenticateResult result = await context.AuthenticateAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Receipt? receipt = context.Features.Get<Receipt>();
        if (!result.Succeeded || result.Ticket is null || receipt is null
            || !string.Equals(result.Ticket.AuthenticationScheme, receipt.Lifetime.AuthenticationScheme, StringComparison.Ordinal)
            || !ReferenceEquals(principal.GetAmbientPlatformIdentity(), receipt.Identity)
            || !ReferenceEquals(context.User.GetAmbientPlatformIdentity(), receipt.Identity)
            || !ReferenceEquals(result.Principal?.GetAmbientPlatformIdentity(), receipt.Identity)
            || principal.GetProviderIdentity()?.AccountKey != receipt.Lifetime.AccountKey
            || !receipt.Lifetime.IsValidAt(clock.GetUtcNow()))
            return null;

        return receipt.Lifetime;
    }

    internal static void CaptureJwt(TokenValidatedContext context)
    {
        TokenValidationParameters parameters = context.Options.TokenValidationParameters;
        if (context.Result?.Failure is not null
            || context.Scheme.Name is not (JwtBearerDefaults.AuthenticationScheme or ApiAuthenticationSchemeNames.LocalIdentity)
            || !parameters.ValidateLifetime || !parameters.RequireExpirationTime
            || !parameters.RequireSignedTokens || !parameters.ValidateIssuerSigningKey
            || !parameters.ValidateIssuer || !parameters.ValidateAudience
            || parameters.LifetimeValidator is not null || parameters.SignatureValidator is not null
            || parameters.SignatureValidatorUsingConfiguration is not null)
            return;

        Capture(context.HttpContext, context.Principal, context.SecurityToken,
            context.Scheme.Name, parameters.ClockSkew);
    }

    internal static void CaptureAtprotoSession(HttpContext context, ClaimsPrincipal principal, SecurityToken token) =>
        Capture(context, principal, token, ApiAuthenticationSchemeNames.AtprotoSession, AtprotoJwtService.ValidationClockSkew);

    private static void Capture(HttpContext context, ClaimsPrincipal? principal, SecurityToken token,
        string scheme, TimeSpan skew)
    {
        ClaimsIdentity? identity = principal?.GetAmbientPlatformIdentity();
        ProviderAccountKey? accountKey = principal?.GetProviderIdentity()?.AccountKey;
        string? encodedPayload = token switch
        {
            JsonWebToken current => current.EncodedPayload,
            JwtSecurityToken legacy => legacy.RawPayload,
            _ => null
        };
        if (identity is null || accountKey is null || string.IsNullOrEmpty(encodedPayload) || skew < TimeSpan.Zero)
            return;
        var localSession = scheme == ApiAuthenticationSchemeNames.LocalIdentity
            ? principal!.TryGetLocalSessionAuthority() : null;
        if (scheme == ApiAuthenticationSchemeNames.LocalIdentity && localSession is null)
            return;

        // Read the already authenticated raw payload, not mutable principal claims or ticket properties.
        // The token handlers support two representations; neither admits an unknown metadata source.
        try
        {
            using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(encodedPayload));
            JsonElement payload = document.RootElement;
            if (payload.ValueKind != JsonValueKind.Object)
                return;
            JsonProperty[] expiries = payload.EnumerateObject().Where(property => property.NameEquals("exp")).ToArray();
            JsonProperty[] starts = payload.EnumerateObject().Where(property => property.NameEquals("nbf")).ToArray();
            if (expiries.Length != 1 || expiries[0].Value.ValueKind != JsonValueKind.Number
                || !expiries[0].Value.TryGetInt64(out long expirySeconds) || starts.Length > 1)
                return;

            DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeSeconds(expirySeconds);
            DateTimeOffset? notBefore = null;
            if (starts.Length == 1)
            {
                if (starts[0].Value.ValueKind != JsonValueKind.Number
                    || !starts[0].Value.TryGetInt64(out long startSeconds))
                    return;
                notBefore = DateTimeOffset.FromUnixTimeSeconds(startSeconds);
            }
            if (expiresAt.UtcDateTime != token.ValidTo
                || (notBefore.HasValue && (notBefore.Value.UtcDateTime != token.ValidFrom || notBefore > expiresAt)))
                return;

            // Reject out-of-range bounds rather than overflowing or turning them into an unlimited grant.
            _ = expiresAt.Add(skew);
            _ = notBefore?.Subtract(skew);
            context.Features.Set(new Receipt(identity,
                new HumanAuthenticationLifetime(scheme, accountKey, notBefore, expiresAt, skew, localSession)));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            // Malformed lifetime metadata produces no observation. Existing authentication policy is unchanged.
        }
    }

    private sealed record Receipt(ClaimsIdentity Identity, HumanAuthenticationLifetime Lifetime);
}
