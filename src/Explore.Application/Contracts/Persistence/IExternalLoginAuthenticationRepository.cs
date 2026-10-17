using Explore.Application.Authentication;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads a committed provider binding for standalone principal resolution, outside identity-write transactions.</summary>
public interface IExternalLoginAuthenticationRepository
{
    Task<UserExternalLogin?> GetByProviderAndKey(
        ProviderAccountKey accountKey, CancellationToken cancellationToken = default);
}
