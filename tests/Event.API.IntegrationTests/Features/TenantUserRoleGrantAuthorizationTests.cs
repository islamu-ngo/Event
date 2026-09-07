using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class TenantUserRoleGrantAuthorizationTests
{
    [Test]
    public async Task GetAll_WhenAuthorizationProviderDenies_ReturnsForbidden()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/tenant-user-role-grants");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetById_WhenAuthorizationProviderDenies_ReturnsForbidden()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/tenant-user-role-grants/{Guid.NewGuid():D}");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }
}
