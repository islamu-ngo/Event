using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Services;

public class EventKeycloakIdentityContractContributor : IKeycloakIdentityContractContributor
{
    public string ContractName => "islamu-event";

    public void Contribute(KeycloakRealmDesiredStateDto desiredState, KeycloakRealmDesiredStateBuildRequestDto request)
    {
        desiredState.RequiredRealmRoles = Merge(desiredState.RequiredRealmRoles, ["offline_access"]);
        desiredState.RoleComposites = MergeRoleComposites(
            desiredState.RoleComposites,
            new KeycloakRoleCompositeDesiredStateDto
            {
                RoleName = $"default-roles-{request.Realm.ToLowerInvariant()}",
                CompositeRoleNames = ["offline_access"]
            });
        desiredState.ClientScopes = MergeClientScopes(
            desiredState.ClientScopes,
            new KeycloakClientScopeDesiredStateDto
            {
                Name = "offline_access",
                RealmRoleMappings = ["offline_access"]
            });

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
            WebOrigins = request.BlazorWebOrigins,
            OptionalClientScopes = ["offline_access"]
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

    private static IReadOnlyList<string> Merge(IReadOnlyList<string> existing, IReadOnlyList<string> additions) =>
        existing.Concat(additions)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<KeycloakRoleCompositeDesiredStateDto> MergeRoleComposites(
        IReadOnlyList<KeycloakRoleCompositeDesiredStateDto> existing,
        KeycloakRoleCompositeDesiredStateDto addition)
    {
        var composites = existing.ToDictionary(composite => composite.RoleName, StringComparer.OrdinalIgnoreCase);
        if (composites.TryGetValue(addition.RoleName, out var current))
        {
            current.CompositeRoleNames = Merge(current.CompositeRoleNames, addition.CompositeRoleNames);
        }
        else
        {
            composites[addition.RoleName] = addition;
        }

        return composites.Values.ToArray();
    }

    private static IReadOnlyList<KeycloakClientScopeDesiredStateDto> MergeClientScopes(
        IReadOnlyList<KeycloakClientScopeDesiredStateDto> existing,
        KeycloakClientScopeDesiredStateDto addition)
    {
        var scopes = existing.ToDictionary(scope => scope.Name, StringComparer.OrdinalIgnoreCase);
        if (scopes.TryGetValue(addition.Name, out var current))
        {
            current.RealmRoleMappings = Merge(current.RealmRoleMappings, addition.RealmRoleMappings);
        }
        else
        {
            scopes[addition.Name] = addition;
        }

        return scopes.Values.ToArray();
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
