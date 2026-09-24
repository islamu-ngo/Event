using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;

namespace Explore.API.Authorization;

public sealed class HttpKeycloakOperatorAuthority(
    IHttpContextAccessor httpContextAccessor,
    IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapStates)
    : IKeycloakOperatorAuthority
{
    public async Task<KeycloakOperatorAuthority> RequireAsync(
        CancellationToken cancellationToken = default)
    {
        var bootstrap = await bootstrapStates.GetCurrent(cancellationToken)
            ?? throw new UnauthorizedAccessException(
                "Instance authority is unavailable.");
        bool setupAuthority = httpContextAccessor.HttpContext?.User.Identities
            .Any(identity =>
                identity.IsAuthenticated
                && string.Equals(
                    identity.AuthenticationType,
                    ApiAuthenticationSchemeNames.SetupSecret,
                    StringComparison.Ordinal)) == true;
        if (setupAuthority)
        {
            return new KeycloakOperatorAuthority(
                bootstrap.Id,
                $"setup:{bootstrap.Generation}",
                bootstrap.Generation,
                IsSetupAuthority: true);
        }

        if (!await adminContext.IsInstanceAdminAsync(cancellationToken)
            || adminContext.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException(
                "Current setup or instance-administrator authority is required.");
        }

        return new KeycloakOperatorAuthority(
            bootstrap.Id,
            userId.ToString("D"),
            bootstrap.Generation,
            IsSetupAuthority: false);
    }
}
