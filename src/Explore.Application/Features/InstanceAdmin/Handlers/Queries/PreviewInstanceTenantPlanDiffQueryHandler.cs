using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class PreviewInstanceTenantPlanDiffQueryHandler
    : IQueryHandler<PreviewInstanceTenantPlanDiffQuery, TenantPlanDiffResult>
{
    public Task<TenantPlanDiffResult> QueryAsync(
        PreviewInstanceTenantPlanDiffQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(TenantPlanDiffService.Diff(request.Current, request.Draft));
    }
}
