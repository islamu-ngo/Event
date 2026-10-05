using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
public sealed class EventDiscoveryCommitBoundAuthorityTests(EventResourcePersistenceTests.TestDatabase database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Reviewer_holds_ownership_grants_and_permissions_against_native_writers_until_commit()
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        int permissionId;
        string permissionCode;
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.SingleAsync(actor => actor.Id == scope.ActorId)).UserId!.Value;
            seed.EventRoleAssignments.Add(EventRoleAssignment.Create(scope.TenantAId, scope.EventAId,
                userId, (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active, Now.AddDays(-1),
                Now.AddDays(1), userId));
            var permission = await seed.RolePermissions.Where(pair => pair.RoleId == (int)RoleEnum.EventManager)
                .Select(pair => pair.Permission).FirstAsync();
            permissionId = permission.Id;
            permissionCode = permission.MasterCode;
            await seed.SaveChangesAsync();
        }
        await using var review = database.CreateIndependentContext();
        await using var revoke = database.CreateIndependentContext();
        await revoke.Database.OpenConnectionAsync();
        await revoke.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 0");
        await new EfCoreUnitOfWork(review).ExecuteSerializableAsync(async token =>
        {
            var snapshot = await new EventAuthoritySnapshotService(review).GetCommitBoundForUserAndEventsAsync(
                scope.TenantAId, userId, [scope.EventAId], Now, token);
            await Assert.That(snapshot.Events[scope.EventAId].PermissionCodes.Contains(permissionCode)).IsTrue();
            foreach (var entityType in new[] { typeof(Actor), typeof(Explore.Domain.Event),
                         typeof(EventRoleAssignment), typeof(Role), typeof(Permission), typeof(RolePermission) })
            {
                var entity = revoke.Model.FindEntityType(entityType)!;
                string tableName = entity.GetTableName()!;
                var mapping = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
                var sql = revoke.GetService<ISqlGenerationHelper>();
                string table = sql.DelimitIdentifier(tableName, entity.GetSchema());
                string key = sql.DelimitIdentifier(entity.FindPrimaryKey()!.Properties[0].GetColumnName(mapping)!);
                int result = SQLitePCL.raw.sqlite3_exec(
                    ((SqliteConnection)revoke.Database.GetDbConnection()).Handle,
                    $"UPDATE {table} SET {key} = {key}");
                await Assert.That((result & 255) is SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED).IsTrue();
            }
            return true;
        });
        await revoke.Permissions.Where(permission => permission.Id == permissionId).ExecuteUpdateAsync(
            setters => setters.SetProperty(permission => permission.IsActive, false));
        await new EfCoreUnitOfWork(review).ExecuteSerializableAsync(async token =>
        {
            var fresh = await new EventAuthoritySnapshotService(review).GetCommitBoundForUserAndEventsAsync(
                scope.TenantAId, userId, [scope.EventAId], Now, token);
            await Assert.That(fresh.Events[scope.EventAId].PermissionCodes.Contains(permissionCode)).IsFalse();
            return true;
        });
    }

    [Test]
    public async Task Committed_grant_revocation_before_protection_is_not_restored_from_tracking()
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        Guid assignmentId;
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.SingleAsync(actor => actor.Id == scope.ActorId)).UserId!.Value;
            var assignment = EventRoleAssignment.Create(scope.TenantAId, scope.EventAId, userId,
                (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active, Now.AddDays(-1), Now.AddDays(1), userId);
            assignmentId = assignment.Id;
            seed.EventRoleAssignments.Add(assignment);
            await seed.SaveChangesAsync();
        }
        await using var review = database.CreateIndependentContext();
        var tracked = await review.EventRoleAssignments.SingleAsync(row => row.Id == assignmentId);
        await using (var revoke = database.CreateIndependentContext())
        {
            var assignment = await revoke.EventRoleAssignments.SingleAsync(row => row.Id == assignmentId);
            assignment.Revoke(userId, Now);
            await revoke.SaveChangesAsync();
        }
        await new EfCoreUnitOfWork(review).ExecuteSerializableAsync(async token =>
        {
            var fresh = await new EventAuthoritySnapshotService(review).GetCommitBoundForUserAndEventsAsync(
                scope.TenantAId, userId, [scope.EventAId], Now, token);
            await Assert.That(fresh.Events[scope.EventAId].IsManager).IsFalse();
            await Assert.That(tracked.Status).IsEqualTo(EventRoleAssignmentStatus.Active);
            return true;
        });
    }
}
