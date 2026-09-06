// ABOUTME: Authenticates only a transient machine and claims its durable replay identifier before dispatch.
// ABOUTME: Creates no platform-user, DID or tenant identity and never falls back to another scheme.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public sealed class AtprotoTransientAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AtprotoTransientAssertionValidator validator,
    IAtprotoTransientAssertionReplayRepository replay)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AtprotoTransientAuthenticationDefaults.IsPrivatePath(Request.Path)
            || Context.Items[AtprotoTransientAuthenticationDefaults.BufferedBodyKey] is not byte[] body
            || Request.Headers[AtprotoTransientAuthenticationDefaults.HeaderName] is not { Count: 1 } values
            || values[0] is not { } token)
            return AuthenticateResult.Fail("Invalid transient assertion.");
        var assertion = await validator.ValidateAsync(token, Request, body, Context.RequestAborted);
        if (assertion is null || !await replay.TryClaimAsync(AtprotoTransientAssertionReplay.CreateFromAssertionId(
                assertion.Jti, assertion.AcceptanceExpiresAtUnixMilliseconds), Context.RequestAborted))
            return AuthenticateResult.Fail("Invalid transient assertion.");
        Context.Items[AtprotoTransientAuthenticationDefaults.VerifiedPurposeKey] = assertion.Purpose;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", AtprotoTransientAuthenticationDefaults.Subject),
            new Claim("use", AtprotoTransientAuthenticationDefaults.Use)
        ], AtprotoTransientAuthenticationDefaults.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        AtprotoTransientRequestBoundary.WriteProblemAsync(Context, StatusCodes.Status401Unauthorized);
}
