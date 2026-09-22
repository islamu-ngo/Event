using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Services;

public class EventKeycloakIdentityContractContributor : IKeycloakIdentityContractContributor
{
    public string ContractName => "islamu-event";

    public void Contribute(KeycloakRealmDesiredStateDto desiredState, KeycloakRealmDesiredStateBuildRequestDto request)
    {
        desiredState.Clients = MergeClients(
            desiredState.Clients,
            BuildBlazorClient(request),
            BuildApiClient(request));
    }

    private static KeycloakClientDesiredStateDto BuildBlazorClient(KeycloakRealmDesiredStateBuildRequestDto request) =>
        new()
        {
            ClientId = request.BlazorClientId,
            DisplayName = request.BlazorClientId,
            ClientKind = "blazor-confidential",
            Enabled = true,
            PublicClient = false,
            BearerOnly = false,
            StandardFlowEnabled = true,
            DirectAccessGrantsEnabled = false,
            ServiceAccountsEnabled = false,
            RedirectUris = request.BlazorRedirectUris,
            WebOrigins = request.BlazorWebOrigins
        };

    private static KeycloakClientDesiredStateDto? BuildApiClient(KeycloakRealmDesiredStateBuildRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiClientId))
            return null;

        return new KeycloakClientDesiredStateDto
        {
            ClientId = request.ApiClientId,
            DisplayName = request.ApiClientId,
            ClientKind = "api-bearer",
            Enabled = true,
            PublicClient = false,
            BearerOnly = true,
            StandardFlowEnabled = false,
            DirectAccessGrantsEnabled = false,
            ServiceAccountsEnabled = false,
            ProtocolMappers =
            [
                new KeycloakProtocolMapperDesiredStateDto
                {
                    Name = $"{request.ApiClientId}-audience",
                    MapperType = "oidc-audience-mapper",
                    IncludedClientAudience = request.ApiClientId,
                    AddToAccessToken = true,
                    AddToIdToken = false
                }
            ]
        };
    }

    private static IReadOnlyList<KeycloakClientDesiredStateDto> MergeClients(
        IReadOnlyList<KeycloakClientDesiredStateDto> existing,
        params KeycloakClientDesiredStateDto?[] additions)
    {
        var clients = existing.ToDictionary(client => client.ClientId, StringComparer.OrdinalIgnoreCase);
        foreach (var addition in additions.Where(addition => addition is not null))
        {
            clients[addition!.ClientId] = addition;
        }

        return clients.Values.ToArray();
    }
}
