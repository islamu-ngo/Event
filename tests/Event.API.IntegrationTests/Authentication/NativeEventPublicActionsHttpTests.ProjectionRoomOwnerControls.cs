using System.Data.Common;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    public static IEnumerable<(int Operation, string Change)> ProjectionRoomBeforeOwnerCases()
    {
        for (int operation = 0; operation < 7; operation++)
            foreach (string change in new[] { "stamp", "binding", "admin", "source" })
                yield return (operation, change);
    }

    [Test]
    [MethodDataSource(nameof(ProjectionRoomBeforeOwnerCases))]
    public async Task OriginalProjectionAndRoomSourcesAreCapturedBeforeTransactionAndAcquiredBeforeBody(
        int operation, string change)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await ProjectionRoomFixture.CreateAsync(interceptor: boundary);
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ExploreDbContext>();
        var projection = operation < 6
            ? await SeedProjectionMaintenance(db, fixture.Data.PublicId, operation % 2 == 1)
            : (ParentId: Guid.Empty, ValueId: Guid.Empty);
        var room = operation == 6 ? await SeedRoomMutation(db) : default;
        object command = operation == 6 ? RoomMutation(room.Room, room.Destination.Id)
            : ProjectionCommand(operation, db.TenantContext!.TenantId, projection.ParentId);
        await GrantFooterAdministrator(db, fixture.Data.OwnerId, 1);
        var originalIdentity = await fixture.Local.ReadIdentityAsync();
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        boundary.Arm(db, async token =>
        {
            await using var changed = fixture.Local.Native.CreateDatabase();
            switch (change)
            {
                case "stamp":
                    await changed.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Data.OwnerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "binding":
                    await changed.UserExternalLogins.Where(row => row.Id == fixture.Local.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "admin":
                    await changed.PlatformUserRoles.Where(row => row.UserId == fixture.Data.OwnerId).ExecuteDeleteAsync(token);
                    break;
                case "source":
                    await changed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
            }
        });
        int completedBodies = 0;
        var owner = new ProjectionRoomAfterBodyOwner((ISerializableAttemptOwner)new EfCoreUnitOfWork(db), _ =>
        {
            completedBodies++;
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            InvokeProjectionRoomOwner(services, command, TestContext.Current!.Execution.CancellationToken, owner));
        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(completedBodies).IsEqualTo(0);
        await Assert.That(db.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Local.Native.CreateDatabase();
        observer.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
        await AssertProjectionRoomEffects(observer, operation, projection, room, false,
            TestContext.Current!.Execution.CancellationToken);
        // The independently committed source change is not part of the failed
        // body transaction and must remain visible after its rollback.
        if (change == "admin")
            await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.UserId == fixture.Data.OwnerId)).IsFalse();
        else if (change == "source")
            await Assert.That(await observer.SystemSettings.Where(row =>
                row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                .Select(row => row.Value).SingleAsync()).IsEqualTo("\"cerbos\"");
        else if (change == "binding")
            await Assert.That(await observer.UserExternalLogins.Where(row => row.Id == fixture.Local.Binding.ExternalLoginId)
                .Select(row => row.ProviderKey).SingleAsync()).IsNotEqualTo(fixture.Data.OwnerId.ToString("D"));
        else
            await Assert.That(await observer.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Data.OwnerId)
                .Select(row => row.SecurityStamp).SingleAsync()).IsNotEqualTo(
                    originalIdentity.SecurityStamp);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task ProjectionAndRoomConflictAcknowledgesOneRollbackAndRequiresANewAction(int operation)
    {
        var gate = new ProjectionRoomCommitConflict();
        await using var fixture = await ProjectionRoomFixture.CreateAsync(interceptor: gate);
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ExploreDbContext>();
        var projection = operation < 6
            ? await SeedProjectionMaintenance(db, fixture.Data.PublicId, operation % 2 == 1)
            : (ParentId: Guid.Empty, ValueId: Guid.Empty);
        var room = operation == 6 ? await SeedRoomMutation(db) : default;
        object NewCommand() => operation == 6 ? RoomMutation(room.Room, room.Destination.Id)
            : ProjectionCommand(operation, PlatformDefaults.DefaultTenantId, projection.ParentId);
        await GrantFooterAdministrator(db, fixture.Data.OwnerId, 1);
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var originalCommand = NewCommand();
        gate.Arm(db, token => AssertProjectionRoomEffects(db, operation, projection, room, true, token));
        await Assert.ThrowsAsync<HumanMutationAttemptRolledBackException>(() =>
            InvokeProjectionRoomOwner(services, originalCommand, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(gate.Attempts).IsEqualTo(1);
        await Assert.That(gate.BodiesObserved).IsEqualTo(1);
        await Assert.That(gate.Rollbacks).IsEqualTo(1);
        await Assert.That(gate.Commits).IsEqualTo(0);
        await Assert.That(db.Database.CurrentTransaction).IsNull();
        await using (var observer = fixture.Local.Native.CreateDatabase())
        {
            observer.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
            await AssertProjectionRoomEffects(observer, operation, projection, room, false,
                TestContext.Current!.Execution.CancellationToken);
        }

        // The owner never replays the admitted human operation. A fresh authenticated
        // request constructs new command custody and prepares new original sources.
        using var fresh = fixture.CreateScope();
        await fixture.AuthenticateAsync(fresh);
        var freshDb = fresh.ServiceProvider.GetRequiredService<ExploreDbContext>();
        gate.ObserveNewOwner(freshDb);
        await Assert.That(await InvokeProjectionRoomOwner(fresh.ServiceProvider, NewCommand(),
            TestContext.Current!.Execution.CancellationToken)).IsTrue();
        await Assert.That(gate.Attempts).IsEqualTo(2);
        await Assert.That(gate.Rollbacks).IsEqualTo(1);
        await Assert.That(gate.Commits).IsEqualTo(1);
        await using var committed = fixture.Local.Native.CreateDatabase();
        committed.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
        await AssertProjectionRoomEffects(committed, operation, projection, room, true,
            TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task ExternalIdentityCannotSupplyColocatedProjectionOrRoomAuthority(int operation)
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ExploreDbContext>();
        var projection = operation < 6
            ? await SeedProjectionMaintenance(db, fixture.Data.PublicId, operation % 2 == 1)
            : (ParentId: Guid.Empty, ValueId: Guid.Empty);
        var room = operation == 6 ? await SeedRoomMutation(db) : default;
        object command = operation == 6 ? RoomMutation(room.Room, room.Destination.Id)
            : ProjectionCommand(operation, PlatformDefaults.DefaultTenantId, projection.ParentId);
        await GrantFooterAdministrator(db, fixture.Data.OwnerId, 1);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            InvokeProjectionRoomOwner(services, command, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(db.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Local.Native.CreateDatabase();
        observer.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
        await AssertProjectionRoomEffects(observer, operation, projection, room, false,
            TestContext.Current!.Execution.CancellationToken);
    }

    private sealed class ProjectionRoomCommitConflict : DbTransactionInterceptor
    {
        private DbContext? _target;
        private Func<CancellationToken, Task>? _body;
        internal int Attempts { get; private set; }
        internal int BodiesObserved { get; private set; }
        internal int Rollbacks { get; private set; }
        internal int Commits { get; private set; }
        internal void Arm(DbContext target, Func<CancellationToken, Task> body)
        {
            _target = target;
            _body = body;
        }
        internal void ObserveNewOwner(DbContext target) => _target = target;
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target)) Attempts++;
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target) && Interlocked.Exchange(ref _body, null) is { } body)
            {
                await body(cancellationToken);
                BodiesObserved++;
                throw new SqliteException("Projection or room conflict before physical commit.", 5);
            }
            return result;
        }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target)) Rollbacks++;
            return Task.CompletedTask;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target)) Commits++;
            return Task.CompletedTask;
        }
    }
}
