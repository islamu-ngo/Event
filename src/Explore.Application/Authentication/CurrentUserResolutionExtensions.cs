using System.Security.Claims;
using Explore.Application.Features.Users.Requests.Queries;
using MediatR;

namespace Explore.Application.Authentication;

/// <summary>
/// Resolution for principals whose provider subject is not itself a platform user id — ATProto DIDs and
/// Google subjects, chiefly. It is an extension over <see cref="IMediator"/> rather than an injected service
/// because both inputs, the mediator and the principal, are already in hand at every call site; adding a
/// constructor dependency would buy nothing and adding a base-class helper would hide the query behind
/// inheritance.
/// </summary>
public static class CurrentUserResolutionExtensions
{
    /// <summary>
    /// Returns the local user id for the caller, or <see langword="null"/> when the principal carries no
    /// provider identity or no local account is linked yet. A null result is an authentication outcome for
    /// the caller to map — never a reason to fall back to a different identity source.
    /// </summary>
    public static async Task<Guid?> ResolveCurrentUserIdAsync(
        this IMediator mediator,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.GetAmbientPlatformIdentity() is null)
        {
            return null;
        }

        var providerIdentity = principal.GetProviderIdentity();
        if (providerIdentity is not null)
        {
            Guid? linkedUserId = await mediator.Send(
                new ResolveCurrentUserIdByIdentityRequest
                {
                    Provider = providerIdentity.Provider,
                    ProviderId = providerIdentity.AccountKey.Value,
                    Email = null,
                    EmailVerified = false,
                },
                cancellationToken);
            return linkedUserId;
        }

        return principal.GetPlatformUserId();
    }
}
