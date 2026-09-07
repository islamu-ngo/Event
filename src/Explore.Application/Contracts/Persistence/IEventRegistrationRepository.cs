using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventRegistrationRepository : IGenericRepository<EventRegistration, Guid>
{
    Task<IReadOnlyList<EventRegistration>> GetLocationAccessCoverageAsync(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken);
}
