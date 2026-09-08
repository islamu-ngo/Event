using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

public interface IKeycloakRealmDesiredStateBuilder
{
    KeycloakRealmDesiredStateDto Build(KeycloakRealmDesiredStateBuildRequestDto request);
}
