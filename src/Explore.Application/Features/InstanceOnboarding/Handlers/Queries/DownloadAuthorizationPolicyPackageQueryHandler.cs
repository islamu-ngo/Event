using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class DownloadAuthorizationPolicyPackageQueryHandler(IPolicyPackageService policyPackageService)
    : IRequestHandler<DownloadAuthorizationPolicyPackageQuery, PolicyPackageArchive>
{
    public Task<PolicyPackageArchive> Handle(
        DownloadAuthorizationPolicyPackageQuery request,
        CancellationToken cancellationToken)
    {
        return policyPackageService.ExportArchiveAsync(cancellationToken);
    }
}
