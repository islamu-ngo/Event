using Explore.Application.Constants;
using Explore.Application.Features.InstanceOnboarding.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>Provider setup requires the actual setup ticket, not a human with a setup header.</summary>
public sealed class ProviderConfigurationSetupAuthority(
    IHttpContextAccessor contexts, ITrustedSetupSettingsAuthority authority)
    : IProviderConfigurationSetupAuthority
{
    public async Task<ITrustedSetupSettingsAuthorityLease> CaptureAsync(
        TrustedSetupSettingsSource source, CancellationToken cancellationToken)
    {
        var request = contexts.HttpContext
            ?? throw new UnauthorizedAccessException("Provider setup requires its original request.");
        var principal = request.User;
        var ticket = await request.AuthenticateAsync();
        if (!ticket.Succeeded || ticket.Ticket?.AuthenticationScheme != ApiAuthenticationSchemeNames.SetupSecret
            || ticket.Principal is null || !ticket.Principal.Identities.Any(identity =>
                identity.IsAuthenticated && principal.Identities.Contains(identity)))
            throw new UnauthorizedAccessException("Provider setup requires its actual setup-secret ticket.");
        return await authority.CaptureAsync(source, cancellationToken);
    }
}
