using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class RegistrationProviderCapabilityHttpTests
{
    [Test]
    public async Task SplitCapabilities_PreserveAuthenticationAndReachTheRealConnectionLookup()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            Type[] controllers =
            [
                typeof(RegistrationProviderConnectionsController),
                typeof(RegistrationProviderBindingsController),
                typeof(RegistrationProviderChannelsController),
                typeof(RegistrationProviderOperationsController)
            ];
            foreach (Type controller in controllers)
                await Assert.That(ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller)).IsNotNull();
        }

        string prefix = $"/api/tenants/{PlatformDefaults.DefaultTenantId:D}/events/{Guid.CreateVersion7():D}/registration-providers";
        string[] reads =
        [
            "connections", "bindings", "health",
            $"workflows/{Guid.CreateVersion7():D}/requirements/{Guid.CreateVersion7():D}/channels"
        ];
        foreach (string route in reads)
        {
            using var anonymous = await client.GetAsync($"{prefix}/{route}");
            await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        // Replace only the unchanged policy engine; controller, mediator, handler and repository are real.
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        using var missing = await client.GetAsync($"{prefix}/connections/{Guid.CreateVersion7():D}");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(missing.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(missing.Headers.CacheControl?.NoStore).IsTrue();
    }
}
