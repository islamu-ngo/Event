using Event.Api.IntegrationTests.Builders;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

public class ApiTestFixture : IAsyncInitializer, IAsyncDisposable
{
    public CustomWebApplicationFactory Factory { get; private set; }
    public HttpClient Client { get; private set; }

    public async Task InitializeAsync()
    {
        Factory = new CustomWebApplicationFactory();
        Client = Factory.CreateClient();
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (await database.Tenants.FindAsync(PlatformDefaults.DefaultTenantId) is null)
        {
            database.Tenants.Add(new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId).Build());
            await database.SaveChangesAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (Factory is null)
        {
            return;
        }

        try
        {
            await Factory.DisposeAsync();
        }
        catch (NullReferenceException ex)
        {
            // Workaround for intermittent WebApplicationFactory teardown race in test host.
            Console.WriteLine($"Ignoring WebApplicationFactory teardown NullReferenceException: {ex.Message}");
        }
    }
}
