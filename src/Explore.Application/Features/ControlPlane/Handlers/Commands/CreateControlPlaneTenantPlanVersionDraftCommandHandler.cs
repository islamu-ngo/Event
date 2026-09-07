using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Commands;

public sealed class CreateControlPlaneTenantPlanVersionDraftCommandHandler(ITenantPlanRepository tenantPlanRepository)
    : IRequestHandler<CreateControlPlaneTenantPlanVersionDraftCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateControlPlaneTenantPlanVersionDraftCommand request,
        CancellationToken cancellationToken)
    {
        TenantPlanValidationResult validation = TenantPlanDraftValidator.Validate(request.Draft);
        if (!validation.IsValid)
        {
            return Failure("Tenant plan draft is invalid.", validation.Errors.Select(error => error.Code));
        }

        TenantPlan? plan = await tenantPlanRepository.GetByKeyAsync(request.PlanKey, cancellationToken);
        if (plan is null)
        {
            return Failure("Tenant plan was not found.", ["tenant_plan_not_found"]);
        }

        if (!string.Equals(plan.Key, request.Draft.Key, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("Tenant plan draft key does not match the target plan.", ["tenant_plan_key_mismatch"]);
        }

        int nextVersionNumber = plan.Versions.Count == 0
            ? 1
            : plan.Versions.Max(version => version.VersionNumber) + 1;

        TenantPlanVersion version = ControlPlaneTenantPlanDraftMapper.ToVersion(
            plan,
            request.Draft,
            nextVersionNumber,
            TenantPlanStatusEnum.Draft);

        plan.Versions.Add(version);
        await tenantPlanRepository.CreateVersionAsync(version, cancellationToken);

        return BaseCommandResponse.Success(version.Id, "Tenant plan version draft created.");
    }

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
