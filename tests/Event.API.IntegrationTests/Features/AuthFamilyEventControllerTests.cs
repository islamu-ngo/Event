using System.Net;
using System.Text;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Authorization family matrix for EventController.
/// Verifies that GET endpoints are AllowAnonymous, write endpoints require Authorize,
/// and admin-level access behaves correctly through the full ASP.NET Core pipeline.
/// </summary>
[ClassDataSource<RealRuntimeApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public class AuthFamilyEventControllerTests(RealRuntimeApiFixture fixture)
{
    private readonly RealRuntimeApiFixture _fixture = fixture;

    #region Anonymous Access (AllowAnonymous GET, denied write)

    [Test]
    public async Task Anonymous_GetAll_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();

        var response = await _fixture.Client.GetAsync("/api/event");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Anonymous_GetById_NonExistent_ReturnsNotFound()
    {
        await _fixture.ResetDatabaseAsync();

        var response = await _fixture.Client.GetAsync($"/api/event/{Guid.NewGuid()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Anonymous_Post_ReturnsUnauthorized()
    {
        await _fixture.ResetDatabaseAsync();

        var content = new StringContent("""{"title":"Anon Event"}""", Encoding.UTF8, "application/json");
        var response = await _fixture.Client.PostAsync("/api/event", content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Anonymous_Patch_ReturnsUnauthorized()
    {
        await _fixture.ResetDatabaseAsync();

        var content = new StringContent("""{"title":{"value":"Updated"}}""", Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/event/{Guid.NewGuid()}") { Content = content };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{Guid.NewGuid():D}\"");
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Anonymous_Delete_ReturnsUnauthorized()
    {
        await _fixture.ResetDatabaseAsync();

        var response = await _fixture.Client.DeleteAsync($"/api/event/{Guid.NewGuid()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Authenticated User Access

    [Test]
    public async Task Authenticated_GetAll_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get, "/api/event", tenantResult.UserId);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Authenticated_GetById_SeededEvent_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var eventResult = await EventScenarioSeed.SeedPublishedEventAsync(
            context, tenantResult.ActorId, tenantResult.TenantId);

        var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/event/{eventResult.EventId}", tenantResult.UserId);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Authenticated_Post_PassesAuthGate()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        var request = _fixture.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/event", tenantResult.UserId);
        request.Content = new StringContent(
            """{"title":"Auth Gate Test Event"}""", Encoding.UTF8, "application/json");

        var response = await _fixture.Client.SendAsync(request);

        // The auth gate should pass — the response should NOT be 401.
        // It may be 201 (success), 400 (validation), or 500 (handler error) depending on pipeline state,
        // but the key assertion is that authentication succeeded.
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Admin Access

    [Test]
    public async Task InstanceAdmin_GetAll_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();

        var request = _fixture.CreateInstanceAdminRequest(HttpMethod.Get, "/api/event");
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task TenantAdmin_GetAll_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();

        using var scope = _fixture.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenantResult = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);

        var request = _fixture.CreateTenantAdminRequest(
            HttpMethod.Get, "/api/event", tenantResult.TenantId, tenantResult.UserId);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    #endregion
}
