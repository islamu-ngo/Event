using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class ClaimConfiguredInstanceAdministratorCommandHandler(
    InstanceOnboardingCompletionOperation completionOperation)
    : IRequestHandler<ClaimConfiguredInstanceAdministratorCommand, BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> Handle(
        ClaimConfiguredInstanceAdministratorCommand request,
        CancellationToken cancellationToken) =>
        completionOperation.ClaimConfiguredAsync(request, cancellationToken);
}
