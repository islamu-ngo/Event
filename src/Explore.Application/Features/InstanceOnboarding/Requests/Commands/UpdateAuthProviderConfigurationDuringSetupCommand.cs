using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateAuthProviderConfigurationDuringSetupCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required PatchAuthProviderConfigurationDto Patch { get; init; } = new();
}
