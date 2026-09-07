using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IEventCustomPropertyProjectionRepository
{
    Task<List<EventCustomPropertyProjection>> GetForEventAsync(
        Guid eventId,
        ExposureLevel? exposureCeiling,
        CancellationToken cancellationToken);

    Task<int> CountActiveDefinitionsForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken);
}
