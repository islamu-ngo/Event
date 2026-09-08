using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Services;

namespace Event.Application.UnitTests.Services;

public class KeycloakRealmDesiredStateBuilderTests
{
    [Test]
    public async Task Build_WithDefaultContributor_ReturnsEventRealmContract()
    {
        var builder = KeycloakRealmDesiredStateBuilder.CreateDefault();

        var desiredState = builder.Build(new KeycloakRealmDesiredStateBuildRequestDto
        {
            Realm = "ISLAMU",
            BlazorClientId = "islamu-event-blazor",
            ApiClientId = "islamu-event-api",
            BlazorRedirectUris = ["https://event.example.com/signin-oidc"],
            BlazorWebOrigins = ["https://event.example.com"]
        });

        await Assert.That(desiredState.DestructiveOperationsSupported).IsFalse();
        await Assert.That(desiredState.RequiredRealmRoles).Contains("offline_access");
        await Assert.That(desiredState.ClientScopes.Single(scope => scope.Name == "offline_access").RealmRoleMappings).Contains("offline_access");
        await Assert.That(desiredState.RoleComposites.Single(composite => composite.RoleName == "default-roles-islamu").CompositeRoleNames).Contains("offline_access");

        var blazorClient = desiredState.Clients.Single(client => client.ClientId == "islamu-event-blazor");
        await Assert.That(blazorClient.ClientKind).IsEqualTo("blazor-confidential");
        await Assert.That(blazorClient.RedirectUris).Contains("https://event.example.com/signin-oidc");
        await Assert.That(blazorClient.WebOrigins).Contains("https://event.example.com");
        await Assert.That(blazorClient.OptionalClientScopes).Contains("offline_access");

        var apiClient = desiredState.Clients.Single(client => client.ClientId == "islamu-event-api");
        await Assert.That(apiClient.ClientKind).IsEqualTo("api-bearer");
        await Assert.That(apiClient.ProtocolMappers.Single().IncludedClientAudience).IsEqualTo("islamu-event-api");
    }

    [Test]
    public async Task Build_WithAdditionalContributor_ComposesFutureProjectClient()
    {
        var builder = new KeycloakRealmDesiredStateBuilder(
        [
            new EventKeycloakIdentityContractContributor(),
            new FutureProjectContributor()
        ]);

        var desiredState = builder.Build(new KeycloakRealmDesiredStateBuildRequestDto
        {
            Realm = "ISLAMU",
            BlazorClientId = "islamu-event-blazor"
        });

        await Assert.That(desiredState.Clients.Select(client => client.ClientId)).Contains("identity-service-api");
        await Assert.That(desiredState.RequiredRealmRoles).Contains("identity-service-reader");
        await Assert.That(desiredState.DestructiveOperationsSupported).IsFalse();
    }

    private sealed class FutureProjectContributor : IKeycloakIdentityContractContributor
    {
        public string ContractName => "identity-service";

        public void Contribute(KeycloakRealmDesiredStateDto desiredState, KeycloakRealmDesiredStateBuildRequestDto request)
        {
            desiredState.RequiredRealmRoles = desiredState.RequiredRealmRoles.Append("identity-service-reader").ToArray();
            desiredState.Clients = desiredState.Clients.Append(new KeycloakClientDesiredStateDto
            {
                ClientId = "identity-service-api",
                DisplayName = "identity-service-api",
                ClientKind = "api-bearer",
                BearerOnly = true
            }).ToArray();
        }
    }
}
