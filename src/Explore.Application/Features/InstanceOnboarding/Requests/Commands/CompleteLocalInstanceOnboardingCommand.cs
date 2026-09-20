
using System.Security.Claims;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record CompleteLocalInstanceOnboardingCommand(
    CompleteLocalInstanceOnboardingRequestDto Request,
    ClaimsPrincipal SetupPrincipal) : ICommand<BaseCommandResponse<Guid>>
{
    public override string ToString() => nameof(CompleteLocalInstanceOnboardingCommand);
}
