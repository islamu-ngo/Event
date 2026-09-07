using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Organization;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Integration tests verifying that the authentication/authorization pipeline correctly
/// denies anonymous access to protected endpoints and allows authenticated access.
/// Uses per-request X-Test-Auth header for thread-safe parallel test execution.
/// </summary>
[NotInParallel("AuthenticatedApiFixture")]
[ClassDataSource<AuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public class AuthorizationIntegrationTests
{
    private readonly AuthenticatedApiTestFixture _fixture;

    public AuthorizationIntegrationTests(AuthenticatedApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Anonymous Deny Tests — Write Endpoints

    [Test]
    public async Task CreateOrganization_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange — no X-Test-Auth header = anonymous
        var dto = new CreateOrganizationDto
        {
            FullName = "Test Org",
            Email = "test@example.com",
            Country = "Belgium",
            City = "Brussels",
            Address = "Test Street 1",
            Postcode = 1000
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization")
        {
            Content = JsonContent.Create(dto)
        };

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateOrganization_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange — no X-Test-Auth header = anonymous
        var dto = new UpdateOrganizationDto
        {
            FullName = new UpdateOrganizationFullNameDto { Value = "Updated Org" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/organization/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{Guid.NewGuid():D}\"");

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetMyOrganizations_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange — no X-Test-Auth header = anonymous
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/organization/my");

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Anonymous Allow Tests — Read Endpoints

    [Test]
    [Arguments("/api/organization")]
    [Arguments("/api/event")]
    [Arguments("/api/eventsession")]
    [Arguments("/api/actor")]
    [Arguments("/api/category")]
    [Arguments("/api/tag")]
    public async Task GetAllPublicEndpoints_WithoutAuth_ShouldReturnOk(string endpoint)
    {
        // Arrange — no X-Test-Auth header = anonymous
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert — public read endpoints should be accessible without auth
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetPhysicalLocations_WithoutAuth_ShouldReturnUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/location");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Authenticated Allow Tests — Write Endpoints

    [Test]
    public async Task CreateOrganization_WithAuth_ShouldNotReturnUnauthorized()
    {
        // Arrange — authenticated user via X-Test-Auth header
        var dto = new CreateOrganizationDto
        {
            FullName = "Auth Org",
            Email = "auth@example.com",
            Country = "Belgium",
            City = "Brussels",
            Address = "Auth Street 1",
            Postcode = 1000
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid(), "Auth User"));

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert — should NOT be 401; could be 201, 400, or 403 depending on auth provider
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CreateOrganization_WithTenantAdmin_CreatesApprovedOrganization()
    {
        var tenantId = SeedIds.DefaultTenantId;
        var adminUserId = Guid.NewGuid();
        await SeedTenantAdminGrantAsync(tenantId, adminUserId);

        var dto = new CreateOrganizationDto
        {
            FullName = $"Tenant Admin Org {Guid.NewGuid():N}",
            Email = "tenant-admin-org@example.com",
            Country = "Belgium",
            City = "Brussels",
            Address = "Admin Street 1",
            Postcode = 1000
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(adminUserId, "Tenant Admin"));

        var response = await _fixture.Client.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var body = JsonSerializer.Deserialize<BaseCommandResponse<Guid>>(
            responseContent,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Assert.That(body).IsNotNull();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var organizationTenant = await db.OrganizationTenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(value => value.OrganizationId == body!.Id);

        await Assert.That(organizationTenant.ApprovalStatusId).IsEqualTo((int)ApprovalStatusEnum.Approved);
        await Assert.That(organizationTenant.ApprovedBy).IsEqualTo(adminUserId);
        await Assert.That(organizationTenant.ApprovedAt).IsNotNull();
    }

    private async Task SeedTenantAdminGrantAsync(Guid tenantId, Guid userId)
    {
        var createdAt = DateTime.UtcNow;
        var tenantUser = new TenantUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = createdAt,
            CreatedAt = createdAt
        };

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        db.TenantUsers.Add(tenantUser);
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tenant = null!,
            TenantUserId = tenantUser.Id,
            TenantUser = tenantUser,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = createdAt,
            CreatedAt = createdAt
        });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task GetMyOrganizations_WithAuth_ShouldNotReturnUnauthorized()
    {
        // Arrange — authenticated user via X-Test-Auth header
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/organization/my");
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid(), "Auth User"));

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Admin Claim Tests

    [Test]
    public async Task UpdateOrganizationStatus_WithInstanceAdmin_ShouldNotReturnUnauthorized()
    {
        // Arrange — instance admin via X-Test-Auth header
        var dto = new UpdateOrganizationApprovalStatusDto { ApprovalStatusId = 1 };

        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/organization/{Guid.NewGuid()}/approval-status")
        {
            Content = JsonContent.Create(dto)
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateInstanceAdminHeaderValue(Guid.NewGuid()));

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert — should NOT be 401; could be 404 (org not found) or other error
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateOrganizationStatus_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange — no X-Test-Auth header = anonymous
        var dto = new UpdateOrganizationApprovalStatusDto { ApprovalStatusId = 1 };

        using var request = new HttpRequestMessage(HttpMethod.Put,
            $"/api/organization/{Guid.NewGuid()}/approval-status")
        {
            Content = JsonContent.Create(dto)
        };

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion
}
