using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateResolverConfigurationCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }

    public required PatchResolverConfigurationDto Patch { get; init; } = new();
}
