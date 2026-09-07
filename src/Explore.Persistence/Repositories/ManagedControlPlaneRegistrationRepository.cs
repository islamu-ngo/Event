using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ManagedControlPlaneRegistrationRepository(ExploreDbContext dbContext)
    : GenericRepository<ManagedControlPlaneRegistration, Guid>(dbContext),
        IManagedControlPlaneRegistrationRepository
{
    public Task<ManagedControlPlaneRegistration?> GetCurrentAsync(
        CancellationToken cancellationToken = default) =>
        dbContext.ManagedControlPlaneRegistrations
            .OrderByDescending(entity => entity.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ManagedControlPlaneRegistration?> GetActiveByControlPlaneKeyIdAsync(
        string keyId,
        CancellationToken cancellationToken = default) =>
        dbContext.ManagedControlPlaneRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.ControlPlaneToEventKeyId == keyId
                    && entity.Status == ManagedControlPlaneRegistrationStatus.Registered
                    && entity.ControlPlaneToEventCredentialExpiresAt > DateTime.UtcNow,
                cancellationToken);
}
