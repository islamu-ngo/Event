using System.Net;
using System.Net.Http.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Notification;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class GroupNotificationPreferenceAuthorizationHttpTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task ParentParticipationBoundaryRejectsDeletedAndForeignParentsForInstanceAdmins(bool useCerbos, bool foreignParent)
    {
        await using var factory = await NotificationHttpFixture.CreateAsync(useCerbos: useCerbos);
        Guid foreignParentId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(factory.OtherTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            foreignParentId = (await db.OrganizationTenants.SingleAsync(row => row.OrganizationId == factory.ForeignOrganizationId)).Id;
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = factory.StrangerId, User = null!,
                RoleId = (await db.Roles.SingleAsync(role => role.MasterCode == "platform.admin")).Id,
                Role = null!, GrantedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            var group = await db.GroupTenants.SingleAsync(row => row.GroupId == factory.GroupId);
            if (foreignParent)
            {
                group.ParentOrganizationTenantId = foreignParentId;
                // The relational composite FK rejects cross-tenant parent bindings at rest.
                await Assert.That(() => db.SaveChangesAsync()).Throws<DbUpdateException>();
                db.ChangeTracker.Clear();
            }
            else
            {
                (await db.OrganizationTenants.SingleAsync(row => row.Id == group.ParentOrganizationTenantId)).IsDeleted = true;
                await db.SaveChangesAsync();
            }
        }
        using var client = factory.Client(factory.StrangerId);
        if (foreignParent)
            await Assert.That((await client.GetFromJsonAsync<NotificationPreferenceMatrixDto>(Path(factory.GroupId)))!.OrganizationId)
                .IsEqualTo(factory.OrganizationId);
        else
            await AssertDeniedAsync(client, factory.GroupId);
        await AssertDeniedAsync(client, factory.ForeignGroupId);
        await AssertDeniedAsync(client, Guid.CreateVersion7());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnparentedGroupDoesNotBorrowAnOrganizationsMatchingIdButTenantAdminCanManageIt(bool useCerbos)
    {
        await using var factory = await NotificationHttpFixture.CreateAsync(useCerbos: useCerbos);
        Guid groupId = factory.OrganizationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var group = new Group { Id = groupId, FullName = "Unparented notification group", ConcurrencyStamp = Guid.CreateVersion7() };
            db.GroupTenants.Add(new GroupTenant
            {
                Id = Guid.CreateVersion7(), GroupId = groupId, Group = group,
                TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!, ConcurrencyStamp = Guid.CreateVersion7()
            });
            var membership = await db.TenantUsers.SingleAsync(row => row.UserId == factory.StrangerId);
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                TenantUserId = membership.Id, TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
            });
            await db.SaveChangesAsync();
        }
        using var parentAdmin = factory.Client(factory.UserId);
        using (var forbidden = await parentAdmin.PatchAsJsonAsync(Path(groupId), Preference()))
            await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var tenantAdmin = factory.Client(factory.StrangerId);
        using (var accepted = await tenantAdmin.PatchAsJsonAsync(Path(groupId), Preference()))
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var muted = await tenantAdmin.PutAsJsonAsync(Path(groupId) + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = true }))
            await Assert.That(muted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var matrix = (await tenantAdmin.GetFromJsonAsync<NotificationPreferenceMatrixDto>(Path(groupId)))!;
        await Assert.That(matrix.OrganizationId).IsNull();
        await Assert.That(matrix.Mute.IsMuted).IsTrue();
        await AssertDeniedAsync(tenantAdmin, factory.ForeignGroupId);
    }

    private static async Task AssertDeniedAsync(HttpClient client, Guid groupId)
    {
        using var read = await client.GetAsync(Path(groupId));
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var patch = await client.PatchAsJsonAsync(Path(groupId), Preference());
        await Assert.That(patch.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var mute = await client.PutAsJsonAsync(Path(groupId) + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = true });
        await Assert.That(mute.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static string Path(Guid groupId) => $"/api/group/{groupId}/notification-preferences";
    private static UpdateNotificationPreferenceMatrixDto Preference() => new()
    {
        Cells = [new UpdateNotificationPreferenceCellDto { CategoryCode = "marketing", ChannelCode = "email", IsEnabled = true }]
    };
}
