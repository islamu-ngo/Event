using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

/// <summary>
/// Reads the current authorization policy package status, including the revision observed in the
/// provider's policy store and the recovery action for whatever it found.
/// </summary>
public sealed record GetAuthorizationPolicyPackageStatusQuery : IRequest<AuthorizationPolicyPackageStatusDto>
{
}
