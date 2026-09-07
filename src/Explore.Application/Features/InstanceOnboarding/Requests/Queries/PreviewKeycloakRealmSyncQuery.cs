using Explore.Application.DTOs.Onboarding;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record PreviewKeycloakRealmSyncQuery : IRequest<KeycloakRealmSyncPlanDto>
{
    public KeycloakRealmSyncPreviewRequestDto Request { get; init; } = new();
}
