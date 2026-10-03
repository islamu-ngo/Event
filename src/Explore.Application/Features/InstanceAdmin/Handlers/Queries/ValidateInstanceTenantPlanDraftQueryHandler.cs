using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class ValidateInstanceTenantPlanDraftQueryHandler
    : IQueryHandler<ValidateInstanceTenantPlanDraftQuery, TenantPlanValidationResult>
{
    public Task<TenantPlanValidationResult> QueryAsync(
        ValidateInstanceTenantPlanDraftQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(TenantPlanDraftValidator.Validate(request.Draft));
    }
}
