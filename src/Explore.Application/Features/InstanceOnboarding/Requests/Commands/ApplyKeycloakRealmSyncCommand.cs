using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record ApplyKeycloakRealmSyncCommand : IRequest<KeycloakRealmSyncPlanDto>
{
    public KeycloakRealmSyncApplyRequestDto Request { get; init; } = new();
}
