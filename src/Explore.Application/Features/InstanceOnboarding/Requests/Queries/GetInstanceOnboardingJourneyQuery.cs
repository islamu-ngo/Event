using System.Security.Claims;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetInstanceOnboardingJourneyQuery : IQuery<InstanceOnboardingJourneyDto>
{
    public ClaimsPrincipal? SetupPrincipal { get; init; }
}
