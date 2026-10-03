using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Commands;

public sealed class CreateInstanceTenantPlanDraftCommandHandler(ITenantPlanRepository tenantPlanRepository)
    : ICommandHandler<CreateInstanceTenantPlanDraftCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        CreateInstanceTenantPlanDraftCommand request,
        CancellationToken cancellationToken)
    {
        TenantPlanValidationResult validation = TenantPlanDraftValidator.Validate(request.Draft);
        if (!validation.IsValid)
        {
            return Failure("Tenant plan draft is invalid.", validation.Errors.Select(error => error.Code));
        }

        var existing = await tenantPlanRepository.GetByKeyAsync(request.Draft.Key, cancellationToken);
        if (existing is not null)
        {
            return Failure("A tenant plan with this key already exists.", ["tenant_plan_key_exists"]);
        }

        var plan = InstanceTenantPlanDraftMapper.ToPlan(request.Draft);
        await tenantPlanRepository.Create(plan);

        return BaseCommandResponse.Success(plan.Id, "Tenant plan draft created.");
    }

    private static BaseCommandResponse<Guid> Failure(string message, IEnumerable<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, message);
}
