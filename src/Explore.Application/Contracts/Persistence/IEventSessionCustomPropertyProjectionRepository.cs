using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IEventSessionCustomPropertyProjectionRepository
{
    Task<List<EventSessionCustomPropertyProjection>> GetForSessionAsync(
        Guid eventSessionId,
        ExposureLevel? exposureCeiling,
        CancellationToken cancellationToken);
}
