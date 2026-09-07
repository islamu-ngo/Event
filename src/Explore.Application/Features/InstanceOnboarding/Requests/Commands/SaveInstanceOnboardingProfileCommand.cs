using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record SaveInstanceOnboardingProfileCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required SelfHostOnboardingProfileDto Profile { get; init; }
}
