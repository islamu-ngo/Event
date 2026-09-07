using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IManagedTenantProvisioningOperationRepository
    : IGenericRepository<ManagedTenantProvisioningOperation, Guid>
{
    Task<ManagedTenantProvisioningOperation?> GetByManagedInstanceAndExternalRequestAsync(
        Guid managedInstanceId,
        string externalRequestId,
        CancellationToken cancellationToken = default);

    Task<ManagedTenantProvisioningOperation?> GetByManagedInstanceAndExternalCustomerReferenceAsync(
        Guid managedInstanceId,
        string externalCustomerReference,
        CancellationToken cancellationToken = default);

    Task<ManagedTenantProvisioningOperation?> GetByManagedInstanceAndIdAsNoTrackingAsync(
        Guid managedInstanceId,
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<ManagedTenantProvisioningOperation?> GetByIdAsNoTrackingAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<bool> TryStartAsync(
        Guid operationId,
        Guid expectedOutboxMessageId,
        DateTime startedAt,
        CancellationToken cancellationToken = default);

    Task<bool> TryCancelAsync(
        Guid managedInstanceId,
        Guid operationId,
        DateTime cancelledAt,
        CancellationToken cancellationToken = default);

    Task<bool> TryRetryAsync(
        Guid operationId,
        Guid outboxMessageId,
        string requestJson,
        string? correlationId,
        DateTime retriedAt,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteAsync(
        Guid operationId,
        Guid expectedOutboxMessageId,
        Guid tenantId,
        Guid tenantAdministratorUserId,
        DateTime completedAt,
        CancellationToken cancellationToken = default);

    Task<bool> TryFailAsync(
        Guid operationId,
        Guid expectedOutboxMessageId,
        string failureCode,
        DateTime failedAt,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveReservationsAsync(
        CancellationToken cancellationToken = default,
        Guid? excludedOperationId = null);
}
