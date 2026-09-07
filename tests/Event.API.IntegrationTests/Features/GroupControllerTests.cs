using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.Group;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class GroupControllerTests
{
    private readonly ApiTestFixture _fixture;
    private const string BaseUrl = "/api/group";

    public GroupControllerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task GetAll_ShouldReturnOk_WithPaginatedResult()
    {
        var response = await _fixture.Client.GetAsync(BaseUrl);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("items");
    }

    [Test]
    public async Task GetById_WithRandomId_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Create_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PostAsJsonAsync(BaseUrl, new CreateGroupDto
        {
            FullName = "Test Group"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Create_ActionRequiresAuthorizeMetadata_AndDoesNotAllowAnonymous()
    {
        var method = typeof(GroupController).GetMethod(nameof(GroupController.Create));

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)).IsNotEmpty();
        await Assert.That(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)).IsEmpty();
    }

    [Test]
    public async Task Create_WhenAuthenticated_PersistsAuthenticatedUserAsPendingGroupAdmin()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var authenticatedUserId = Guid.CreateVersion7();
        var groupName = $"Creator Binding {Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = JsonContent.Create(new CreateGroupDto
            {
                FullName = groupName
            })
        };
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                authenticatedUserId,
                "Group Creator",
                ("internal_user_id", authenticatedUserId.ToString("D"))));

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var membership = await dbContext.GroupMembers
            .Include(member => member.GroupTenant)
                .ThenInclude(participation => participation.Group)
            .SingleAsync(member => member.GroupTenant.Group.FullName == groupName);
        await Assert.That(membership.UserId).IsEqualTo(authenticatedUserId);
        await Assert.That(membership.RoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
        await Assert.That(membership.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(membership.GroupTenant.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(membership.GroupTenant.ApprovalStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
    }

    [Test]
    public async Task Create_WhenBodyContainsCreatorUserId_ReturnsBadRequestAndPersistsNoMembership()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var authenticatedUserId = Guid.CreateVersion7();
        var hostileCreatorUserId = Guid.CreateVersion7();
        var groupName = $"Hostile Creator {Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = JsonContent.Create(new
            {
                FullName = groupName,
                CreatorUserId = hostileCreatorUserId
            })
        };
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                authenticatedUserId,
                "Group Creator",
                ("internal_user_id", authenticatedUserId.ToString("D"))));

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await dbContext.Groups.AnyAsync(group => group.FullName == groupName)).IsFalse();
        await Assert.That(await dbContext.GroupMembers.AnyAsync(member => member.UserId == hostileCreatorUserId)).IsFalse();
    }

    [Test]
    public async Task Update_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PatchAsJsonAsync($"{BaseUrl}/{Guid.NewGuid()}", CreateUpdateDto());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdatePut_WhenUsingOldRoute_ShouldReturnMethodNotAllowed()
    {
        var response = await _fixture.Client.PutAsJsonAsync($"{BaseUrl}/{Guid.NewGuid()}", CreateUpdateDto());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task UpdatePatch_WhenAuthenticatedWithoutIfMatch_ShouldReturnBadRequest()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(CreateUpdateDto())
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Delete_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.DeleteAsync($"{BaseUrl}/{Guid.NewGuid()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static UpdateGroupDto CreateUpdateDto() =>
        new()
        {
            FullName = new UpdateGroupFullNameDto
            {
                Value = "Updated Group"
            }
        };
}
