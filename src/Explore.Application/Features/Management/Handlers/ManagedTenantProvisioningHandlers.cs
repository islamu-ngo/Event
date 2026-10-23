using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;

namespace Explore.Application.Features.Management.Handlers.Commands;

public static class ManagedTenantProvisioningOutboxEvents
{
    public const string ProcessRequested = "ManagedTenantProvisioningProcessRequested";
}

public sealed class ScheduleManagedTenantProvisioningCommandHandler(
    IManagedTenantProvisioningSchedulingPort scheduling)
    : ICommandHandler<ScheduleManagedTenantProvisioningCommand, BaseCommandResponse<ManagementTenantProvisioningOperationDto>>
{
    public Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>> ExecuteAsync(
        ScheduleManagedTenantProvisioningCommand request, CancellationToken cancellationToken = default) =>
        scheduling.ScheduleAsync(request, cancellationToken);
}

public sealed class CancelManagedTenantProvisioningOperationCommandHandler(
    IManagedTenantProvisioningOperationRepository operationRepository)
    : ICommandHandler<CancelManagedTenantProvisioningOperationCommand,
        BaseCommandResponse<ManagementTenantProvisioningOperationDto>>
{
    public async Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>> ExecuteAsync(
        CancelManagedTenantProvisioningOperationCommand request,
        CancellationToken cancellationToken = default)
    {
        DateTime cancelledAt = DateTime.UtcNow;
        bool cancelled = await operationRepository.TryCancelAsync(
            request.ManagedInstanceId,
            request.OperationId,
            cancelledAt,
            cancellationToken);
        ManagedTenantProvisioningOperation? operation =
            await operationRepository.GetByManagedInstanceAndIdAsNoTrackingAsync(
                request.ManagedInstanceId,
                request.OperationId,
                cancellationToken);
        if (operation is null)
        {
            return Failure("tenant_provisioning_operation_not_found", "Tenant provisioning operation was not found.");
        }

        if (!cancelled && operation.Status != ManagedTenantProvisioningStatus.Cancelled)
        {
            return Failure(
                "tenant_provisioning_cancellation_conflict",
                "Only a pending tenant provisioning operation can be cancelled.");
        }

        return BaseCommandResponse.Success(
            ManagedTenantProvisioningRequestCodec.ToDto(operation),
            cancelled
                ? "Managed tenant provisioning cancelled."
                : "Managed tenant provisioning was already cancelled.");
    }

    private static BaseCommandResponse<ManagementTenantProvisioningOperationDto> Failure(
        string failureCode,
        string message) => BaseCommandResponse.Failure<ManagementTenantProvisioningOperationDto>(
            failureCode,
            message,
            [message]);
}

public sealed class ProcessManagedTenantProvisioningOperationCommandHandler(
    IManagedTenantWorkerCreationPort creator)
    : ICommandHandler<ProcessManagedTenantProvisioningOperationCommand, bool>
{
    public async Task<bool> ExecuteAsync(
        ProcessManagedTenantProvisioningOperationCommand request, CancellationToken cancellationToken = default)
    {
        // The admitted creator settles success or policy refusal on its original operation transaction.
        // Lost custody throws; it must never be converted into successful message consumption.
        await creator.CreateAsync(request.OperationId, request.OutboxMessageId, cancellationToken);
        return true;
    }
}

public sealed class ReconcileManagedTenantProvisioningDeadLetterCommandHandler(
    IManagedTenantProvisioningOperationRepository operationRepository)
    : ICommandHandler<ReconcileManagedTenantProvisioningDeadLetterCommand, bool>
{
    public const string FailureCode = "tenant_provisioning_dispatch_exhausted";

    public async Task<bool> ExecuteAsync(
        ReconcileManagedTenantProvisioningDeadLetterCommand request,
        CancellationToken cancellationToken = default)
    {
        ManagedTenantProvisioningOperation? operation =
            await operationRepository.GetByIdAsNoTrackingAsync(request.OperationId, cancellationToken);
        if (operation is null
            || operation.CurrentOutboxMessageId != request.OutboxMessageId
            || operation.Status is ManagedTenantProvisioningStatus.Succeeded
                or ManagedTenantProvisioningStatus.Failed
                or ManagedTenantProvisioningStatus.Cancelled)
        {
            return true;
        }

        await operationRepository.TryFailAsync(
            operation.Id,
            request.OutboxMessageId,
            FailureCode,
            DateTime.UtcNow,
            cancellationToken);
        return true;
    }
}
