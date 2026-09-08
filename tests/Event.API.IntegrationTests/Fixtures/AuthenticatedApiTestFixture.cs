// Uses AuthenticatedWebApplicationFactory. Auth is per-request via X-Test-Auth header (no shared state).

using Explore.Application.Contracts.Infrastructure;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

public class AuthenticatedApiTestFixture : IAsyncInitializer, IAsyncDisposable
{
    public AuthenticatedWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// The mock IAuthorizationProvider that can be configured per-test.
    /// Defaults to allow-all for endpoint access tests.
    /// </summary>
    public IAuthorizationProvider? AuthorizationProvider { get; set; }

    public async Task InitializeAsync()
    {
        Factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = AuthorizationProvider
        };
        Client = Factory.CreateClient();

        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates an HttpRequestMessage with the X-Test-Auth header for authenticated requests.
    /// </summary>
    public HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, Guid userId, string name = "Test User")
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId, name));
        return request;
    }

    /// <summary>
    /// Creates an HttpRequestMessage with instance admin claims.
    /// </summary>
    public HttpRequestMessage CreateInstanceAdminRequest(HttpMethod method, string url, Guid userId, string name = "Instance Admin")
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateInstanceAdminHeaderValue(userId, name));
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await Factory.DisposeAsync();
    }
}
