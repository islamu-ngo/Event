using Explore.Application.Contracts.Infrastructure;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

public class SingleTenantAuthenticatedApiTestFixture : IAsyncInitializer, IAsyncDisposable
{
    public SingleTenantAuthenticatedWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public IAuthorizationProvider? AuthorizationProvider { get; set; }

    public async Task InitializeAsync()
    {
        Factory = new SingleTenantAuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = AuthorizationProvider
        };
        Client = Factory.CreateClient();

        await Task.CompletedTask;
    }

    public HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, Guid userId, string name = "Test User")
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId, name));
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await Factory.DisposeAsync();
    }
}
