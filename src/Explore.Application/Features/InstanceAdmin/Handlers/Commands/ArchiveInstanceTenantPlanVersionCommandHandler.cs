using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Commands;

public sealed class ArchiveInstanceTenantPlanVersionCommandHandler(ITenantPlanRepository tenantPlanRepository)
    : ICommandHandler<ArchiveInstanceTenantPlanVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ArchiveInstanceTenantPlanVersionCommand request,
        CancellationToken cancellationToken)
    {
        TenantPlanVersion? version = await tenantPlanRepository.GetVersionAsync(request.VersionId, cancellationToken);
        if (version is null)
        {
            return Failure("Tenant plan version was not found.", ["tenant_plan_version_not_found"]);
        }

        if (version.TenantPlanStatusId != (int)TenantPlanStatusEnum.Published)
        {
            return Failure("Only published tenant plan versions can be archived.", ["tenant_plan_version_not_published"]);
        }

        version.TenantPlanStatusId = (int)TenantPlanStatusEnum.Archived;
        version.IsActiveForProvisioning = false;
        await tenantPlanRepository.UpdateVersionAsync(version, cancellationToken);

        return BaseCommandResponse.Success(version.Id, "Tenant plan version archived.");
    }

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
