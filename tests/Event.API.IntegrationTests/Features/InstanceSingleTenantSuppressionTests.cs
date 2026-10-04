using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Fast)]
[NotInParallel("SingleTenantAuthenticatedApiFixture")]
[ClassDataSource<SingleTenantAuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public sealed class InstanceSingleTenantSuppressionTests(SingleTenantAuthenticatedApiTestFixture fixture)
{
    [Test]
    public async Task MultiTenantInstanceEndpoints_WithInstanceAdminInSingleTenant_ReturnForbidden()
    {
        string[] endpoints =
        [
            "/api/admin/instance/tenants",
            "/api/admin/instance/domains"
        ];

        foreach (var endpoint in endpoints)
        {
            using var request = CreateInstanceAdminRequest(endpoint);
            using var response = await fixture.Client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    private static HttpRequestMessage CreateInstanceAdminRequest(string endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateInstanceAdminHeaderValue(Guid.NewGuid()));
        return request;
    }
}
