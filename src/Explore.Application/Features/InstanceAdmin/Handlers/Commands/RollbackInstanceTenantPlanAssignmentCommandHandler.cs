using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Commands;

public sealed class RollbackInstanceTenantPlanAssignmentCommandHandler(
    ITenantPlanRepository tenantPlanRepository,
    ISerializableAttemptOwner owner,
    ICurrentUserService currentUserService,
    IInstanceAdministratorCommitAuthority commitAuthority,
    IHumanAdministrativeActionPreparation preparation)
    : ICommandHandler<RollbackInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RollbackInstanceTenantPlanAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        Guid? principalId = currentUserService.UserId;
        if (principalId is not { } actorId || actorId == Guid.Empty)
            return BaseCommandResponse.Authorization<Guid>("Current instance administrator authority is required.");
        var prepared = await preparation.PrepareAsync(request, actorId, cancellationToken);
        return await owner.ExecuteAsync(ExecuteAsync, cancellationToken);

        async Task<BaseCommandResponse<Guid>> ExecuteAsync(CancellationToken token)
        {
            await prepared.AcquireAsync(request, actorId, token);
            if (!await commitAuthority.IsInstanceAdministratorForCommitAsync(actorId, token))
                return BaseCommandResponse.Authorization<Guid>("Current instance administrator authority is required.");
            TenantPlanAssignment? current = await tenantPlanRepository.GetActiveAssignmentForTenantAsync(
                request.TenantId,
                token);
            if (current is null)
            {
                return Failure("Tenant has no active plan assignment.", ["tenant_plan_active_assignment_not_found"]);
            }

            TenantPlanAssignment? previous = await tenantPlanRepository.GetPreviousEligibleAssignmentForTenantAsync(
                request.TenantId,
                current.Id,
                token);
            if (previous is null)
            {
                return Failure("Tenant has no eligible previous plan assignment.", ["tenant_plan_rollback_assignment_not_found"]);
            }

            if (previous.Id != request.AssignmentId
                || previous.TenantId != request.TenantId
                || previous.TenantPlanAssignmentStatusId != (int)TenantPlanAssignmentStatusEnum.Superseded
                || previous.TenantPlanVersion.TenantPlanStatusId != (int)TenantPlanStatusEnum.Published
                || previous.EndedAt is null
                || previous.EndedAt > current.AssignedAt)
            {
                return Failure("Tenant plan assignment is not the eligible rollback target.", ["tenant_plan_rollback_assignment_not_eligible"]);
            }

            DateTime now = DateTime.UtcNow;
            current.TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.RolledBack;
            current.EndedAt = now;
            await tenantPlanRepository.UpdateAssignmentAsync(current, token);

            previous.TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active;
            previous.EndedAt = null;
            await tenantPlanRepository.UpdateAssignmentAsync(previous, token);

            return BaseCommandResponse.Success(previous.Id, "Tenant plan assignment rolled back.");
        }
    }

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
