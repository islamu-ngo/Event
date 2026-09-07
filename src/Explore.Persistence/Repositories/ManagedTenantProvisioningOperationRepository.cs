using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ManagedTenantProvisioningOperationRepository(ExploreDbContext dbContext)
    : GenericRepository<ManagedTenantProvisioningOperation, Guid>(dbContext),
        IManagedTenantProvisioningOperationRepository
{
    public Task<ManagedTenantProvisioningOperation?> GetByManagedInstanceAndExternalRequestAsync(
        Guid managedInstanceId,
        string externalRequestId,
        CancellationToken cancellationToken = default) =>
        dbContext.ManagedTenantProvisioningOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                operation => operation.ManagedInstanceId == managedInstanceId
                    && operation.ExternalRequestId == externalRequestId,
                cancellationToken);

    public Task<ManagedTenantProvisioningOperation?> GetByManagedInstanceAndIdAsNoTrackingAsync(
        Guid managedInstanceId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        dbContext.ManagedTenantProvisioningOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                operation => operation.ManagedInstanceId == managedInstanceId
                    && operation.Id == operationId,
                cancellationToken);

    public Task<ManagedTenantProvisioningOperation?> GetByManagedInstanceAndExternalCustomerReferenceAsync(
        Guid managedInstanceId,
        string externalCustomerReference,
        CancellationToken cancellationToken = default) =>
        dbContext.ManagedTenantProvisioningOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                operation => operation.ManagedInstanceId == managedInstanceId
                    && operation.ExternalCustomerReference == externalCustomerReference,
                cancellationToken);

    public Task<ManagedTenantProvisioningOperation?> GetByIdAsNoTrackingAsync(
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        dbContext.ManagedTenantProvisioningOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);

    public async Task<bool> TryStartAsync(
        Guid operationId,
        Guid expectedOutboxMessageId,
        DateTime startedAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.ManagedTenantProvisioningOperations
            .Where(operation => operation.Id == operationId
                && operation.CurrentOutboxMessageId == expectedOutboxMessageId
                && operation.Status == ManagedTenantProvisioningStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, ManagedTenantProvisioningStatus.Processing)
                    .SetProperty(operation => operation.StartedAt, startedAt)
                    .SetProperty(operation => operation.UpdatedAt, startedAt),
                cancellationToken) == 1;

    public async Task<bool> TryCancelAsync(
        Guid managedInstanceId,
        Guid operationId,
        DateTime cancelledAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.ManagedTenantProvisioningOperations
            .Where(operation => operation.ManagedInstanceId == managedInstanceId
                && operation.Id == operationId
                && operation.Status == ManagedTenantProvisioningStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, ManagedTenantProvisioningStatus.Cancelled)
                    .SetProperty(operation => operation.CancelledAt, cancelledAt)
                    .SetProperty(operation => operation.RequestJson, (string?)null)
                    .SetProperty(operation => operation.UpdatedAt, cancelledAt),
                cancellationToken) == 1;

    public async Task<bool> TryCompleteAsync(
        Guid operationId,
        Guid expectedOutboxMessageId,
        Guid tenantId,
        Guid tenantAdministratorUserId,
        DateTime completedAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.ManagedTenantProvisioningOperations
            .Where(operation => operation.Id == operationId
                && operation.CurrentOutboxMessageId == expectedOutboxMessageId
                && operation.Status == ManagedTenantProvisioningStatus.Processing)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, ManagedTenantProvisioningStatus.Succeeded)
                    .SetProperty(operation => operation.TenantId, tenantId)
                    .SetProperty(operation => operation.TenantAdministratorUserId, tenantAdministratorUserId)
                    .SetProperty(operation => operation.CompletedAt, completedAt)
                    .SetProperty(operation => operation.FailureCode, (string?)null)
                    .SetProperty(operation => operation.RequestJson, (string?)null)
                    .SetProperty(operation => operation.UpdatedAt, completedAt),
                cancellationToken) == 1;

    public async Task<bool> TryRetryAsync(
        Guid operationId,
        Guid outboxMessageId,
        string requestJson,
        string? correlationId,
        DateTime retriedAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.ManagedTenantProvisioningOperations
            .Where(operation => operation.Id == operationId
                && (operation.Status == ManagedTenantProvisioningStatus.Failed
                    || operation.Status == ManagedTenantProvisioningStatus.Cancelled))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, ManagedTenantProvisioningStatus.Pending)
                    .SetProperty(operation => operation.CurrentOutboxMessageId, outboxMessageId)
                    .SetProperty(operation => operation.RequestJson, requestJson)
                    .SetProperty(operation => operation.CorrelationId, correlationId)
                    .SetProperty(operation => operation.TenantId, (Guid?)null)
                    .SetProperty(operation => operation.TenantAdministratorUserId, (Guid?)null)
                    .SetProperty(operation => operation.FailureCode, (string?)null)
                    .SetProperty(operation => operation.StartedAt, (DateTime?)null)
                    .SetProperty(operation => operation.CompletedAt, (DateTime?)null)
                    .SetProperty(operation => operation.FailedAt, (DateTime?)null)
                    .SetProperty(operation => operation.CancelledAt, (DateTime?)null)
                    .SetProperty(operation => operation.UpdatedAt, retriedAt),
                cancellationToken) == 1;

    public async Task<bool> TryFailAsync(
        Guid operationId,
        Guid expectedOutboxMessageId,
        string failureCode,
        DateTime failedAt,
        CancellationToken cancellationToken = default) =>
        await dbContext.ManagedTenantProvisioningOperations
            .Where(operation => operation.Id == operationId
                && operation.CurrentOutboxMessageId == expectedOutboxMessageId
                && (operation.Status == ManagedTenantProvisioningStatus.Pending
                    || operation.Status == ManagedTenantProvisioningStatus.Processing))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, ManagedTenantProvisioningStatus.Failed)
                    .SetProperty(operation => operation.FailureCode, failureCode)
                    .SetProperty(operation => operation.FailedAt, failedAt)
                    .SetProperty(operation => operation.RequestJson, (string?)null)
                    .SetProperty(operation => operation.UpdatedAt, failedAt),
                cancellationToken) == 1;

    public Task<int> CountActiveReservationsAsync(
        CancellationToken cancellationToken = default,
        Guid? excludedOperationId = null) =>
        dbContext.ManagedTenantProvisioningOperations
            .AsNoTracking()
            .CountAsync(
                operation => operation.Id != excludedOperationId
                    && (operation.Status == ManagedTenantProvisioningStatus.Pending
                        || operation.Status == ManagedTenantProvisioningStatus.Processing),
                cancellationToken);
}
