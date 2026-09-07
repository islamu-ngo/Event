using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class ArchiveControlPlaneTenantPlanVersionCommandHandler(ITenantPlanRepository tenantPlanRepository)
    : IRequestHandler<ArchiveControlPlaneTenantPlanVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ArchiveControlPlaneTenantPlanVersionCommand request,
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
