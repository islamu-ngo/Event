using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Group;
using Explore.Application.Exceptions;
using Explore.Application.Features.Groups.Requests.Commands;
using Explore.Application.Features.Groups.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeGroupsHttpTests
{
    [Test]
    public async Task Writes_PreserveCreationHierarchyConcurrencyApprovalAndSoftDeletion()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var userId = await SeedAsync(factory);
        using var client = AuthenticatedClient(factory, userId);
        var rootId = await CreateAsync(client, new { fullName = "Root", description = "Original" });
        var childId = await CreateAsync(client, new { fullName = "Child", parentGroupId = rootId });
        var root = await ReadParticipationAsync(factory, rootId);
        var child = await ReadParticipationAsync(factory, childId);
        await Assert.That(child.ParentGroupTenantId).IsEqualTo((Guid?)root.Id);
        await Assert.That(root.ApprovalStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
        await Assert.That(root.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var actor = await db.Actors.AsNoTracking().Include(item => item.Pii)
                .SingleAsync(item => item.GroupId == rootId);
            await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.Group);
            await Assert.That(actor.Pii.DisplayName).IsEqualTo("Root");
            var member = await db.GroupMembers.AsNoTracking().SingleAsync(item => item.GroupTenantId == root.Id);
            await Assert.That(member.UserId).IsEqualTo(userId);
            await Assert.That(member.RoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
            await Assert.That(member.TenantId).IsEqualTo(root.TenantId);
        }

        object[] invalidCreates =
        [
            new { fullName = "" },
            new { fullName = "Missing parent", parentGroupId = Guid.CreateVersion7() },
            new { fullName = "Two parents", parentGroupId = rootId, parentOrganizationId = Guid.CreateVersion7() }
        ];
        foreach (var body in invalidCreates)
        {
            using var invalid = await client.PostAsJsonAsync("/api/group", body);
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var missingStamp = await client.PatchAsJsonAsync($"/api/group/{rootId}",
            new { fullName = new { value = "Rejected" } }))
        {
            await Assert.That(missingStamp.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var cycle = await PatchAsync(client, rootId,
            new { parentGroup = new { value = new { hasValue = true, value = childId } } }, root.Group.ConcurrencyStamp))
        {
            await Assert.That(cycle.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That((await JsonAsync(cycle)).GetProperty("code").GetString()).IsEqualTo("validation_failed");
        }
        using (var selfParent = await PatchAsync(client, rootId,
            new { parentGroup = new { value = new { hasValue = true, value = rootId } } }, root.Group.ConcurrencyStamp))
        {
            await Assert.That(selfParent.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        var unchanged = await ReadParticipationAsync(factory, rootId);
        await Assert.That(unchanged.ParentGroupTenantId).IsNull();
        await Assert.That(unchanged.Group.FullName).IsEqualTo("Root");
        await Assert.That(unchanged.Group.ConcurrencyStamp).IsEqualTo(root.Group.ConcurrencyStamp);
        using (var renamed = await PatchAsync(client, rootId,
            new { fullName = new { value = "Renamed root" } }, root.Group.ConcurrencyStamp))
        {
            await Assert.That(renamed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(renamed)).GetProperty("success").GetBoolean()).IsTrue();
        }
        var changed = await ReadParticipationAsync(factory, rootId);
        await Assert.That(changed.Group.FullName).IsEqualTo("Renamed root");
        await Assert.That(changed.Group.Description).IsEqualTo("Original");
        await Assert.That(changed.Group.ConcurrencyStamp).IsNotEqualTo(root.Group.ConcurrencyStamp);
        using (var stale = await PatchAsync(client, rootId,
            new { fullName = new { value = "Stale overwrite" } }, root.Group.ConcurrencyStamp))
        {
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        using (var invalidApproval = await client.PutAsJsonAsync($"/api/group/{rootId}/approval-status",
            new { approvalStatusId = int.MaxValue }))
        {
            await Assert.That(invalidApproval.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var approved = await client.PutAsJsonAsync($"/api/group/{rootId}/approval-status",
            new { approvalStatusId = (int)ApprovalStatusEnum.Approved }))
        {
            await Assert.That(approved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await Assert.That((await ReadParticipationAsync(factory, rootId)).ApprovalStatusId)
            .IsEqualTo((int)ApprovalStatusEnum.Approved);
        using (var detached = await PatchAsync(client, childId,
            new { parentGroup = new { value = new { hasValue = true, value = (Guid?)null } } },
            child.Group.ConcurrencyStamp))
        {
            await Assert.That(detached.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await Assert.That((await ReadParticipationAsync(factory, childId)).ParentGroupTenantId).IsNull();
        using (var deleted = await client.DeleteAsync($"/api/group/{childId}"))
        {
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(deleted)).GetProperty("success").GetBoolean()).IsTrue();
        }
        using var finalScope = factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That((await finalDb.Groups.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(group => group.Id == childId)).IsDeleted).IsTrue();
        await Assert.That(await finalDb.Groups.IgnoreQueryFilters().CountAsync()).IsEqualTo(2);
        await Assert.That((await finalDb.Groups.AsNoTracking().SingleAsync()).FullName).IsEqualTo("Renamed root");
    }

    [Test]
    public async Task Reads_PreservePresentationFilteringAndTenantBoundMembership()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var userId = await SeedAsync(factory);
        using var client = AuthenticatedClient(factory, userId);
        using var anonymous = factory.CreateClient();
        var alphaId = await CreateAsync(client, new { fullName = "Alpha" });
        var betaId = await CreateAsync(client, new { fullName = "Beta" });
        var foreignId = Guid.CreateVersion7();
        var objectId = Guid.CreateVersion7();
        var contentPath = $"/api/storageobject/{objectId}/content";
        var publicPath = $"/api/storageobject/{objectId}/public";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var alphaActor = await db.Actors.Include(actor => actor.Pii).SingleAsync(actor => actor.GroupId == alphaId);
            var betaActor = await db.Actors.Include(actor => actor.Pii).SingleAsync(actor => actor.GroupId == betaId);
            alphaActor.Pii.ProfilePictureUri = "private/raw-object-key";
            betaActor.Pii.ProfilePictureUri = contentPath;
            var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
            var foreignTenant = new Tenant
            {
                Id = Guid.CreateVersion7(), Slug = "foreign-group", FullName = "Foreign group tenant",
                TenantStatusId = status.Id, TenantStatus = status
            };
            db.Tenants.Add(foreignTenant);
            var foreignGroup = new Group { Id = foreignId, FullName = "Foreign" };
            var foreign = new GroupTenant
            {
                Id = Guid.CreateVersion7(), TenantId = foreignTenant.Id, Tenant = foreignTenant,
                GroupId = foreignId, Group = foreignGroup,
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending, ApprovalStatus = null!
            };
            db.GroupTenants.Add(foreign);
            db.GroupMembers.Add(new GroupMember
            {
                Id = Guid.CreateVersion7(), GroupTenantId = foreign.Id, GroupTenant = foreign,
                TenantId = foreignTenant.Id, Tenant = foreignTenant,
                UserId = userId, User = null!, RoleId = (int)RoleEnum.GroupAdmin, Role = null!
            });
            await db.SaveChangesAsync();
        }
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .SetTenant(PlatformDefaults.DefaultTenantId);
            var detail = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetGroupDetailsRequest, GroupDto?>>();
            var list = scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetGroupListRequest, PaginatedResult<GroupListDto>>>();
            var mine = scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetMyGroupsRequest, PaginatedResult<GroupListDto>>>();
            await Assert.That(await detail.QueryAsync(new GetGroupDetailsRequest(Guid.CreateVersion7()), default)).IsNull();
            var alpha = await detail.QueryAsync(new GetGroupDetailsRequest(alphaId), default)
                ?? throw new InvalidOperationException("Expected Alpha.");
            var beta = await detail.QueryAsync(new GetGroupDetailsRequest(betaId), default)
                ?? throw new InvalidOperationException("Expected Beta.");
            await Assert.That(alpha.ActorProfilePictureUri).IsNull();
            await Assert.That(beta.ActorProfilePictureUri).IsEqualTo(publicPath);
            var repeated = await detail.QueryAsync(new GetGroupDetailsRequest(betaId), default)
                ?? throw new InvalidOperationException("Expected the cached group.");
            await Assert.That(repeated.ActorProfilePictureUri).IsEqualTo(publicPath);
            var page = await list.QueryAsync(new GetGroupListRequest { PageNumber = 1, PageSize = 2 }, default);
            await Assert.That(page.Items.Select(group => group.Id)).IsEquivalentTo(new[] { alphaId, betaId });
            await Assert.That(page.TotalCount).IsEqualTo(3);
            await Assert.That(page.Items.Single(group => group.Id == betaId).ActorProfilePictureUri).IsEqualTo(publicPath);
            var own = await mine.QueryAsync(new GetMyGroupsRequest { UserId = userId.ToString() }, default);
            await Assert.That(own.Items.Select(group => group.Id)).IsEquivalentTo(new[] { alphaId, betaId });
            await Assert.That(own.Items.All(group => group.CurrentUserRoleId == (int)RoleEnum.GroupAdmin)).IsTrue();
            await Assert.That((await mine.QueryAsync(new GetMyGroupsRequest { UserId = "invalid" }, default)).Items).IsEmpty();
        }
        using var read = await anonymous.GetAsync($"/api/group/{betaId}");
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await JsonAsync(read);
        await Assert.That(body.GetProperty("actorProfilePictureUri").GetString()).IsEqualTo(publicPath);
        await Assert.That(body.TryGetProperty("userEmail", out _)).IsFalse();
        await Assert.That(body.TryGetProperty("members", out _)).IsFalse();
        await Assert.That(body.GetProperty("_links").TryGetProperty("edit", out _)).IsFalse();
        using var myResponse = await client.GetAsync("/api/group/my");
        await Assert.That(myResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var ownBody = await JsonAsync(myResponse);
        await Assert.That(ownBody.GetProperty("_embedded").GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("id").GetGuid()))
            .IsEquivalentTo(new[] { alphaId, betaId });
        using var unauthorized = await anonymous.GetAsync("/api/group/my");
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var missing = await anonymous.GetAsync($"/api/group/{Guid.CreateVersion7()}");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That((await verifyDb.Actors.AsNoTracking().Include(actor => actor.Pii)
            .SingleAsync(actor => actor.GroupId == betaId)).Pii.ProfilePictureUri).IsEqualTo(contentPath);
    }

    [Test]
    public async Task NativeUpdateAndApproval_DenyBeforeChangingGroupState()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { CheckPredicate = _ => false }
        };
        var userId = await SeedAsync(factory);
        using var client = AuthenticatedClient(factory, userId);
        var id = await CreateAsync(client, new { fullName = "Denied group" });
        var before = await ReadParticipationAsync(factory, id);
        using var scope = factory.Services.CreateScope();
        var update = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<UpdateGroupCommand, BaseCommandResponse<Guid>>>();
        var approval = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<UpdateGroupApprovalStatusCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(async () => await update.ExecuteAsync(new UpdateGroupCommand
        {
            GroupId = id, UserId = userId.ToString(), ExpectedConcurrencyStamp = before.Group.ConcurrencyStamp,
            UpdateGroupDto = new() { FullName = new() { Value = "Forbidden change" } }
        }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await approval.ExecuteAsync(new UpdateGroupApprovalStatusCommand
        {
            Id = id, GroupApprovalStatusDto = new() { ApprovalStatusId = (int)ApprovalStatusEnum.Approved }
        }, default)).Throws<AuthorizationException>();
        var after = await ReadParticipationAsync(factory, id);
        await Assert.That(after.Group.FullName).IsEqualTo(before.Group.FullName);
        await Assert.That(after.Group.ConcurrencyStamp).IsEqualTo(before.Group.ConcurrencyStamp);
        await Assert.That(after.ApprovalStatusId).IsEqualTo(before.ApprovalStatusId);
    }

    private static HttpClient AuthenticatedClient(AuthenticatedWebApplicationFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Group user", ("internal_user_id", userId.ToString("D"))));
        client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        return client;
    }

    private static async Task<Guid> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        db.Tenants.Add(new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId, Slug = "native-groups", FullName = "Native groups",
            TenantStatusId = status.Id, TenantStatus = status
        });
        var userId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = userId, Pii = new() { Email = "group-user@example.test", FirstName = "Group", LastName = "User" }
        });
        var role = await db.Roles.SingleAsync(item => item.Id == (int)RoleEnum.GroupAdmin);
        foreach (var code in new[] { PermissionCodes.GroupManage, PermissionCodes.GroupDelete })
        {
            var permission = await db.Set<Permission>().SingleOrDefaultAsync(item => item.MasterCode == code);
            if (permission is null)
            {
                permission = new Permission
                {
                    MasterCode = code, ResourceKind = "group", Action = code.Split(':')[1],
                    FullName = code, GroupName = "Groups", RoleScopeId = role.RoleScopeId, IsActive = true
                };
                db.Set<Permission>().Add(permission);
            }
            if (!await db.Set<RolePermission>().AnyAsync(item =>
                item.RoleId == role.Id && item.PermissionId == permission.Id))
            {
                db.Set<RolePermission>().Add(new RolePermission
                {
                    RoleId = role.Id, Role = role, PermissionId = permission.Id, Permission = permission
                });
            }
        }
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/group", body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = await JsonAsync(response);
        await Assert.That(result.GetProperty("success").GetBoolean()).IsTrue();
        var id = result.GetProperty("id").GetGuid();
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        return id;
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, Guid stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/group/{id}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await client.SendAsync(request);
    }

    private static async Task<GroupTenant> ReadParticipationAsync(AuthenticatedWebApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().GroupTenants
            .AsNoTracking().Include(item => item.Group).SingleAsync(item => item.GroupId == id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
