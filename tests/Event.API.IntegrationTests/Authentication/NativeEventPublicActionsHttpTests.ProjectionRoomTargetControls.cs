using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(4)]
    [Arguments(5)]
    public async Task SingleProjectionAcquisitionRechecksItsPersistedParentBeforeUpdaterReads(int operation)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await ProjectionRoomFixture.CreateAsync(interceptor: boundary);
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ExploreDbContext>();
        bool session = operation == 5;
        var projection = await SeedProjectionMaintenance(db, fixture.Data.PublicId, session);
        await GrantFooterAdministrator(db, fixture.Data.OwnerId, 1);
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        boundary.Arm(db, async token =>
        {
            await using var changed = fixture.Local.Native.CreateDatabase();
            changed.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
            if (session)
                await changed.EventSessions.Where(row => row.Id == projection.ParentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
            else
                await changed.Events.Where(row => row.Id == projection.ParentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
        });
        int completedBodies = 0;
        var owner = new ProjectionRoomAfterBodyOwner((ISerializableAttemptOwner)new EfCoreUnitOfWork(db), _ =>
        {
            completedBodies++;
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<AuthorizationException>(() => InvokeProjectionRoomOwner(services,
            ProjectionCommand(operation, PlatformDefaults.DefaultTenantId, projection.ParentId),
            TestContext.Current!.Execution.CancellationToken, owner));
        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(completedBodies).IsEqualTo(0);
        await Assert.That(db.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Local.Native.CreateDatabase();
        observer.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
        await AssertProjectionRoomEffects(observer, operation, projection, default, false,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(session
            ? await observer.EventSessions.AnyAsync(row => row.Id == projection.ParentId)
            : await observer.Events.AnyAsync(row => row.Id == projection.ParentId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HumanRoomUpdateKeepsPersistedSourceAndDestinationGuardsAfterSourceAdmission(bool destination)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await ProjectionRoomFixture.CreateAsync(interceptor: boundary);
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ExploreDbContext>();
        var room = await SeedRoomMutation(db);
        await GrantFooterAdministrator(db, fixture.Data.OwnerId, 2);
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var command = RoomMutation(room.Room, room.Destination.Id);
        Guid erasedId = destination ? room.Destination.Id : room.Room.LocationId;
        boundary.Arm(db, async token =>
        {
            await using var changed = fixture.Local.Native.CreateDatabase();
            changed.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
            await changed.Locations.Where(row => row.Id == erasedId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.LocationPrivacyStateId,
                    (int)LocationPrivacyStateEnum.Erased), token);
        });
        int completedBodies = 0;
        var owner = new ProjectionRoomAfterBodyOwner((ISerializableAttemptOwner)new EfCoreUnitOfWork(db), _ =>
        {
            completedBodies++;
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<AuthorizationException>(() => InvokeProjectionRoomOwner(services, command,
            TestContext.Current!.Execution.CancellationToken, owner));
        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(completedBodies).IsEqualTo(0);
        await Assert.That(db.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Local.Native.CreateDatabase();
        observer.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
        await AssertProjectionRoomEffects(observer, 6, default, room, false,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(await observer.Locations.AsNoTracking().Where(row => row.Id == erasedId)
            .Select(row => row.LocationPrivacyStateId).SingleAsync()).IsEqualTo((int)LocationPrivacyStateEnum.Erased);
    }
}
