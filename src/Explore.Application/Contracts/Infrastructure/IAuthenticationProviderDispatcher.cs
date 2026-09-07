using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Infrastructure;

public interface IAuthenticationProviderDispatcher
{
    Task<AuthenticationProviderKind> GetActivePrimaryProviderAsync(
        CancellationToken cancellationToken);
}
