// ABOUTME: Dispatches transient first-run Local enrollment with the actual authenticated setup principal.
// ABOUTME: Separates trusted adapter authority from caller-supplied settings and hides diagnostic values.

using System.Security.Claims;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record CompleteLocalInstanceOnboardingCommand(
    CompleteLocalInstanceOnboardingRequestDto Request,
    ClaimsPrincipal SetupPrincipal) : IRequest<BaseCommandResponse<Guid>>
{
    public override string ToString() => nameof(CompleteLocalInstanceOnboardingCommand);
}
