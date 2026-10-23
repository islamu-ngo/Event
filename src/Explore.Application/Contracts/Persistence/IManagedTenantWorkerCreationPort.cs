using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Creates the complete graph selected by one genuinely admitted managed-process
/// generation. Request payloads and ordinary human authority cannot select this owner.
/// </summary>
public interface IManagedTenantWorkerCreationPort
{
    Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> CreateAsync(
        Guid operationId, Guid outboxMessageId, CancellationToken cancellationToken = default);
}

/// <summary>Original Runtime delivery custody; it confers no graph or identity permissions.</summary>
public interface IManagedTenantWorkerCreationAdmission
{
    ValueTask<IManagedTenantWorkerCreationInvocation> AcquireAsync(Guid operationId, Guid outboxMessageId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Opaque original delivery. The concrete issuer must verify its own issued object;
/// implementing this interface or copying its identifiers cannot admit a worker.
/// </summary>
public interface IManagedTenantWorkerCreationInvocation : IAsyncDisposable
{
    Guid OperationId { get; }
    Guid OutboxMessageId { get; }
    Task RequireCurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>Selects a distinct original creator only after concrete Runtime admission.</summary>
public interface IManagedTenantWorkerCreationOwnerFactory
{
    ValueTask<IManagedTenantWorkerCreationOwner> CreateAsync(IManagedTenantWorkerCreationInvocation invocation,
        CancellationToken cancellationToken = default);
}

/// <summary>One complete durable request; no arbitrary entities, subject selectors or callbacks are accepted.</summary>
public interface IManagedTenantWorkerCreationOwner : IAsyncDisposable
{
    Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> CreateAsync(
        CancellationToken cancellationToken = default);
}
