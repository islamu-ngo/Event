using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class DownloadAuthorizationPolicyPackageQueryHandler(IPolicyPackageService policyPackageService)
    : IQueryHandler<DownloadAuthorizationPolicyPackageQuery, PolicyPackageArchive>
{
    public Task<PolicyPackageArchive> QueryAsync(
        DownloadAuthorizationPolicyPackageQuery request,
        CancellationToken cancellationToken)
    {
        return policyPackageService.ExportArchiveAsync(cancellationToken);
    }
}
