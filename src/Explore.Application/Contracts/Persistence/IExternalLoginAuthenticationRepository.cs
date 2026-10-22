using Explore.Application.Authentication;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads a provider binding using the selected standalone owner or the original Local identity workflow transaction.</summary>
public interface IExternalLoginAuthenticationRepository
{
    Task<UserExternalLogin?> GetByProviderAndKey(
        ProviderAccountKey accountKey, CancellationToken cancellationToken = default);
}
