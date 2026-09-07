using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateAuthorizationProviderConfigurationDuringSetupCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required PatchAuthorizationProviderConfigurationDto Patch { get; init; } = new();
}
