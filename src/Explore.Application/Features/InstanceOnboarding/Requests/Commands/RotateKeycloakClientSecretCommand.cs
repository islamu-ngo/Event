using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record RotateKeycloakClientSecretCommand : ICommand<KeycloakClientSecretRotationResultDto>
{
    public Guid UserId { get; init; }
    public KeycloakClientSecretRotationRequestDto Request { get; init; } = new();
}
