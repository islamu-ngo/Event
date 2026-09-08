using System.Security.Claims;
using System.Text.Encodings.Web;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public sealed class PrivacyErasureReceiptAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IPrivacyErasureService privacyErasureService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string IntentIdClaim = "privacy_erasure_intent_id";
    private const string CredentialPrefix = "ErasureReceipt ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(CredentialPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        string receipt = authorization[CredentialPrefix.Length..];
        Guid? intentId = await privacyErasureService.AuthenticateReceiptAsync(
            receipt,
            Context.RequestAborted);
        if (intentId is null)
        {
            return AuthenticateResult.Fail("Invalid privacy-erasure receipt.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(IntentIdClaim, intentId.Value.ToString("D"))],
            ApiAuthenticationSchemeNames.PrivacyErasureReceipt);
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            ApiAuthenticationSchemeNames.PrivacyErasureReceipt));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ApiAuthenticationSchemeNames.PrivacyErasureReceipt;
        return Task.CompletedTask;
    }
}
