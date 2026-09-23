using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Keycloak;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public sealed class RelationalKeycloakOperationCoordinator(
    ExploreDbContext db,
    IKeycloakOperationRepository repository,
    IDbContextFactory<ExploreDbContext> contextFactory)
    : IKeycloakOperationCoordinator
{
    public async Task<T> ExecuteAsync<T>(
        KeycloakOperation operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        string lockKey =
            $"explore:keycloak-operation:{operation.Target.InstanceId:D}:"
            + $"{operation.Target.AuthorityKey}:{operation.Target.Realm}";
        await using IAsyncDisposable lease =
            await RelationalNamedLock.AcquireSessionAsync(
                db,
                lockKey,
                cancellationToken);

        KeycloakOperation persisted =
            await repository.GetAsync(operation.Id, cancellationToken)
            ?? throw new InvalidOperationException(
                "The operation intent must be persisted before provider transmission.");
        if (persisted.State != KeycloakOperationState.Applying
            || persisted.ConcurrencyStamp != operation.ConcurrencyStamp
            || persisted.Target != operation.Target
            || persisted.SetupGeneration != operation.SetupGeneration
            || !string.Equals(
                persisted.Actor,
                operation.Actor,
                StringComparison.Ordinal)
            || !string.Equals(
                persisted.Digest,
                operation.Digest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The persisted operation intent no longer matches the approved send.");
        }

        bool hasOverlap = await repository.HasUnresolvedOverlapAsync(
            operation.Target,
            excludedOperationId: operation.Id,
            cancellationToken);
        if (hasOverlap)
        {
            throw new KeycloakOperationOverlapException();
        }

        return await action(cancellationToken);
    }

    public async Task<T> ExecuteReconciliationAsync<T>(
        KeycloakOperation operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        string lockKey =
            $"explore:keycloak-operation:{operation.Target.InstanceId:D}:"
            + $"{operation.Target.AuthorityKey}:{operation.Target.Realm}";
        await using IAsyncDisposable lease =
            await RelationalNamedLock.AcquireSessionAsync(
                db,
                lockKey,
                cancellationToken);
        return await action(cancellationToken);
    }

    public async Task RequestCancellationAsync(
        Guid operationId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using ExploreDbContext cancellationDb =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        var cancellationRepository =
            new KeycloakOperationRepository(cancellationDb);
        KeycloakOperation operation =
            await cancellationRepository.GetAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Keycloak operation {operationId:D} was not found.");
        Guid expectedConcurrencyStamp = operation.ConcurrencyStamp;
        operation.RequestCancellation(requestedAtUtc);
        await cancellationRepository.SaveAsync(
            operation,
            expectedConcurrencyStamp,
            cancellationToken);
    }
}
