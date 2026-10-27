using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.Management.Validators;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Features.Management.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.Features.Management;

/// <summary>Coordinates one instance-wide scheduling workflow; transport authentication remains with its caller.</summary>
public sealed class ManagedTenantProvisioningSchedulingOperation(
    IManagedTenantProvisioningSchedulingSource source,
    IManagedTenantProvisioningOperationRepository operationRepository,
    IOutboxRepository outboxRepository,
    ISettingMutationLock mutationLock,
    IEventDiscoveryEnrollmentGate enrollmentGate)
{
    public async Task<BaseCommandResponse<ManagedTenantProvisioningOperation>> ExecuteAsync(
        ScheduleManagedTenantProvisioningCommand request,
        CancellationToken cancellationToken = default)
    {
        var validator = new ManagementTenantProvisioningRequestValidator();
        await validator.ValidateAndThrowAsync(request.Request, cancellationToken);
        ManagementTenantProvisioningRequestDto normalized =
            ManagedTenantProvisioningRequestCodec.Normalize(request.Request);
        string requestHash = ManagedTenantProvisioningRequestCodec.ComputeHash(normalized);
        await using var administrator = await source.RetainAdministratorAsync(
            normalized.Administrator.LocalIdentity?.LocalSubjectId, cancellationToken);
        await using var enrollment = await enrollmentGate.AcquireAsync(cancellationToken);

        BaseCommandResponse<ManagedTenantProvisioningOperation>? authorityFailure =
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

        BaseCommandResponse<ManagedTenantProvisioningOperation>? instancePolicyFailure =
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
            BaseCommandResponse<ManagedTenantProvisioningOperation>? tenantPolicyFailure =
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

                BaseCommandResponse<ManagedTenantProvisioningOperation>? concurrentInstancePolicyFailure =
                    await EvaluateInstanceSchedulingPolicyAsync(
                        request.ManagedInstanceId,
                        token);
                if (concurrentInstancePolicyFailure is not null)
                {
                    return concurrentInstancePolicyFailure;
                }

                if (concurrentRecovery != CommittedRecoveryResolution.Owned)
                {
                    BaseCommandResponse<ManagedTenantProvisioningOperation>? concurrentTenantPolicyFailure =
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
                    await source.RequireCurrentAsync(token);
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
                await source.RequireCurrentAsync(token);

                return Success(operation, "Managed tenant provisioning accepted.");
            },
            cancellationToken);
    }

    private async Task<BaseCommandResponse<ManagedTenantProvisioningOperation>?>
        EvaluateInstanceSchedulingPolicyAsync(
            Guid managedInstanceId,
            CancellationToken cancellationToken)
    {
        DeploymentMode deploymentMode = await source.ReadDeploymentModeAsync(cancellationToken);
        if (deploymentMode != DeploymentMode.MultiTenant)
        {
            return Failure(
                "tenant_provisioning_requires_multi_tenant",
                "Managed tenant provisioning is unavailable in SingleTenant mode.");
        }

        ManagedTenantProvisioningRegistrationPolicy.Facts? registration =
            await source.ReadRegistrationFactsAsync(cancellationToken);
        ManagementTenantProvisioningBlockerDto? registrationBlocker =
            ManagedTenantProvisioningRegistrationPolicy.EvaluateFacts(
                registration,
                managedInstanceId,
                deploymentMode);
        return registrationBlocker is null
            ? null
            : Failure(registrationBlocker.Code, registrationBlocker.Message);
    }

    private async Task<BaseCommandResponse<ManagedTenantProvisioningOperation>?>
        EvaluateTenantCreationPolicyAsync(
            ManagementTenantProvisioningRequestDto request,
            bool includeCapacity,
            CancellationToken cancellationToken)
    {
        ManagedTenantProvisioningPreflightResult policy = await ManagedTenantProvisioningPreflight.EvaluateFromSourcesAsync(
            request, requireProvisionablePlan: true, source, cancellationToken);
        if (!policy.Success)
        {
            return Failure(policy.FailureCode!, policy.Error!);
        }

        if (!includeCapacity)
        {
            return null;
        }

        TenantActivationCapacityAssessment capacity = await source.AssessCapacityAsync(
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

        ManagedTenantProvisioningBinding? customerBinding = await source.ReadCustomerBindingAsync(
            request.ExternalCustomerReference, cancellationToken);
        if (customerBinding is null)
        {
            return CommittedRecoveryResolution.None;
        }

        if (customerBinding.InternalType != ExternalBindingTypes.Internal.Tenant)
        {
            return CommittedRecoveryResolution.Conflict;
        }

        bool ownedTenant = await source.TenantMatchesSlugAsync(
            customerBinding.InternalId, retryCandidate.TenantSlug, cancellationToken);
        ManagedTenantProvisioningBinding? operationBinding = await source.ReadOperationBindingAsync(
            retryCandidate.Id, customerBinding.InternalId, cancellationToken);

        return ownedTenant
            && operationBinding?.InternalType == ExternalBindingTypes.Internal.Tenant
            && operationBinding.InternalId == customerBinding.InternalId
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
        BaseCommandResponse<ManagedTenantProvisioningOperation>? ImmediateResponse,
        ManagedTenantProvisioningOperation? RetryCandidate);

    private enum CommittedRecoveryResolution
    {
        None,
        Owned,
        Conflict
    }

    private static BaseCommandResponse<ManagedTenantProvisioningOperation> Success(
        ManagedTenantProvisioningOperation operation,
        string message) => BaseCommandResponse.Success(
            operation,
            message);

    private static BaseCommandResponse<ManagedTenantProvisioningOperation> Failure(
        string failureCode,
        string message) => BaseCommandResponse.Failure<ManagedTenantProvisioningOperation>(
            failureCode,
            message,
            [message]);

    private static BaseCommandResponse<ManagedTenantProvisioningOperation> RecoveryConflict() =>
        Failure(
            "tenant_provisioning_operation_provenance_conflict",
            "The customer reference is bound to a tenant that is not owned by this provisioning operation.");
}
