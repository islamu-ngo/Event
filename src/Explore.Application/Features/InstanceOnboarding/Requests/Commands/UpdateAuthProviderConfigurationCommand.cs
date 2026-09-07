using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateAuthProviderConfigurationCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchAuthProviderConfigurationDto Patch { get; init; } = new();
}
