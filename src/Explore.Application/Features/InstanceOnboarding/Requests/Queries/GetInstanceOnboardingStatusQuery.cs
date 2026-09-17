using System.Security.Claims;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetInstanceOnboardingStatusQuery : IQuery<InstanceOnboardingStatusDto>
{
    public ClaimsPrincipal? SetupPrincipal { get; init; }

    public override string ToString() => nameof(GetInstanceOnboardingStatusQuery);
}
