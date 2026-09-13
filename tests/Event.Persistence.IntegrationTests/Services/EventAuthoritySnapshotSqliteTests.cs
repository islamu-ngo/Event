using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Services;

public sealed class EventAuthoritySnapshotSqliteTests
{
    [Test]
    public async Task Snapshot_UnionsActivePermissionScalarsWithoutLosingRolesOrOwnership()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync();
        var roleOnly = await fixture.SeedEventAsync();
        var db = fixture.Context;
        int[] roleIds = [(int)RoleEnum.EventOwner, (int)RoleEnum.EventManager, (int)RoleEnum.CheckInStaff];
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(row => roleIds.Contains(row.RoleId)).ToListAsync());
        await db.SaveChangesAsync();
        var update = await db.Permissions.SingleAsync(permission => permission.MasterCode == PermissionCodes.EventUpdate);
        var team = await db.Permissions.SingleAsync(permission => permission.MasterCode == PermissionCodes.EventManageTeam);
        var view = await db.Permissions.SingleAsync(permission => permission.MasterCode == PermissionCodes.EventView);
        var inactive = await db.Permissions.SingleAsync(permission => permission.MasterCode == PermissionCodes.EventDelete);
        inactive.IsActive = false;
        db.RolePermissions.AddRange(
            Grant(RoleEnum.EventOwner, update.Id), Grant(RoleEnum.EventOwner, team.Id), Grant(RoleEnum.EventOwner, inactive.Id),
            Grant(RoleEnum.EventManager, update.Id), Grant(RoleEnum.EventManager, view.Id));
        db.EventRoleAssignments.AddRange(
            Assignment(fixture, target.Id, RoleEnum.EventOwner),
            Assignment(fixture, target.Id, RoleEnum.EventManager),
            Assignment(fixture, roleOnly.Id, RoleEnum.CheckInStaff));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = fixture.Services.GetRequiredService<IEventAuthoritySnapshotService>();
        var snapshot = await service.GetForUserAndEventsAsync(fixture.TenantId, fixture.UserId,
            [target.Id, roleOnly.Id, target.Id], CancellationToken.None);
        await Assert.That(snapshot.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(snapshot.UserId).IsEqualTo(fixture.UserId);
        await Assert.That(snapshot.Events.Count).IsEqualTo(2);
        await Assert.That(snapshot.Events[target.Id].PermissionCodes).IsEquivalentTo(new[]
        {
            PermissionCodes.EventUpdate, PermissionCodes.EventManageTeam, PermissionCodes.EventView
        });
        await Assert.That(snapshot.Events[target.Id].RoleCodes.Count).IsEqualTo(2);
        await Assert.That(snapshot.Events[target.Id].IsOwner).IsTrue();
        await Assert.That(snapshot.Events[target.Id].IsManager).IsTrue();
        await Assert.That(snapshot.Events[roleOnly.Id].RoleCodes.Count).IsEqualTo(1);
        await Assert.That(snapshot.Events[roleOnly.Id].PermissionCodes).IsEmpty();
        await Assert.That(snapshot.Events[roleOnly.Id].IsOwner).IsFalse();
        await Assert.That(snapshot.Events[roleOnly.Id].IsManager).IsFalse();
        await Assert.That(db.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    public async Task Snapshot_BindsTenantUserRequestedEventsAndEffectiveAssignmentWindow()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var active = await fixture.SeedEventAsync();
        var future = await fixture.SeedEventAsync();
        var expired = await fixture.SeedEventAsync();
        var pending = await fixture.SeedEventAsync();
        var revoked = await fixture.SeedEventAsync();
        var notRequested = await fixture.SeedEventAsync();
        var activeAssignment = Assignment(fixture, active.Id, RoleEnum.EventOwner);
        var futureAssignment = Assignment(fixture, future.Id, RoleEnum.EventOwner);
        futureAssignment.StartsAtUtc = DateTime.UtcNow.AddDays(30);
        var expiredAssignment = Assignment(fixture, expired.Id, RoleEnum.EventOwner);
        expiredAssignment.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1);
        var pendingAssignment = Assignment(fixture, pending.Id, RoleEnum.EventOwner);
        pendingAssignment.Status = EventRoleAssignmentStatus.Pending;
        var revokedAssignment = Assignment(fixture, revoked.Id, RoleEnum.EventOwner);
        revokedAssignment.Revoke(fixture.UserId, DateTime.UtcNow.AddDays(-1));
        fixture.Context.EventRoleAssignments.AddRange(activeAssignment, futureAssignment, expiredAssignment,
            pendingAssignment, revokedAssignment, Assignment(fixture, notRequested.Id, RoleEnum.EventOwner));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var service = fixture.Services.GetRequiredService<IEventAuthoritySnapshotService>();
        var missing = Guid.CreateVersion7();
        Guid[] ids = [active.Id, future.Id, expired.Id, pending.Id, revoked.Id, missing];
        var snapshot = await service.GetForUserAndEventsAsync(fixture.TenantId, fixture.UserId, ids, default);
        await Assert.That(snapshot.Events.Keys).IsEquivalentTo(ids);
        await Assert.That(snapshot.Events[active.Id].IsOwner).IsTrue();
        await Assert.That(snapshot.Events[active.Id].PermissionCodes).Contains(PermissionCodes.EventUpdate);
        foreach (var id in new[] { future.Id, expired.Id, pending.Id, revoked.Id, missing })
        {
            await Assert.That(snapshot.Events[id].RoleCodes).IsEmpty();
            await Assert.That(snapshot.Events[id].PermissionCodes).IsEmpty();
            await Assert.That(snapshot.Events[id].IsOwner).IsFalse();
        }
        var otherUser = await service.GetForUserAndEventsAsync(fixture.TenantId, Guid.CreateVersion7(), ids, default);
        await Assert.That(otherUser.Events.Values.All(authority => authority.PermissionCodes.Count == 0 && !authority.IsOwner)).IsTrue();
        var otherTenant = await service.GetForUserAndEventsAsync(Guid.CreateVersion7(), fixture.UserId, ids, default);
        await Assert.That(otherTenant.Events.Values.All(authority => authority.RoleCodes.Count == 0 && !authority.IsOwner)).IsTrue();
        await Assert.That((await service.GetForUserAndEventsAsync(fixture.TenantId, fixture.UserId, [], default)).Events).IsEmpty();
    }

    private static EventRoleAssignment Assignment(EventVisitorCapabilitySqliteFixture fixture, Guid eventId, RoleEnum role) =>
        EventRoleAssignment.Create(fixture.TenantId, eventId, fixture.UserId, (int)role,
            EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddDays(-30), null, fixture.UserId);

    private static RolePermission Grant(RoleEnum role, int permissionId) => new()
    {
        RoleId = (int)role, Role = null!, PermissionId = permissionId, Permission = null!, GrantedAt = DateTime.UtcNow
    };
}
