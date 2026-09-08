using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;

namespace Explore.Infrastructure.Services;

public sealed class DisabledConfiguredAdministratorBootstrapProvider
    : IConfiguredAdministratorBootstrapProvider
{
    public Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
        ProviderAccountKey authenticatedAccount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticatedAccount);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ConfiguredAdministratorBootstrapBinding?>(null);
    }
}
