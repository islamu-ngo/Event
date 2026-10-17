using Explore.Application.Contracts.Operations;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task ActiveSchedule_BlocksReparentingButAllowsSameLocationUpdates()
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync();
        using var request = fixture.CreateScope();
        await fixture.AuthenticateAsync(request);
        var setup = request.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(setup);
        string originalName = room.Name;
        await GrantFooterAdministrator(setup, fixture.Data.OwnerId, 2);
        Guid sourceId = room.LocationId;
        Guid initialStamp = room.ConcurrencyStamp;
        Guid tenantId = PlatformDefaults.DefaultTenantId;
        var placement = EventLocation.CreatePhysical(
            tenantId, fixture.Data.PublicId, sourceId, fixture.Data.OwnerId,
            fixture.Local.Clock.GetUtcNow().UtcDateTime);
        await new EventLocationRepository(setup).AddAsync(placement, default);
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = fixture.Data.PublicId,
            Event = null!,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Active room schedule",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)),
            "UTC", new EventScheduleProjectionCalculator());
        session.AssignEventLocation(placement);
        session.RoomId = room.Id;
        setup.EventSessions.Add(session);
        await setup.SaveChangesAsync();
        setup.ChangeTracker.Clear();

        // Select once from the actual authenticated request. All three attempts retain
        // this physical tenant owner, its credential lifetime and production finalizers.
        await using var selected = await request.ServiceProvider
            .GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new PolicyMutationScope(tenantId), default);
        var services = selected.Services;
        var owner = services.GetRequiredService<ExploreDbContext>();
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.RequireOriginalOwner(owner);
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        await Assert.That(lease.HumanScope!.Subject!.PrincipalId).IsEqualTo(fixture.Data.OwnerId);
        await Assert.That(lease.HumanLifetime).IsNotNull();
        await Assert.That(owner.BindTenantLease()).IsEqualTo((Guid?)tenantId);
        await Assert.That(owner.IsTenantFilterBypassed).IsFalse();
        var update = services.GetRequiredService<
            ICommandHandler<UpdateLocationRoomCommand, BaseCommandResponse<Guid>>>();
        var move = new UpdateLocationRoomCommand
        {
            LocationRoomId = room.Id,
            ExpectedConcurrencyStamp = initialStamp,
            UpdateLocationRoomDto = new()
            {
                Location = new() { LocationId = target.Id },
                Name = new() { Value = "Moved room" }
            }
        };

        var denied = await update.ExecuteAsync(move, default);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Errors).IsNotEmpty();
        var unchanged = await ReadRoom();
        await Assert.That(unchanged.LocationId).IsEqualTo(sourceId);
        await Assert.That(unchanged.Name).IsEqualTo(originalName);
        await Assert.That(unchanged.ConcurrencyStamp).IsEqualTo(initialStamp);
        await Assert.That(unchanged.TenantId).IsEqualTo(tenantId);
        await AssertPlacement(room.Id);

        var renamed = await update.ExecuteAsync(move with
        {
            UpdateLocationRoomDto = new()
            {
                Location = new() { LocationId = sourceId },
                Name = new() { Value = "Renamed room" }
            }
        }, default);
        await Assert.That(renamed.IsSuccess).IsTrue();
        var sameLocation = await ReadRoom();
        await Assert.That(sameLocation.LocationId).IsEqualTo(sourceId);
        await Assert.That(sameLocation.Name).IsEqualTo("Renamed room");
        await Assert.That(sameLocation.ConcurrencyStamp).IsNotEqualTo(initialStamp);
        await Assert.That(sameLocation.TenantId).IsEqualTo(tenantId);
        await AssertPlacement(room.Id);

        // Remove the real schedule reference, not a repository predicate. Keep its
        // event placement intact so the later move cannot silently reparent it.
        using (var unscheduleScope = fixture.CreateScope())
        {
            await fixture.AuthenticateAsync(unscheduleScope);
            var unschedule = unscheduleScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var scheduled = await unschedule.EventSessions
                .SingleAsync(row => row.Id == session.Id && row.TenantId == tenantId);
            scheduled.RoomId = null;
            await unschedule.SaveChangesAsync();
        }
        await AssertPlacement(null);
        using var movedRequest = fixture.CreateScope();
        await fixture.AuthenticateAsync(movedRequest);
        var moved = await movedRequest.ServiceProvider
            .GetRequiredService<ICommandHandler<UpdateLocationRoomCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(
            move with { ExpectedConcurrencyStamp = sameLocation.ConcurrencyStamp }, default);
        await Assert.That(moved.IsSuccess).IsTrue();
        var relocated = await ReadRoom();
        await Assert.That(relocated.LocationId).IsEqualTo(target.Id);
        await Assert.That(relocated.Name).IsEqualTo("Moved room");
        await Assert.That(relocated.ConcurrencyStamp).IsNotEqualTo(sameLocation.ConcurrencyStamp);
        await Assert.That(relocated.TenantId).IsEqualTo(tenantId);
        await AssertPlacement(null);
        await Assert.That(services.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(owner);
        await Assert.That(owner.Database.CurrentTransaction).IsNull();

        async Task<LocationRoom> ReadRoom()
        {
            await using var observer = fixture.Local.Native.CreateDatabase();
            return await observer.LocationRooms.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
                .SingleAsync(row => row.Id == room.Id && row.TenantId == tenantId);
        }

        async Task AssertPlacement(Guid? expectedRoomId)
        {
            await using var observer = fixture.Local.Native.CreateDatabase();
            var scheduled = await observer.EventSessions.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
                .SingleAsync(row => row.Id == session.Id && row.TenantId == tenantId);
            await Assert.That(scheduled.RoomId).IsEqualTo(expectedRoomId);
            await Assert.That(scheduled.LocationId).IsEqualTo((Guid?)sourceId);
            await Assert.That(scheduled.EventLocationId).IsEqualTo((Guid?)placement.Id);
            await Assert.That(scheduled.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Published);
            await Assert.That(scheduled.IsDeleted).IsFalse();
            await Assert.That((await observer.EventLocations.IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking()
                .SingleAsync(row => row.Id == placement.Id && row.TenantId == tenantId)).LocationId).IsEqualTo((Guid?)sourceId);
        }
    }
}
