using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record SyncAuthorizationPolicyPackageCommand : ICommand<BaseCommandResponse<Guid>>
{
    public AuthorizationPolicyPackageSyncRequestDto Request { get; init; } = new();
}
