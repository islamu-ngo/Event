
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
