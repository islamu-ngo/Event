using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class PreviewControlPlaneTenantPlanDiffQueryHandler
    : IQueryHandler<PreviewControlPlaneTenantPlanDiffQuery, TenantPlanDiffResult>
{
    public Task<TenantPlanDiffResult> QueryAsync(
        PreviewControlPlaneTenantPlanDiffQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(TenantPlanDiffService.Diff(request.Current, request.Draft));
    }
}
