using System.Security.Claims;
using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetInstanceOnboardingStatusQuery : IRequest<InstanceOnboardingStatusDto>
{
    public ClaimsPrincipal? SetupPrincipal { get; init; }

    public override string ToString() => nameof(GetInstanceOnboardingStatusQuery);
}
