using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Explore.API.Hateoas;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public sealed class AtprotoBootstrapAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AtprotoJwtService jwtService,
    IAtprotoBootstrapReplayRepository replayStore,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!HttpMethods.IsPost(Request.Method)
            || !string.Equals(Request.Path.Value, AtprotoJwtOptions.BridgePath, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        if (!Request.Headers.TryGetValue(AtprotoJwtOptions.BootstrapHeaderName, out var values)
            || values.Count != 1)
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await jwtService.ValidateBootstrapAsync(
            values[0]!,
            tenantContext.TenantId,
            Request.Method,
            Request.Path.Value!,
            Context.RequestAborted).ConfigureAwait(false);
        if (identity is null
            || !await replayStore.TryConsumeAsync(
                identity.Jti,
                identity.TenantId,
                timeProvider.GetUtcNow().AddMinutes(2),
                Context.RequestAborted).ConfigureAwait(false))
        {
            return AuthenticateResult.Fail("ATProto bootstrap authentication failed.");
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("client_id", "event-blazor-bff"),
            new Claim(AtprotoJwtOptions.TenantClaim, identity.TenantId.ToString("D")),
            new Claim(AtprotoJwtOptions.DidClaim, identity.Did),
            new Claim(AtprotoJwtOptions.ClassificationClaim, identity.Classification)
        ], ApiAuthenticationSchemeNames.AtprotoBootstrap));
        if (identity.CanonicalActorId is { } canonicalActorId)
        {
            ((ClaimsIdentity)principal.Identity!).AddClaims([
                new(AtprotoJwtOptions.CanonicalActorIdClaim, canonicalActorId.ToString("D")),
                new(AtprotoJwtOptions.ExpectedCanonicalActorConcurrencyStampClaim, identity.ExpectedCanonicalActorConcurrencyStamp!.Value.ToString("D"))
            ]);
        }
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

public sealed class AtprotoSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AtprotoJwtService jwtService,
    IAtprotoBootstrapReplayRepository replayStore,
    ITenantContext tenantContext)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values)
            || values.Count != 1
            || !values[0]!.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = values[0]!["Bearer ".Length..].Trim();
        var principal = await jwtService.ValidateSessionAsync(
            token,
            tenantContext.TenantId,
            Context.RequestAborted).ConfigureAwait(false);
        if (principal is null)
        {
            return AuthenticateResult.Fail("ATProto session authentication failed.");
        }

        if (IsCurrentSessionEndpoint())
        {
            if (!Request.Headers.TryGetValue(AtprotoJwtOptions.SessionBridgeHeaderName, out var bridgeValues)
                || bridgeValues.Count != 1
                || !Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
                || principal.FindFirstValue(AtprotoJwtOptions.DidClaim) is not { } did)
            {
                return AuthenticateResult.Fail("ATProto session bridge authentication failed.");
            }

            var bridgeIdentity = await jwtService.ValidateSessionBridgeAsync(
                bridgeValues[0]!,
                tenantContext.TenantId,
                userId,
                did,
                Request.Method,
                Request.Path.Value!,
                Context.RequestAborted).ConfigureAwait(false);
            if (bridgeIdentity is null
                || !await replayStore.TryConsumeAsync(
                    bridgeIdentity.ReplayKey,
                    bridgeIdentity.TenantId,
                    bridgeIdentity.ExpiresAt,
                    Context.RequestAborted).ConfigureAwait(false))
            {
                return AuthenticateResult.Fail("ATProto session bridge authentication failed.");
            }
        }

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private bool IsCurrentSessionEndpoint()
    {
        var routeName = Context.GetEndpoint()?.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName;
        if (routeName is RouteNames.GetCurrentAtprotoSession
            or RouteNames.RefreshCurrentAtprotoSession
            or RouteNames.DeleteCurrentAtprotoSession)
        {
            return true;
        }

        return string.Equals(
            Request.Path.Value?.TrimEnd('/'),
            AtprotoJwtOptions.CurrentSessionPath,
            StringComparison.OrdinalIgnoreCase);
    }
}
