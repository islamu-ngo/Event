using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record BootstrapKeycloakRealmCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required KeycloakBootstrapRequestDto BootstrapRequest { get; init; } = new();
}
