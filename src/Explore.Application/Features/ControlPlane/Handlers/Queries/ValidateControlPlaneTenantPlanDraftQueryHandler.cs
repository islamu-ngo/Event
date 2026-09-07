using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class ValidateControlPlaneTenantPlanDraftQueryHandler
    : IRequestHandler<ValidateControlPlaneTenantPlanDraftQuery, TenantPlanValidationResult>
{
    public Task<TenantPlanValidationResult> Handle(
        ValidateControlPlaneTenantPlanDraftQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(TenantPlanDraftValidator.Validate(request.Draft));
    }
}
