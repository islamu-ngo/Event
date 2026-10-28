using System.Security.Claims;
using System.Text.Encodings.Web;
using Explore.Application.Contracts.Moderation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

/// <summary>
/// Purpose-bound original-receipt authentication. Its identity has no account claims,
/// role grants or ordinary status authority and is never selected by bearer defaults.
/// </summary>
public sealed class ModerationRecognitionCorrectionReceiptAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IModerationRecognitionCorrectionReceiptAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ModerationRecognitionCorrectionReceipt";
    public const string IntentIdClaim = "recognition_correction_intent_id";
    public const string CredentialPrefix = "ErasureCorrectionReceipt ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(CredentialPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();
        Guid? intentId = await authenticator.AuthenticateAsync(
            authorization[CredentialPrefix.Length..], Context.RequestAborted);
        if (intentId is null) return AuthenticateResult.Fail("Invalid recognition correction receipt.");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(IntentIdClaim, intentId.Value.ToString("D"))], SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = SchemeName;
        return Task.CompletedTask;
    }
}
