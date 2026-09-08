using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Contracts.Services;

public interface IKeycloakIdentityContractContributor
{
    string ContractName { get; }

    void Contribute(KeycloakRealmDesiredStateDto desiredState, KeycloakRealmDesiredStateBuildRequestDto request);
}
