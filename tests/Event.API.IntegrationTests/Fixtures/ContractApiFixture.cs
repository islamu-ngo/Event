// Shared per assembly for lightweight contract validation (serialization, headers, ProblemDetails).

using TUnit.Core.Interfaces;
using Explore.API.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Contract test fixture for API surface validation on enrolled PostgreSQL context owners.
/// TestAuthHandler supplies protocol-test tickets; Local admission fixtures exercise native authentication.
/// Uses <see cref="AuthenticatedWebApplicationFactory"/> with <see cref="StubAuthorizationProvider"/>.
/// </summary>
public class ContractApiFixture : IAsyncInitializer, IAsyncDisposable
{
    public AuthenticatedWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider(),
            SeedActiveDefaultTenant = true
        };
        var origin = Factory.Services.GetRequiredService<IOptions<EffectiveHostAuthorityOptions>>()
            .Value.GetCanonicalOrigin();
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates an HTTP request with standard authenticated user claims.
    /// </summary>
    public HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string url,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            "X-Test-Auth",
            TestAuthHandler.CreateAuthHeaderValue(userId ?? Guid.NewGuid(), "Test User"));
        return request;
    }

    /// <summary>
    /// Creates an HTTP request with instance admin claims.
    /// </summary>
    public HttpRequestMessage CreateInstanceAdminRequest(
        HttpMethod method,
        string url,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            "X-Test-Auth",
            TestAuthHandler.CreateInstanceAdminHeaderValue(userId ?? Guid.NewGuid()));
        return request;
    }

    /// <summary>
    /// Creates an HTTP request with tenant admin claims for a specific tenant.
    /// </summary>
    public HttpRequestMessage CreateTenantAdminRequest(
        HttpMethod method,
        string url,
        Guid tenantId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(
            "X-Test-Auth",
            TestAuthHandler.CreateTenantAdminHeaderValue(userId ?? Guid.NewGuid(), tenantId));
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

}
