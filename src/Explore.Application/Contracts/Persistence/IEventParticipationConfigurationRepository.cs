// ABOUTME: Application persistence contract for exact event participation configuration mutations.
// ABOUTME: Returns Domain entities so handlers retain validation, mapping, and tenant-bound concurrency ownership.

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
