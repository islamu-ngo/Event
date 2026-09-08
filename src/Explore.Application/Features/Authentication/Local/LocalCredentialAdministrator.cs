
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Authentication.Local;

internal static class LocalCredentialAdministrator
{
    internal static async Task<Guid?> ResolveAsync(
        IAdminContext adminContext, IPlatformUserRoleRepository platformUserRoles, CancellationToken cancellationToken)
    {
        Guid? userId = await adminContext.ResolveUserIdAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (userId is null || userId == Guid.Empty)
        {
            return null;
        }
        bool authorized = await platformUserRoles.IsUserPlatformAdmin(userId.Value).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return authorized ? userId : null;
    }
}
