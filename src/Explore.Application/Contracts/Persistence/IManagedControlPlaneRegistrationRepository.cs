using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IManagedControlPlaneRegistrationRepository
    : IGenericRepository<ManagedControlPlaneRegistration, Guid>
{
    Task<ManagedControlPlaneRegistration?> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<ManagedControlPlaneRegistration?> GetActiveByControlPlaneKeyIdAsync(
        string keyId,
        CancellationToken cancellationToken = default);
}
