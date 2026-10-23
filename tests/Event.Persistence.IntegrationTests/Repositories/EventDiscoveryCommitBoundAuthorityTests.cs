using Explore.Domain;
using Explore.Application.Exceptions;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Services;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

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
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_new_role_source_cannot_be_acquired_behind_the_event_fence(bool existingAssignment)
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        Guid? assignmentId = null;
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.SingleAsync(row => row.Id == scope.ActorId)).UserId!.Value;
            if (existingAssignment)
            {
                var assignment = EventRoleAssignment.Create(scope.TenantAId, scope.EventAId, userId,
                    (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active, Now.AddDays(-1), Now.AddDays(1), userId);
                assignmentId = assignment.Id;
                seed.EventRoleAssignments.Add(assignment);
                await seed.SaveChangesAsync();
            }
        }
        var change = new ChangeBeforeActorFence(async (context, token) =>
        {
            if (assignmentId is { } id)
                await context.EventRoleAssignments.Where(row => row.Id == id).ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), token);
            else
            {
                context.EventRoleAssignments.Add(EventRoleAssignment.Create(scope.TenantAId, scope.EventAId, userId,
                    (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active, Now.AddDays(-1), Now.AddDays(1), userId));
                await context.SaveChangesAsync(token);
            }
        });
        await using var review = database.CreateContext(change);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
        {
            await new EfCoreUnitOfWork(review).ExecuteSerializableAsync(token =>
                new EventAuthoritySnapshotService(review).GetCommitBoundForUserAndEventsAsync(
                    scope.TenantAId, userId, [scope.EventAId], Now, token));
        });
        await Assert.That(change.Observed).IsTrue();
        await using var current = database.CreateContext();
        var snapshot = await new EventAuthoritySnapshotService(current).GetForUserAndEventsAsync(
            scope.TenantAId, userId, [scope.EventAId], Now, CancellationToken.None);
        await Assert.That(snapshot.Events[scope.EventAId].IsManager).IsEqualTo(existingAssignment);
        await Assert.That(current.EventRoleAssignments.CountAsync(row => row.EventId == scope.EventAId))
            .IsEqualTo(existingAssignment ? 1 : 0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Borrowed_custody_plan_never_adds_replacement_actor_or_role_anchors(int change)
    {
        var scope = await database.SeedScopeAsync();
        Guid human;
        Guid assignmentId;
        var replacementId = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
        {
            human = (await seed.Actors.SingleAsync(row => row.Id == scope.ActorId)).UserId!.Value;
            var assignment = EventRoleAssignment.Create(scope.TenantAId, scope.EventAId, human,
                (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active, Now.AddDays(-1), Now.AddDays(1), human);
            assignmentId = assignment.Id;
            seed.EventRoleAssignments.Add(assignment);
            seed.Actors.Add(new Actor
            {
                Id = replacementId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = human,
                Pii = new ActorPii { DisplayName = "Replacement custody publisher" }
            });
            await seed.SaveChangesAsync();
        }
        var early = new CountBorrowedEarlyAnchors();
        await using var context = database.CreateContext(early);
        async Task ReadAsync()
        {
            await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
            {
                var snapshots = new EventAuthoritySnapshotService(context);
                await snapshots.GetCommitBoundForUserAndEventsAsync(scope.TenantAId, human, [scope.EventAId], Now, token);
                var plan = await EventAuthorityProviderOperations.ReadPlanAsync(context, scope.TenantAId,
                    [human], [scope.EventAId], token);
                if (change == 1)
                    await context.Events.Where(row => row.Id == scope.EventAId).ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.ActorId, replacementId), token);
                if (change == 2)
                    await context.EventRoleAssignments.Where(row => row.Id == assignmentId).ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), token);
                early.Active = true;
                var borrowed = await snapshots.GetCommitBoundForUsersAndEventsAsync(
                    scope.TenantAId, [human], [scope.EventAId], Now, token, acquiredPlan: plan);
                await Assert.That(borrowed[human].Events[scope.EventAId].IsManager).IsTrue();
                return true;
            });
        }
        if (change == 0) await ReadAsync();
        else await Assert.ThrowsAsync<ConcurrencyConflictException>(ReadAsync);
        await Assert.That(early.Count).IsEqualTo(0);
        await using var current = database.CreateContext();
        await Assert.That(await current.Events.Where(row => row.Id == scope.EventAId).Select(row => row.ActorId).SingleAsync())
            .IsEqualTo(scope.ActorId);
        await Assert.That(await current.EventRoleAssignments.Where(row => row.Id == assignmentId).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.EventManager);
    }

    private sealed class CountBorrowedEarlyAnchors : DbCommandInterceptor
    {
        public bool Active { get; set; }
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Active && eventData.Context is ExploreDbContext context)
            {
                var sql = context.GetService<ISqlGenerationHelper>();
                foreach (var type in new[] { typeof(Actor), typeof(Role), typeof(Permission), typeof(RolePermission) })
                {
                    var entity = context.Model.FindEntityType(type)!;
                    if (command.CommandText.StartsWith($"UPDATE {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())}",
                        StringComparison.OrdinalIgnoreCase)) Count++;
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public async Task Every_representatives_role_is_planned_before_any_actor_is_fenced()
    {
        var scope = await database.SeedScopeAsync();
        Guid managerId;
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = $"batch-owner-{Guid.CreateVersion7():N}@example.test", FirstName = "Batch", LastName = "Owner" }
        };
        Guid ownerAssignmentId;
        await using (var seed = database.CreateContext())
        {
            managerId = (await seed.Actors.SingleAsync(row => row.Id == scope.ActorId)).UserId!.Value;
            seed.Users.Add(owner);
            var ownerAssignment = EventRoleAssignment.Create(scope.TenantAId, scope.EventAId, owner.Id,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, Now.AddDays(-1), Now.AddDays(1), managerId);
            ownerAssignmentId = ownerAssignment.Id;
            seed.EventRoleAssignments.AddRange(ownerAssignment, EventRoleAssignment.Create(
                scope.TenantAId, scope.EventAId, managerId, (int)RoleEnum.EventManager,
                EventRoleAssignmentStatus.Active, Now.AddDays(-1), Now.AddDays(1), managerId));
            await seed.SaveChangesAsync();
        }
        var change = new ChangeBeforeActorFence(async (context, token) =>
            await context.EventRoleAssignments.Where(row => row.Id == ownerAssignmentId).ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), token));
        await using var review = database.CreateContext(change);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
        {
            await new EfCoreUnitOfWork(review).ExecuteSerializableAsync(token =>
                new EventAuthoritySnapshotService(review).GetCommitBoundForUsersAndEventsAsync(
                    scope.TenantAId, [managerId, owner.Id], [scope.EventAId], Now, token));
        });
        await Assert.That(change.Observed).IsTrue();
        await using var current = database.CreateContext();
        await Assert.That((await current.EventRoleAssignments.SingleAsync(row => row.Id == ownerAssignmentId)).RoleId)
            .IsEqualTo((int)RoleEnum.EventOwner);
    }

    private sealed class ChangeBeforeActorFence(Func<ExploreDbContext, CancellationToken, Task> change) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            var actor = context.Model.FindEntityType(typeof(Actor))!;
            string table = context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(actor.GetTableName()!, actor.GetSchema());
            if (!Observed && command.CommandText.StartsWith($"UPDATE {table}", StringComparison.Ordinal))
            {
                Observed = true;
                await change(context, cancellationToken);
            }
            return result;
        }
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
