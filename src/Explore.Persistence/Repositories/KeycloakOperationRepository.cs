using Explore.Application.Contracts.Persistence;
using Explore.Domain.Keycloak;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class KeycloakOperationRepository(ExploreDbContext db)
    : IKeycloakOperationRepository
{
    public Task<KeycloakOperation?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        db.KeycloakOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                operation => operation.Id == id,
                cancellationToken);

    public async Task AddAsync(
        KeycloakOperation operation,
        CancellationToken cancellationToken = default)
    {
        db.KeycloakOperations.Add(operation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(
        KeycloakOperation operation,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default)
    {
        KeycloakOperation? tracked = db.KeycloakOperations.Local
            .SingleOrDefault(candidate => candidate.Id == operation.Id);
        KeycloakOperation entity = operation;
        if (tracked is not null && !ReferenceEquals(tracked, operation))
        {
            db.Entry(tracked).CurrentValues.SetValues(operation);
            entity = tracked;
        }
        else if (tracked is null)
        {
            db.KeycloakOperations.Attach(operation);
        }

        db.Entry(entity)
            .Property(candidate => candidate.ConcurrencyStamp)
            .OriginalValue = expectedConcurrencyStamp;
        db.Entry(entity).State = EntityState.Modified;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasUnresolvedOverlapAsync(
        KeycloakTarget target,
        Guid? excludedOperationId = null,
        CancellationToken cancellationToken = default) =>
        db.KeycloakOperations
            .AsNoTracking()
            .AnyAsync(
                operation =>
                    (!excludedOperationId.HasValue
                     || operation.Id != excludedOperationId.Value)
                    && operation.Target.InstanceId == target.InstanceId
                    && operation.Target.AuthorityKey == target.AuthorityKey
                    && operation.Target.Authority == target.Authority
                    && operation.Target.Realm == target.Realm
                    && (operation.State == KeycloakOperationState.Applying
                        || operation.State == KeycloakOperationState.OutcomeUnknown),
                cancellationToken);

    public async Task<IReadOnlyList<KeycloakOperation>>
        FindSettledRetentionEligibleAsync(
            DateTimeOffset beforeUtc,
            CancellationToken cancellationToken = default)
    {
        long cutoffTicks = beforeUtc.ToUniversalTime().UtcTicks;
        return await db.KeycloakOperations
            .AsNoTracking()
            .Where(operation =>
                operation.SettledAtUtcTicks != null
                && operation.SettledAtUtcTicks < cutoffTicks
                && (operation.State == KeycloakOperationState.Verified
                    || operation.State == KeycloakOperationState.PartiallyApplied
                    || operation.State == KeycloakOperationState.Conflict
                    || operation.State == KeycloakOperationState.FailedBeforeWrite
                    || operation.State == KeycloakOperationState.Cancelled
                    || operation.State == KeycloakOperationState.Expired))
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        KeycloakOperation operation,
        CancellationToken cancellationToken = default)
    {
        db.KeycloakOperations.Remove(operation);
        await db.SaveChangesAsync(cancellationToken);
    }
}
