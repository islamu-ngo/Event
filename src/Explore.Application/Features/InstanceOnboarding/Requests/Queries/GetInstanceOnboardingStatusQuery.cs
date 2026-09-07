// ABOUTME: Query contract for retrieving first-run onboarding completion and current user scope state.
// ABOUTME: Used by startup routing to determine onboarding or normal application entry.

using System.Security.Claims;
using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record GetInstanceOnboardingStatusQuery : IRequest<InstanceOnboardingStatusDto>
{
    public ClaimsPrincipal? SetupPrincipal { get; init; }

    public override string ToString() => nameof(GetInstanceOnboardingStatusQuery);
}
