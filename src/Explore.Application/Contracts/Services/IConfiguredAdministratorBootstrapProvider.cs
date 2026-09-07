using Explore.Application.Authentication;
using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IConfiguredAdministratorBootstrapProvider
{
    /// <summary>
    /// Returns a fresh binding only after upstream authentication established the supplied account.
    /// Implementations must use local configuration only because resolution occurs in a transaction.
    /// </summary>
    Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
        ProviderAccountKey authenticatedAccount,
        CancellationToken cancellationToken = default);
}
