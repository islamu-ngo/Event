using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Blazor.IntegrationTests.Extensions;

public sealed class ApiServiceDiscoveryTests
{
    [Test]
    public async Task AddApiHttpClients_WhenAspireServiceReferenceExists_UsesAspireApiUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["services:explore-api:https:0"] = "https://localhost:7211"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICircuitAccessTokenService>());
        services.AddSingleton(Substitute.For<ICircuitUserContext>());
        services.AddSingleton(Substitute.For<ICircuitTokenStore>());
        services.AddSingleton(Substitute.For<ITenantRouteContextAccessor>());
        services.AddSingleton(Substitute.For<ISetupSecretResolver>());
        services.AddSingleton(CreateSupportAccessSessionStore());

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);

        services.AddApiHttpClients(configuration, environment);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IInstanceMessagingSettingsClient));

        await Assert.That(client.BaseAddress).IsEqualTo(new Uri("https://localhost:7211/"));
    }

    private static IBffSupportAccessSessionStore CreateSupportAccessSessionStore()
    {
        var store = Substitute.For<IBffSupportAccessSessionStore>();
        store.ResolveCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BffSupportAccessStoreResult.Failed("session_not_found")));
        return store;
    }
}
