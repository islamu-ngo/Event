using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

public sealed record PreviewKeycloakRealmSyncQuery : IQuery<KeycloakRealmSyncPlanDto>
{
    public KeycloakRealmSyncPreviewRequestDto Request { get; init; } = new();
}
