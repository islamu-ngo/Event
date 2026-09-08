using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record BootstrapKeycloakRealmCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required KeycloakBootstrapRequestDto BootstrapRequest { get; init; } = new();
}
