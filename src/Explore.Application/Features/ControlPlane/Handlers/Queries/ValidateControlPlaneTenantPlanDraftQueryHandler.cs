using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class ValidateControlPlaneTenantPlanDraftQueryHandler
    : IQueryHandler<ValidateControlPlaneTenantPlanDraftQuery, TenantPlanValidationResult>
{
    public Task<TenantPlanValidationResult> QueryAsync(
        ValidateControlPlaneTenantPlanDraftQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(TenantPlanDraftValidator.Validate(request.Draft));
    }
}
