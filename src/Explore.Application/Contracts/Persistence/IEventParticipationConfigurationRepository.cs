using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventParticipationConfigurationRepository
{
    Task<EventParticipationConfiguration?> GetByEventAndTenantAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventParticipationConfiguration>> GetAccountRequiredAsync(
        Guid? tenantId,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        EventParticipationConfiguration configuration,
        CancellationToken cancellationToken);
}
