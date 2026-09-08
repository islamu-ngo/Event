using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Services;

public class KeycloakRealmDesiredStateBuilder : IKeycloakRealmDesiredStateBuilder
{
    private readonly IReadOnlyList<IKeycloakIdentityContractContributor> _contributors;

    public KeycloakRealmDesiredStateBuilder(IEnumerable<IKeycloakIdentityContractContributor> contributors)
    {
        _contributors = contributors.ToArray();
    }

    public static KeycloakRealmDesiredStateBuilder CreateDefault() =>
        new([new EventKeycloakIdentityContractContributor()]);

    public KeycloakRealmDesiredStateDto Build(KeycloakRealmDesiredStateBuildRequestDto request)
    {
        var desiredState = new KeycloakRealmDesiredStateDto
        {
            Realm = request.Realm,
            BlazorClientId = request.BlazorClientId,
            ApiClientId = request.ApiClientId,
            DestructiveOperationsSupported = false
        };

        foreach (var contributor in _contributors)
        {
            contributor.Contribute(desiredState, request);
        }

        return desiredState;
    }
}
