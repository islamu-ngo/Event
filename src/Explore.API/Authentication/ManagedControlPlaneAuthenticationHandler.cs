using System.Security.Claims;
using System.Text.Encodings.Web;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Management;
using Explore.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public sealed class ManagedControlPlaneAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ManagedControlPlaneOptions> managedControlPlaneOptions,
    IManagedControlPlaneRegistrationRepository registrationRepository)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!managedControlPlaneOptions.Value.Enabled)
        {
            return AuthenticateResult.NoResult();
        }

        if (!Request.Headers.TryGetValue(ManagedControlPlaneAuthenticationDefaults.HeaderName, out var values)
            || values.Count != 1)
        {
            return AuthenticateResult.NoResult();
        }

        var rawCredential = values[0];
        if (string.IsNullOrWhiteSpace(rawCredential)
            || rawCredential.Length > 1024
            || !ApiKeyHashing.TryParsePersistedApiKey(rawCredential, out var keyId, out var secret))
        {
            return AuthenticateResult.Fail("Invalid managed Control Plane credential.");
        }

        var registration = await registrationRepository.GetActiveByControlPlaneKeyIdAsync(
            keyId,
            Context.RequestAborted);
        if (registration is null
            || !ApiKeyHashing.MatchesHash(secret, registration.ControlPlaneToEventSecretHash))
        {
            return AuthenticateResult.Fail("Invalid managed Control Plane credential.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, $"managed-control-plane:{keyId}"),
            new Claim(ManagedControlPlaneAuthenticationDefaults.ManagedInstanceIdClaim, registration.ManagedInstanceId.ToString("D")),
            new Claim(ManagedControlPlaneAuthenticationDefaults.ScopeClaim, ManagedControlPlaneContract.ControlPlaneReadScope),
            new Claim(ManagedControlPlaneAuthenticationDefaults.ScopeClaim, ManagedControlPlaneContract.ControlPlaneWriteScope)
        };
        var identity = new ClaimsIdentity(
            claims,
            ApiAuthenticationSchemeNames.ManagedControlPlane,
            ClaimTypes.Name,
            ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(
            principal,
            ApiAuthenticationSchemeNames.ManagedControlPlane));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ApiAuthenticationSchemeNames.ManagedControlPlane;
        return Task.CompletedTask;
    }
}
