using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

/// <summary>
/// Downloads the current authorization policy package archive for manual operator installation.
/// </summary>
public sealed record DownloadAuthorizationPolicyPackageQuery : IRequest<PolicyPackageArchive>
{
}
