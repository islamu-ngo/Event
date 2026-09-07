using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class PublishControlPlaneTenantPlanVersionCommandHandler(
    ITenantPlanRepository tenantPlanRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PublishControlPlaneTenantPlanVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        PublishControlPlaneTenantPlanVersionCommand request,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(ExecuteAsync, cancellationToken);

        async Task<BaseCommandResponse<Guid>> ExecuteAsync(CancellationToken token)
        {
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
