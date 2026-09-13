using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class LocationRoomRelocationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task MoveAndRename_UsesFinalUniqueNameAndPreservesIdentityAndAudit()
    {
        var data = await SeedAsync();
        var userId = Guid.CreateVersion7();
        await using (var context = fixture.CreateDbContext())
        {
            context.CurrentUserService = new CurrentUser(userId);
            var repository = new LocationRoomRepository(context);
            var room = await context.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
            var target = await context.Locations.SingleAsync(item => item.Id == data.TargetId);
            await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async cancellationToken =>
            {
                await repository.MoveToLocationAsync(room, target, "Renamed room", data.Stamp, cancellationToken);
                room.Capacity = null;
                await repository.Update(room);
            });
        }
        await using var verification = fixture.CreateDbContext();
        var moved = await verification.LocationRooms.AsNoTracking().SingleAsync(item => item.Id == data.RoomId);
        await Assert.That(moved.LocationId).IsEqualTo(data.TargetId);
        await Assert.That(moved.TenantId).IsEqualTo(data.TenantId);
        await Assert.That(moved.Name).IsEqualTo("Renamed room");
        await Assert.That(moved.Capacity).IsNull();
        await Assert.That(moved.ConcurrencyStamp).IsNotEqualTo(data.Stamp);
        await Assert.That(moved.UpdatedBy).IsEqualTo((Guid?)userId);
        await Assert.That(moved.UpdatedAt).IsNotNull();
        await Assert.That(await verification.LocationRooms.CountAsync(item => item.LocationId == data.TargetId))
            .IsEqualTo(2);
    }

    [Test]
    public async Task SaveFailure_RollsBackKeyNameAndRemainingFieldChanges()
    {
        var data = await SeedAsync();
        await using (var context = fixture.CreateDbContext(new FailSave()))
        {
            var repository = new LocationRoomRepository(context);
            var room = await context.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
            var target = await context.Locations.SingleAsync(item => item.Id == data.TargetId);
            await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async token =>
            {
                await repository.MoveToLocationAsync(room, target, "Renamed room", data.Stamp, token);
                room.Capacity = null;
                await repository.Update(room);
            })).Throws<InjectedSaveException>();
        }
        await using var verification = fixture.CreateDbContext();
        var retained = await verification.LocationRooms.AsNoTracking().SingleAsync(item => item.Id == data.RoomId);
        await Assert.That(retained.LocationId).IsEqualTo(data.SourceId);
        await Assert.That(retained.Name).IsEqualTo("Shared name");
        await Assert.That(retained.Capacity).IsEqualTo((int?)20);
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(data.Stamp);
    }

    [Test]
    public async Task ConcurrentEditAfterRead_PreventsRelocationFromOverwritingNewState()
    {
        var data = await SeedAsync();
        await using var staleContext = fixture.CreateDbContext();
        var staleRoom = await staleContext.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
        var target = await staleContext.Locations.SingleAsync(item => item.Id == data.TargetId);
        Guid freshStamp;
        await using (var editor = fixture.CreateDbContext())
        {
            var current = await editor.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
            current.Description = "Concurrent edit";
            await editor.SaveChangesAsync();
            freshStamp = current.ConcurrencyStamp;
        }
        await Assert.That(async () => await new EfCoreUnitOfWork(staleContext).ExecuteInTransactionAsync(
            token => new LocationRoomRepository(staleContext)
                .MoveToLocationAsync(staleRoom, target, "Renamed room", data.Stamp, token)))
            .Throws<ConcurrencyConflictException>();
        await using var verification = fixture.CreateDbContext();
        var retained = await verification.LocationRooms.AsNoTracking().SingleAsync(item => item.Id == data.RoomId);
        await Assert.That(retained.LocationId).IsEqualTo(data.SourceId);
        await Assert.That(retained.Description).IsEqualTo("Concurrent edit");
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(freshStamp);
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task PersistedScheduleReferences_RemainVisibleAfterSoftDeletion(int kind, bool deleted)
    {
        var data = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, program, placement) = await SeedScheduleAsync(context, data);
        switch (kind)
        {
            case 0:
                var session = new EventSession
                {
                    Id = Guid.CreateVersion7(), EventId = program.Id, Event = program,
                    TenantId = tenant.Id, Tenant = tenant, Title = "Room session", IsDeleted = deleted
                };
                session.AssignEventLocation(placement);
                session.RoomId = data.RoomId;
                context.EventSessions.Add(session);
                break;
            case 1:
                var group = new EventSessionGroup
                {
                    Id = Guid.CreateVersion7(), EventId = program.Id, Event = program,
                    TenantId = tenant.Id, Tenant = tenant, Name = "Room group", IsDeleted = deleted
                };
                group.AssignEventLocation(placement);
                group.RoomId = data.RoomId;
                context.EventSessionGroups.Add(group);
                break;
            case 2:
                var item = new EventAgendaItem
                {
                    Id = Guid.CreateVersion7(), EventId = program.Id, Event = program,
                    TenantId = tenant.Id, Tenant = tenant, Title = "Room agenda", IsDeleted = deleted
                };
                item.Reschedule(UtcInstantRange.Create(
                    new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero)),
                    "UTC", new EventScheduleProjectionCalculator());
                item.AssignEventLocation(placement);
                item.RoomId = data.RoomId;
                context.EventAgendaItems.Add(item);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        await context.SaveChangesAsync();

        await Assert.That(await new LocationRoomRepository(context).HasScheduleReferencesAsync(
            data.RoomId, default)).IsTrue();
    }

    [Test]
    public async Task DestinationNameCollision_ReturnsValidationWithoutPartiallyMovingRoom()
    {
        var data = await SeedAsync();
        await using (var context = fixture.CreateDbContext())
        {
            var room = await context.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
            var target = await context.Locations.SingleAsync(item => item.Id == data.TargetId);
            await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(
                token => new LocationRoomRepository(context)
                    .MoveToLocationAsync(room, target, "Shared name", data.Stamp, token)))
                .Throws<BadRequestException>();
        }
        await using var verification = fixture.CreateDbContext();
        var retained = await verification.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
        await Assert.That(retained.LocationId).IsEqualTo(data.SourceId);
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(data.Stamp);
    }

    [Test]
    public async Task SoftDeletedRoom_CannotBeRelocatedFromAnOlderSnapshot()
    {
        var data = await SeedAsync();
        await using var moving = fixture.CreateDbContext();
        var room = await moving.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
        var target = await moving.Locations.SingleAsync(item => item.Id == data.TargetId);
        await using (var deleting = fixture.CreateDbContext())
        {
            var current = await deleting.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
            await new LocationRoomRepository(deleting).Delete(current);
        }
        await Assert.That(async () => await new EfCoreUnitOfWork(moving).ExecuteInTransactionAsync(
            token => new LocationRoomRepository(moving)
                .MoveToLocationAsync(room, target, "Moved room", data.Stamp, token)))
            .Throws<ConcurrencyConflictException>();
        await using var verification = fixture.CreateDbContext();
        var retained = await verification.LocationRooms.IgnoreQueryFilters(["SoftDelete"])
            .SingleAsync(item => item.Id == data.RoomId);
        await Assert.That(retained.IsDeleted).IsTrue();
        await Assert.That(retained.LocationId).IsEqualTo(data.SourceId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnusableDestination_CannotEscapeTenantOrPartiallyMoveRoom(bool foreignTenant)
    {
        var data = await SeedAsync();
        Location target;
        await using (var setup = fixture.CreateDbContext())
        {
            var tenant = foreignTenant
                ? new Tenant
                {
                    FullName = "Foreign destination", Slug = $"foreign-room-{Guid.CreateVersion7():N}",
                    TenantStatusId = 2, TenantStatus = null!
                }
                : await setup.Tenants.SingleAsync(item => item.Id == data.TenantId);
            target = new Location
            {
                FullName = "Unusable destination", Country = "BE", City = "Brussels", Tenant = tenant
            };
            setup.Locations.Add(target);
            await setup.SaveChangesAsync();
            if (!foreignTenant)
            {
                setup.Locations.Remove(target);
                await setup.SaveChangesAsync();
            }
        }
        await using (var moving = fixture.CreateDbContext())
        {
            var room = await moving.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
            await Assert.That(async () => await new EfCoreUnitOfWork(moving).ExecuteInTransactionAsync(
                token => new LocationRoomRepository(moving)
                    .MoveToLocationAsync(room, target, "Moved room", data.Stamp, token)))
                .Throws<ConcurrencyConflictException>();
        }
        await using var verification = fixture.CreateDbContext();
        var retained = await verification.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
        await Assert.That(retained.LocationId).IsEqualTo(data.SourceId);
        await Assert.That(retained.TenantId).IsEqualTo(data.TenantId);
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(data.Stamp);
    }

    [Test]
    public async Task ConcurrentScheduleAttachment_CannotLeaveAnInvalidRoomLocationReference()
    {
        var data = await SeedAsync();
        await using var attaching = fixture.CreateDbContext();
        var (tenant, program, placement) = await SeedScheduleAsync(attaching, data);
        await attaching.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var attachmentTransaction = await attaching.Database.BeginTransactionAsync();
            var group = new EventSessionGroup
            {
                Id = Guid.CreateVersion7(), EventId = program.Id, Event = program,
                TenantId = tenant.Id, Tenant = tenant, Name = "Concurrent room reference"
            };
            group.AssignEventLocation(placement);
            group.RoomId = data.RoomId;
            attaching.EventSessionGroups.Add(group);
            await attaching.SaveChangesAsync();

            var observer = new MoveCommandObserver();
            await using var moving = fixture.CreateDbContext(observer);
            observer.TableName = moving.Model.FindEntityType(typeof(LocationRoom))?.GetTableName()
                ?? throw new InvalidOperationException("Expected room table metadata.");
            var room = await moving.LocationRooms.SingleAsync(item => item.Id == data.RoomId);
            var target = await moving.Locations.SingleAsync(item => item.Id == data.TargetId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var moveTask = new EfCoreUnitOfWork(moving).ExecuteInTransactionAsync(async token =>
            {
                var repository = new LocationRoomRepository(moving);
                await Assert.That(await repository.HasScheduleReferencesAsync(data.RoomId, token)).IsFalse();
                await repository.MoveToLocationAsync(room, target, "Moved room", data.Stamp, token);
                await repository.Update(room);
            }, timeout.Token);
            await observer.Started.Task.WaitAsync(timeout.Token);
            await attachmentTransaction.CommitAsync(timeout.Token);
            await Assert.That(async () => await moveTask).Throws<ConcurrencyConflictException>();

            await using var verification = fixture.CreateDbContext();
            await Assert.That((await verification.LocationRooms.SingleAsync(item => item.Id == data.RoomId)).LocationId)
                .IsEqualTo(data.SourceId);
            var persistedReference = await verification.EventSessionGroups.SingleAsync(item => item.Id == group.Id);
            await Assert.That(persistedReference.RoomId).IsEqualTo((Guid?)data.RoomId);
            await Assert.That(persistedReference.LocationId).IsEqualTo((Guid?)data.SourceId);
        });
    }

    private static async Task<(Tenant Tenant, Explore.Domain.Event Program, EventLocation Placement)> SeedScheduleAsync(
        ExploreDbContext context, SeedData data)
    {
        var tenant = await context.Tenants.SingleAsync(item => item.Id == data.TenantId);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new() { Email = "room-reference@example.test", FirstName = "Room", LastName = "Reference" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = user.Id, User = user, Pii = new() { DisplayName = "Room reference" }
        };
        var program = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(), Title = "Room references", ActorId = actor.Id, Actor = actor,
            TenantId = tenant.Id, Tenant = tenant, EventStatus = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = 1, VisibilityType = null!, EventFormatId = 1, EventFormat = null!
        };
        context.Events.Add(program);
        await context.SaveChangesAsync();
        var placement = EventLocation.CreatePhysical(data.TenantId, program.Id, data.SourceId, user.Id,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        context.EventLocations.Add(placement);
        await context.SaveChangesAsync();
        return (tenant, program, placement);
    }

    private async Task<SeedData> SeedAsync()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = "Room relocation", Slug = $"room-relocation-{Guid.CreateVersion7():N}",
            TenantStatusId = 2, TenantStatus = null!
        };
        var source = new Location
        {
            FullName = "Source", Country = "BE", City = "Brussels", Tenant = tenant
        };
        var target = new Location
        {
            FullName = "Target", Country = "BE", City = "Brussels", Tenant = tenant
        };
        context.Locations.AddRange(source, target);
        await context.SaveChangesAsync();
        var room = new LocationRoom
        {
            Name = "Shared name", LocationId = source.Id, Location = source,
            TenantId = tenant.Id, Tenant = tenant, Capacity = 20
        };
        var occupiedName = new LocationRoom
        {
            Name = "Shared name", LocationId = target.Id, Location = target,
            TenantId = tenant.Id, Tenant = tenant
        };
        context.LocationRooms.AddRange(room, occupiedName);
        await context.SaveChangesAsync();
        return new SeedData(tenant.Id, source.Id, target.Id, room.Id, room.ConcurrencyStamp);
    }

    private sealed record SeedData(Guid TenantId, Guid SourceId, Guid TargetId, Guid RoomId, Guid Stamp);

    private sealed record CurrentUser(Guid? UserId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
    }

    private sealed class InjectedSaveException : Exception;

    private sealed class MoveCommandObserver : DbCommandInterceptor
    {
        public string TableName { get; set; } = string.Empty;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(TableName, StringComparison.Ordinal))
            {
                Started.TrySetResult();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InjectedSaveException();
    }
}
