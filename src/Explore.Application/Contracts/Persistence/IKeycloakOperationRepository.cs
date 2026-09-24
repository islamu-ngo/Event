using Explore.Domain.Keycloak;

namespace Explore.Application.Contracts.Persistence;

public interface IKeycloakOperationRepository
{
    Task<KeycloakOperation?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        KeycloakOperation operation,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        KeycloakOperation operation,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default);

    Task<bool> HasUnresolvedOverlapAsync(
        KeycloakTarget target,
        Guid? excludedOperationId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeycloakOperation>> FindSettledRetentionEligibleAsync(
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        KeycloakOperation operation,
        CancellationToken cancellationToken = default);
}
