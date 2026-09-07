using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record CompleteInstanceOnboardingCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required CompleteInstanceOnboardingRequest Settings { get; init; }

    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
    public string? AuthProvider { get; init; }
    public string? AuthProviderId { get; init; }
    public bool? EmailVerified { get; init; }
}
