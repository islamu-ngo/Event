using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Commands;

public sealed class PublishInstanceTenantPlanVersionCommandHandler(
    ITenantPlanRepository tenantPlanRepository,
    ISerializableAttemptOwner owner,
    ICurrentUserService currentUserService,
    IInstanceAdministratorCommitAuthority commitAuthority,
    IHumanAdministrativeActionPreparation preparation)
    : ICommandHandler<PublishInstanceTenantPlanVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        PublishInstanceTenantPlanVersionCommand request,
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
            TenantPlanVersion? version = await tenantPlanRepository.GetVersionAsync(request.VersionId, token);
            if (version is null)
            {
                return Failure("Tenant plan version was not found.", ["tenant_plan_version_not_found"]);
            }

            if (version.TenantPlanStatusId != (int)TenantPlanStatusEnum.Draft)
            {
                return Failure("Only draft tenant plan versions can be published.", ["tenant_plan_version_not_draft"]);
            }

            version.TenantPlanStatusId = (int)TenantPlanStatusEnum.Published;
            await tenantPlanRepository.UpdateVersionAsync(version, token);

            if (request.ExistingTenantPolicy == TenantPlanExistingAssignmentPolicy.MoveExistingTenantsToPublishedVersion)
            {
                IReadOnlyList<TenantPlanAssignment> assignments = await tenantPlanRepository.ListActiveAssignmentsForPlanAsync(
                    version.TenantPlanId,
                    token);

                foreach (TenantPlanAssignment assignment in assignments)
                {
                    assignment.TenantPlanVersionId = version.Id;
                    assignment.TenantPlanVersion = version;
                    await tenantPlanRepository.UpdateAssignmentAsync(assignment, token);
                }
            }

            return BaseCommandResponse.Success(version.Id, "Tenant plan version published.");
        }
    }

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
