using Explore.Application.Contracts.Operations;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class ClaimConfiguredInstanceAdministratorCommandHandler(
    InstanceOnboardingCompletionOperation completionOperation)
    : ICommandHandler<ClaimConfiguredInstanceAdministratorCommand, BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ClaimConfiguredInstanceAdministratorCommand request,
        CancellationToken cancellationToken = default) =>
        completionOperation.ClaimConfiguredAsync(request, cancellationToken);
}
