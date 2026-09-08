using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Exceptions;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.Management;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class TransitionControlPlaneTenantLifecycleCommandHandler(
    ITenantRepository tenantRepository,
    ITenantLifecycleLogRepository lifecycleLogRepository,
    IEmailDispatchOutboxRepository emailDispatchOutboxRepository,
    ICurrentUserService currentUserService,
    ISettingMutationLock mutationLock,
    TenantActivationCapacityPolicy capacityPolicy,
    ITenantDirectoryOperatorReadinessEvaluator directoryOperatorReadiness)
    : IRequestHandler<TransitionControlPlaneTenantLifecycleCommand, BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>>
{
    public async Task<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>> Handle(
        TransitionControlPlaneTenantLifecycleCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        if (userId is null)
        {
            return Failure("Authenticated operator context is required.");
        }

        var reason = NormalizeReason(request.Reason);
        if (RequiresReason(request.TargetStatus) && string.IsNullOrWhiteSpace(reason))
        {
            return Failure($"{request.TargetStatus} requires a reason.");
        }

        async Task<BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto>> TransitionAsync(
            CancellationToken ct)
        {
            var tenant = await tenantRepository.GetByIdAsNoTrackingAsync(request.TenantId, ct);
            if (tenant is null)
            {
                return Failure("Tenant was not found.");
            }

            if (RequiresConfirmation(request.TargetStatus) && !IsConfirmed(request.ConfirmationText, tenant.Slug))
            {
                return Failure($"{request.TargetStatus} requires confirmation with tenant slug '{tenant.Slug}'.");
            }

            var currentStatus = (TenantStatusEnum)tenant.TenantStatusId;
            if (!IsAllowedTransition(currentStatus, request.TargetStatus))
            {
                return Failure($"Cannot transition tenant from {currentStatus} to {request.TargetStatus}.");
            }

            var transitionedAt = DateTime.UtcNow;
            if (currentStatus == request.TargetStatus)
            {
                return Success(ControlPlaneTenantMapper.ToTransition(
                    tenant.Id,
                    tenant.TenantStatusId,
                    tenant.TenantStatusId,
                    userId.Value,
                    reason,
                    transitionedAt),
                    "Tenant already has the requested lifecycle status.");
            }

            var oldStatusId = tenant.TenantStatusId;
            var newStatusId = (int)request.TargetStatus;
            if (request.TargetStatus == TenantStatusEnum.Active)
            {
                TenantDirectoryOperatorReadinessAssessment identity =
                    await directoryOperatorReadiness.EvaluateAsync(
                        tenant.Id,
                        Explore.Domain.ValueObjects
                            .TenantDirectoryOperatorIdentityCapability.Activation,
                        ct);
                if (!identity.IsReady)
                {
                    string[] repairCodes = identity.ReasonCodes
                        .Where(TenantDirectoryOperatorReadinessReasonCodePolicy.IsClosedCode)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    return Failure(
                        "Tenant directory operator identity is not ready.",
                        identity.FailureCode,
                        repairCodes.Length == 0 ? null : repairCodes);
                }

                TenantActivationCapacityAssessment capacity = await capacityPolicy.EvaluateAsync(
                    requireMultiTenant: false,
                    cancellationToken: ct);
                if (!capacity.Allowed)
                {
                    return Failure(capacity.Error!, capacity.FailureCode);
                }
            }

            var transitioned = await tenantRepository.TryTransitionStatusAsync(
                tenant.Id,
                oldStatusId,
                newStatusId,
                transitionedAt,
                userId.Value,
                ct);
            if (!transitioned)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "Tenant lifecycle status changed since it was loaded. Reload and retry the transition.",
                    nameof(Tenant),
                    tenant.Id.ToString());
            }

            if (request.TargetStatus == TenantStatusEnum.Purged)
            {
                await emailDispatchOutboxRepository.SuppressAndRedactTenant(
                    tenant.Id,
                    userId.Value,
                    transitionedAt,
                    ct);
            }

            await lifecycleLogRepository.CreateAsync(new TenantLifecycleLog
            {
                TenantId = tenant.Id,
                Tenant = null!,
                OldStatusId = oldStatusId,
                NewStatusId = newStatusId,
                NewStatus = null!,
                TransitionedByUserId = userId.Value,
                Reason = reason,
                TransitionedAt = transitionedAt,
                CreatedAt = transitionedAt,
                CreatedBy = userId.Value
            }, ct);

            return Success(ControlPlaneTenantMapper.ToTransition(
                tenant.Id,
                oldStatusId,
                newStatusId,
                userId.Value,
                reason,
                transitionedAt),
                request.TargetStatus is TenantStatusEnum.Purged
                    ? "Tenant purge scheduled."
                    : "Tenant lifecycle status updated.");
        }

        string identityLockKey =
            TenantDirectoryOperatorIdentityMutationLockKeys.ForTenant(request.TenantId);
        string[] lockKeys = capacityPolicy.IsEnforced
            ? [GovernanceSettingKeys.Deployment.Mode, identityLockKey]
            : [identityLockKey];
        return await mutationLock.ExecuteManyAsync(
            lockKeys,
            TransitionAsync,
            cancellationToken);
    }

    private static bool RequiresReason(TenantStatusEnum targetStatus) =>
        targetStatus is TenantStatusEnum.Suspended or TenantStatusEnum.Archived or TenantStatusEnum.Purged;

    private static bool RequiresConfirmation(TenantStatusEnum targetStatus) =>
        targetStatus is TenantStatusEnum.Purged;

    private static bool IsConfirmed(string? confirmationText, string tenantSlug) =>
        string.Equals(confirmationText?.Trim(), tenantSlug, StringComparison.Ordinal);

    private static bool IsAllowedTransition(TenantStatusEnum currentStatus, TenantStatusEnum targetStatus) =>
        currentStatus switch
        {
            TenantStatusEnum.Purged => targetStatus == TenantStatusEnum.Purged,
            TenantStatusEnum.Active => targetStatus is TenantStatusEnum.Active or TenantStatusEnum.Suspended or TenantStatusEnum.Archived,
            TenantStatusEnum.Provisioning => targetStatus is TenantStatusEnum.Provisioning or TenantStatusEnum.Active or TenantStatusEnum.Suspended or TenantStatusEnum.Archived,
            TenantStatusEnum.Suspended => targetStatus is TenantStatusEnum.Suspended or TenantStatusEnum.Active or TenantStatusEnum.Archived,
            TenantStatusEnum.Archived => targetStatus is TenantStatusEnum.Archived or TenantStatusEnum.Active or TenantStatusEnum.Purged,
            _ => false
        };

    private static string? NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    private static BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto> Success(
        ControlPlaneTenantLifecycleTransitionDto result,
        string message) => BaseCommandResponse.Success(result, message);

    private static BaseCommandResponse<ControlPlaneTenantLifecycleTransitionDto> Failure(
        string message,
        string? failureCode = null,
        IEnumerable<string>? errors = null) => failureCode is null
            ? BaseCommandResponse.Validation<ControlPlaneTenantLifecycleTransitionDto>([message], message)
            : BaseCommandResponse.Failure<ControlPlaneTenantLifecycleTransitionDto>(failureCode, message, errors ?? [message]);
}
