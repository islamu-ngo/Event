using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateAuthProviderConfigurationDuringSetupCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required PatchAuthProviderConfigurationDto Patch { get; init; } = new();
}
