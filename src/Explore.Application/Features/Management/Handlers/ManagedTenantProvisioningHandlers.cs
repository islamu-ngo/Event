// ABOUTME: Schedules, reads, cancels, and processes durable Event-owned tenant provisioning operations.
// ABOUTME: Rejects mode, capacity, trust, and bootstrap policy before mutation and dispatches only an outbox pointer.

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.Management.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.ManagedProviderProvisioning;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Management.Handlers.Commands;

public static class ManagedTenantProvisioningOutboxEvents
{
    public const string ProcessRequested = "ManagedTenantProvisioningProcessRequested";
}

public sealed class ScheduleManagedTenantProvisioningCommandHandler(
    IDeploymentModeProvider deploymentModeProvider,
    IManagedControlPlaneRegistrationRepository registrationRepository,
    IManagedTenantProvisioningOperationRepository operationRepository,
    IExternalBindingRepository externalBindingRepository,
    ITenantRepository tenantRepository,
    IOutboxRepository outboxRepository,
    ISettingMutationLock mutationLock,
    TenantActivationCapacityPolicy capacityPolicy,
    ManagedTenantProvisioningPreflight preflight)
    : IRequestHandler<ScheduleManagedTenantProvisioningCommand,
        BaseCommandResponse<ManagementTenantProvisioningOperationDto>>
{
    public async Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>> Handle(
        ScheduleManagedTenantProvisioningCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ManagementTenantProvisioningRequestValidator();
        await validator.ValidateAndThrowAsync(request.Request, cancellationToken);
        ManagementTenantProvisioningRequestDto normalized =
            ManagedTenantProvisioningRequestCodec.Normalize(request.Request);
        string requestHash = ManagedTenantProvisioningRequestCodec.ComputeHash(normalized);

        BaseCommandResponse<ManagementTenantProvisioningOperationDto>? authorityFailure =
            await EvaluateInstanceSchedulingPolicyAsync(request.ManagedInstanceId, cancellationToken);
        if (authorityFailure is not null)
            return authorityFailure;

        ReplayResolution replay = await ResolveReplayAsync(
            request.ManagedInstanceId,
            normalized,
            requestHash,
            allowTerminalRetry: true,
            cancellationToken);
        if (replay.ImmediateResponse is not null)
        {
            return replay.ImmediateResponse;
        }

        BaseCommandResponse<ManagementTenantProvisioningOperationDto>? instancePolicyFailure =
            await EvaluateInstanceSchedulingPolicyAsync(
                request.ManagedInstanceId,
                cancellationToken);
        if (instancePolicyFailure is not null)
        {
            return instancePolicyFailure;
        }

        CommittedRecoveryResolution retryRecovery = await ResolveCommittedRecoveryAsync(
            replay.RetryCandidate,
            normalized,
            cancellationToken);
        if (retryRecovery == CommittedRecoveryResolution.Conflict)
        {
            return RecoveryConflict();
        }

        if (retryRecovery != CommittedRecoveryResolution.Owned)
        {
            BaseCommandResponse<ManagementTenantProvisioningOperationDto>? tenantPolicyFailure =
                await EvaluateTenantCreationPolicyAsync(
                    normalized,
                    includeCapacity: false,
                    cancellationToken);
            if (tenantPolicyFailure is not null)
            {
                return tenantPolicyFailure;
            }
        }

        return await mutationLock.ExecuteAsync(
            GovernanceSettingKeys.Deployment.Mode,
            async token =>
            {
                ReplayResolution concurrentReplay = await ResolveReplayAsync(
                    request.ManagedInstanceId,
                    normalized,
                    requestHash,
                    allowTerminalRetry: true,
                    token);
                if (concurrentReplay.ImmediateResponse is not null)
                {
                    return concurrentReplay.ImmediateResponse;
                }

                CommittedRecoveryResolution concurrentRecovery = await ResolveCommittedRecoveryAsync(
                    concurrentReplay.RetryCandidate,
                    normalized,
                    token);
                if (concurrentRecovery == CommittedRecoveryResolution.Conflict)
                {
                    return RecoveryConflict();
                }

                BaseCommandResponse<ManagementTenantProvisioningOperationDto>? concurrentInstancePolicyFailure =
                    await EvaluateInstanceSchedulingPolicyAsync(
                        request.ManagedInstanceId,
                        token);
                if (concurrentInstancePolicyFailure is not null)
                {
                    return concurrentInstancePolicyFailure;
                }

                if (concurrentRecovery != CommittedRecoveryResolution.Owned)
                {
                    BaseCommandResponse<ManagementTenantProvisioningOperationDto>? concurrentTenantPolicyFailure =
                        await EvaluateTenantCreationPolicyAsync(
                            normalized,
                            includeCapacity: true,
                            token);
                    if (concurrentTenantPolicyFailure is not null)
                    {
                        return concurrentTenantPolicyFailure;
                    }
                }

                DateTime now = DateTime.UtcNow;
                if (concurrentReplay.RetryCandidate is not null)
                {
                    ManagedTenantProvisioningOperation candidate = concurrentReplay.RetryCandidate;
                    Guid retryOutboxMessageId = Guid.CreateVersion7();
                    bool retried = await operationRepository.TryRetryAsync(
                        candidate.Id,
                        retryOutboxMessageId,
                        ManagedTenantProvisioningRequestCodec.Serialize(normalized),
                        normalized.Callback?.CorrelationId,
                        now,
                        token);
                    if (!retried)
                    {
                        ReplayResolution racedReplay = await ResolveReplayAsync(
                            request.ManagedInstanceId,
                            normalized,
                            requestHash,
                            allowTerminalRetry: false,
                            token);
                        return racedReplay.ImmediateResponse
                            ?? Failure(
                                "tenant_provisioning_retry_conflict",
                                "The tenant provisioning operation changed while retrying.");
                    }

                    await CreateProcessOutboxAsync(candidate.Id, retryOutboxMessageId, now);
                    ManagedTenantProvisioningOperation retriedOperation =
                        await operationRepository.GetByManagedInstanceAndIdAsNoTrackingAsync(
                            request.ManagedInstanceId,
                            candidate.Id,
                            token)
                        ?? throw new InvalidOperationException("Retried tenant provisioning operation could not be reloaded.");
                    return Success(retriedOperation, "Managed tenant provisioning retry accepted.");
                }

                Guid operationId = Guid.CreateVersion7();
                Guid outboxMessageId = Guid.CreateVersion7();
                var operation = await operationRepository.Create(new ManagedTenantProvisioningOperation
                {
                    Id = operationId,
                    ManagedInstanceId = request.ManagedInstanceId,
                    ExternalRequestId = normalized.ExternalRequestId,
                    ExternalCustomerReference = normalized.ExternalCustomerReference,
                    RequestHash = requestHash,
                    RequestJson = ManagedTenantProvisioningRequestCodec.Serialize(normalized),
                    TenantSlug = normalized.TenantSlug,
                    CurrentOutboxMessageId = outboxMessageId,
                    CorrelationId = normalized.Callback?.CorrelationId,
                    Status = ManagedTenantProvisioningStatus.Pending,
                    CreatedAt = now,
                    CreatedBy = null
                });

                await CreateProcessOutboxAsync(operation.Id, outboxMessageId, now);

                return Success(operation, "Managed tenant provisioning accepted.");
            },
            cancellationToken);
    }

    private async Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>?>
        EvaluateInstanceSchedulingPolicyAsync(
            Guid managedInstanceId,
            CancellationToken cancellationToken)
    {
        DeploymentMode deploymentMode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        if (deploymentMode != DeploymentMode.MultiTenant)
        {
            return Failure(
                "tenant_provisioning_requires_multi_tenant",
                "Managed tenant provisioning is unavailable in SingleTenant mode.");
        }

        ManagedControlPlaneRegistration? registration =
            await registrationRepository.GetCurrentAsync(cancellationToken);
        ManagementTenantProvisioningBlockerDto? registrationBlocker =
            ManagedTenantProvisioningRegistrationPolicy.Evaluate(
                registration,
                managedInstanceId,
                deploymentMode);
        return registrationBlocker is null
            ? null
            : Failure(registrationBlocker.Code, registrationBlocker.Message);
    }

    private async Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>?>
        EvaluateTenantCreationPolicyAsync(
            ManagementTenantProvisioningRequestDto request,
            bool includeCapacity,
            CancellationToken cancellationToken)
    {
        ManagedTenantProvisioningPreflightResult policy = await preflight.EvaluateAsync(
            request,
            requireProvisionablePlan: true,
            cancellationToken);
        if (!policy.Success)
        {
            return Failure(policy.FailureCode!, policy.Error!);
        }

        if (!includeCapacity)
        {
            return null;
        }

        TenantActivationCapacityAssessment capacity = await capacityPolicy.EvaluateAsync(
            requireMultiTenant: true,
            cancellationToken: cancellationToken);
        return capacity.Allowed
            ? null
            : Failure(capacity.FailureCode!, capacity.Error!);
    }

    private async Task<CommittedRecoveryResolution> ResolveCommittedRecoveryAsync(
        ManagedTenantProvisioningOperation? retryCandidate,
        ManagementTenantProvisioningRequestDto request,
        CancellationToken cancellationToken)
    {
        if (retryCandidate is null)
        {
            return CommittedRecoveryResolution.None;
        }

        ExternalBinding? customerBinding = await externalBindingRepository.GetByExternalKeyAsync(
            "islamu-event-control-plane",
            "control-plane",
            ExternalBindingTypes.External.ProviderCustomer,
            request.ExternalCustomerReference,
            scopeTenantId: null,
            cancellationToken);
        if (customerBinding is null)
        {
            return CommittedRecoveryResolution.None;
        }

        if (customerBinding.InternalType != ExternalBindingTypes.Internal.Tenant)
        {
            return CommittedRecoveryResolution.Conflict;
        }

        Tenant? tenant = await tenantRepository.GetByIdAsNoTrackingAsync(
            customerBinding.InternalId,
            cancellationToken);
        ExternalBinding? operationBinding = await externalBindingRepository.GetByExternalKeyAsync(
            "islamu-event-control-plane",
            "control-plane",
            ExternalBindingTypes.External.ManagedTenantProvisioningOperation,
            retryCandidate.Id.ToString("D"),
            customerBinding.InternalId,
            cancellationToken);

        return tenant is not null
            && string.Equals(tenant.Slug, retryCandidate.TenantSlug, StringComparison.Ordinal)
            && operationBinding?.InternalType == ExternalBindingTypes.Internal.Tenant
            && operationBinding.InternalId == tenant.Id
                ? CommittedRecoveryResolution.Owned
                : CommittedRecoveryResolution.Conflict;
    }

    private async Task<ReplayResolution> ResolveReplayAsync(
        Guid managedInstanceId,
        ManagementTenantProvisioningRequestDto request,
        string requestHash,
        bool allowTerminalRetry,
        CancellationToken cancellationToken)
    {
        ManagedTenantProvisioningOperation? requestReplay =
            await operationRepository.GetByManagedInstanceAndExternalRequestAsync(
                managedInstanceId,
                request.ExternalRequestId,
                cancellationToken);
        if (requestReplay is not null)
        {
            if (!string.Equals(requestReplay.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new ReplayResolution(
                    Failure(
                        "tenant_provisioning_idempotency_conflict",
                        "The external request id is already bound to a different provisioning request."),
                    null);
            }

            return allowTerminalRetry
                && requestReplay.Status is (ManagedTenantProvisioningStatus.Failed
                    or ManagedTenantProvisioningStatus.Cancelled)
                ? new ReplayResolution(null, requestReplay)
                : new ReplayResolution(
                    Success(requestReplay, "Managed tenant provisioning request replayed."),
                    null);
        }

        ManagedTenantProvisioningOperation? customerReplay =
            await operationRepository.GetByManagedInstanceAndExternalCustomerReferenceAsync(
                managedInstanceId,
                request.ExternalCustomerReference,
                cancellationToken);
        if (customerReplay is null)
        {
            return new ReplayResolution(null, null);
        }

        if (!string.Equals(customerReplay.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new ReplayResolution(
                Failure(
                    "tenant_provisioning_customer_conflict",
                    "The external customer reference is already bound to a different provisioning request."),
                null);
        }

        return allowTerminalRetry
            && customerReplay.Status is (ManagedTenantProvisioningStatus.Failed
                or ManagedTenantProvisioningStatus.Cancelled)
            ? new ReplayResolution(null, customerReplay)
            : new ReplayResolution(
                Success(customerReplay, "Managed tenant provisioning request replayed."),
                null);
    }

    private Task<OutboxMessage> CreateProcessOutboxAsync(
        Guid operationId,
        Guid outboxMessageId,
        DateTime createdAt) =>
        outboxRepository.Create(new OutboxMessage
        {
            Id = outboxMessageId,
            AggregateType = nameof(ManagedTenantProvisioningOperation),
            AggregateId = operationId,
            EventType = ManagedTenantProvisioningOutboxEvents.ProcessRequested,
            Payload = null,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = createdAt,
            MaxRetries = 10
        });

    private sealed record ReplayResolution(
        BaseCommandResponse<ManagementTenantProvisioningOperationDto>? ImmediateResponse,
        ManagedTenantProvisioningOperation? RetryCandidate);

    private enum CommittedRecoveryResolution
    {
        None,
        Owned,
        Conflict
    }

    private static BaseCommandResponse<ManagementTenantProvisioningOperationDto> Success(
        ManagedTenantProvisioningOperation operation,
        string message) => BaseCommandResponse.Success(
            ManagedTenantProvisioningRequestCodec.ToDto(operation),
            message);

    private static BaseCommandResponse<ManagementTenantProvisioningOperationDto> Failure(
        string failureCode,
        string message) => BaseCommandResponse.Failure<ManagementTenantProvisioningOperationDto>(
            failureCode,
            message,
            [message]);

    private static BaseCommandResponse<ManagementTenantProvisioningOperationDto> RecoveryConflict() =>
        Failure(
            "tenant_provisioning_operation_provenance_conflict",
            "The customer reference is bound to a tenant that is not owned by this provisioning operation.");
}

public sealed class CancelManagedTenantProvisioningOperationCommandHandler(
    IManagedTenantProvisioningOperationRepository operationRepository)
    : IRequestHandler<CancelManagedTenantProvisioningOperationCommand,
        BaseCommandResponse<ManagementTenantProvisioningOperationDto>>
{
    public async Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>> Handle(
        CancelManagedTenantProvisioningOperationCommand request,
        CancellationToken cancellationToken)
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
    IManagedTenantProvisioningOperationRepository operationRepository,
    IManagedProviderClientProvisioner provisioner)
    : IRequestHandler<ProcessManagedTenantProvisioningOperationCommand, bool>
{
    public async Task<bool> Handle(
        ProcessManagedTenantProvisioningOperationCommand request,
        CancellationToken cancellationToken)
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

        if (operation.Status == ManagedTenantProvisioningStatus.Pending)
        {
            await operationRepository.TryStartAsync(
                operation.Id,
                request.OutboxMessageId,
                DateTime.UtcNow,
                cancellationToken);
            operation = await operationRepository.GetByIdAsNoTrackingAsync(
                operation.Id,
                cancellationToken);
        }

        if (operation is null
            || operation.CurrentOutboxMessageId != request.OutboxMessageId
            || operation.Status is ManagedTenantProvisioningStatus.Succeeded
                or ManagedTenantProvisioningStatus.Failed
                or ManagedTenantProvisioningStatus.Cancelled)
        {
            return true;
        }

        if (operation.Status != ManagedTenantProvisioningStatus.Processing)
        {
            throw TransitionConflict(operation.Id);
        }

        if (string.IsNullOrWhiteSpace(operation.RequestJson))
        {
            await EnsureTerminalTransitionAsync(
                operation.Id,
                request.OutboxMessageId,
                await operationRepository.TryFailAsync(
                    operation.Id,
                    request.OutboxMessageId,
                    "tenant_provisioning_request_snapshot_missing",
                    DateTime.UtcNow,
                    cancellationToken),
                cancellationToken);
            return true;
        }

        ManagementTenantProvisioningRequestDto managementRequest;
        try
        {
            managementRequest = ManagedTenantProvisioningRequestCodec.Deserialize(operation.RequestJson);
        }
        catch (JsonException)
        {
            await EnsureTerminalTransitionAsync(operation.Id, request.OutboxMessageId,
                await operationRepository.TryFailAsync(operation.Id, request.OutboxMessageId,
                    "tenant_provisioning_request_snapshot_invalid", DateTime.UtcNow, cancellationToken), cancellationToken);
            return true;
        }
        ManagedProviderClientProvisioningDto provisioningDto =
            ManagedTenantProvisioningRequestCodec.ToProvisioningRequest(managementRequest);
        BaseCommandResponse<ManagedProviderClientProvisioningResultDto> result =
            await provisioner.EnsureAsync(
                provisioningDto,
                managementRequest,
                operation.Id,
                request.OutboxMessageId,
                cancellationToken);
        if (!result.IsSuccess || result.Id is null)
        {
            await EnsureTerminalTransitionAsync(
                operation.Id,
                request.OutboxMessageId,
                await operationRepository.TryFailAsync(
                    operation.Id,
                    request.OutboxMessageId,
                    result.FailureCode ?? "tenant_provisioning_failed",
                    DateTime.UtcNow,
                    cancellationToken),
                cancellationToken);
            return true;
        }

        await EnsureTerminalTransitionAsync(
            operation.Id,
            request.OutboxMessageId,
            await operationRepository.TryCompleteAsync(
                operation.Id,
                request.OutboxMessageId,
                result.Id.TenantId,
                result.Id.UserId,
                DateTime.UtcNow,
                cancellationToken),
            cancellationToken);
        return true;
    }

    private async Task EnsureTerminalTransitionAsync(
        Guid operationId,
        Guid expectedOutboxMessageId,
        bool transitioned,
        CancellationToken cancellationToken)
    {
        if (transitioned)
        {
            return;
        }

        ManagedTenantProvisioningOperation? current =
            await operationRepository.GetByIdAsNoTrackingAsync(operationId, cancellationToken);
        if (current is null
            || current.CurrentOutboxMessageId != expectedOutboxMessageId
            || current.Status is ManagedTenantProvisioningStatus.Succeeded
            or ManagedTenantProvisioningStatus.Failed
            or ManagedTenantProvisioningStatus.Cancelled)
        {
            return;
        }

        throw TransitionConflict(operationId);
    }

    private static ConcurrencyConflictException TransitionConflict(Guid operationId) => new(
        ConcurrencyConflictException.ConcurrentUpdate,
        "Managed tenant provisioning operation state changed concurrently.",
        nameof(ManagedTenantProvisioningOperation),
        operationId.ToString("D"));

}

public sealed class ReconcileManagedTenantProvisioningDeadLetterCommandHandler(
    IManagedTenantProvisioningOperationRepository operationRepository)
    : IRequestHandler<ReconcileManagedTenantProvisioningDeadLetterCommand, bool>
{
    public const string FailureCode = "tenant_provisioning_dispatch_exhausted";

    public async Task<bool> Handle(
        ReconcileManagedTenantProvisioningDeadLetterCommand request,
        CancellationToken cancellationToken)
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
