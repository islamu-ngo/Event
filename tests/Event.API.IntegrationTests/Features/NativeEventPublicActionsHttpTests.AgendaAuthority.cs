using Cerbos.Sdk.Builder;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventAgendaItems.Handlers.Commands;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Domain;
using Explore.Persistence;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task NativeAgendaCreationPreservesSchedulingTenantPlacementAndInputMapping()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareAgendaParent(db, data.PublicId);
        Guid tenantId = db.TenantContext!.TenantId;
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            FullName = "Agenda venue",
            City = "Brussels",
            Country = "BE"
        };
        var room = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            LocationId = location.Id,
            Location = location,
            Name = "Agenda room"
        };
        var day = new EventDay
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            EventId = data.PublicId,
            Event = null!,
            LocalDate = new(2026, 7, 21)
        };
        db.AddRange(location, room, day);
        await db.SaveChangesAsync();
        var input = AgendaInput(data.PublicId) with
        {
            LocationId = location.Id,
            RoomId = room.Id,
            KindId = 7,
            SortOrder = 4,
            Description = ""
        };
        var handler = ActivatorUtilities.CreateInstance<CreateEventAgendaItemCommandHandler>(scope.ServiceProvider);

        var response = await handler.ExecuteAsync(new() { EventAgendaItemDto = input }, default);

        await Assert.That(response.IsSuccess).IsTrue();
        var item = await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == response.Id);
        var placement = await db.EventLocations.AsNoTracking().SingleAsync(row => row.Id == item.EventLocationId);
        await Assert.That(item.TenantId).IsEqualTo(tenantId);
        await Assert.That(item.EventId).IsEqualTo(data.PublicId);
        await Assert.That(placement.EventId).IsEqualTo(data.PublicId);
        await Assert.That(placement.LocationId).IsEqualTo(location.Id);
        await Assert.That(item.RoomId).IsEqualTo(room.Id);
        await Assert.That(item.EventDayId).IsEqualTo(day.Id);
        await Assert.That(item.LocalStartTime).IsEqualTo(new TimeOnly(1, 30));
        await Assert.That(item.LocalEndTime).IsEqualTo(new TimeOnly(2, 30));
        await Assert.That(item.KindId).IsEqualTo(7);
        await Assert.That(item.SortOrder).IsEqualTo(4);
        await Assert.That(item.Description).IsEqualTo("");
        await Assert.That(item.CreatedAt).IsNotEqualTo(default(DateTime));
        await Assert.That(item.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeAgendaOwnerOrInstanceAdministratorCanCreateUpdateAndDelete(bool instanceAdministrator)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, instanceAdministrator ? data.OutsiderId : data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareAgendaParent(db, data.PublicId);
        if (instanceAdministrator)
            await GrantTemplateAdministrator(db, data.OutsiderId);
        var create = ActivatorUtilities.CreateInstance<CreateEventAgendaItemCommandHandler>(scope.ServiceProvider);
        var update = ActivatorUtilities.CreateInstance<UpdateEventAgendaItemCommandHandler>(scope.ServiceProvider);
        var delete = ActivatorUtilities.CreateInstance<DeleteEventAgendaItemCommandHandler>(scope.ServiceProvider);

        var created = await create.ExecuteAsync(new() { EventAgendaItemDto = AgendaInput(data.PublicId) }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var item = await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        db.ChangeTracker.Clear();
        var changed = await update.ExecuteAsync(new()
        {
            EventAgendaItemId = item.Id,
            ExpectedConcurrencyStamp = item.ConcurrencyStamp,
            EventAgendaItemDto = new() { Title = new() { Value = "Revised agenda" } }
        }, default);
        await Assert.That(changed.IsSuccess).IsTrue();
        await Assert.That((await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == item.Id)).Title)
            .IsEqualTo("Revised agenda");
        db.ChangeTracker.Clear();
        var deleted = await delete.ExecuteAsync(new() { Id = item.Id }, default);
        await Assert.That(deleted.IsSuccess).IsTrue();
        await Assert.That(await db.EventAgendaItems.AnyAsync(row => row.Id == item.Id)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task DirectAgendaHandlersRejectSameTenantNonowners(int operation)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        Guid id;
        Guid stamp;
        using (var owner = Scope(factory, data.OwnerId))
        {
            var db = owner.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await PrepareAgendaParent(db, data.PublicId);
            var created = await ActivatorUtilities.CreateInstance<CreateEventAgendaItemCommandHandler>(owner.ServiceProvider)
                .ExecuteAsync(new() { EventAgendaItemDto = AgendaInput(data.PublicId) }, default);
            await Assert.That(created.IsSuccess).IsTrue();
            id = created.Id;
            stamp = (await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == id)).ConcurrencyStamp;
        }
        using var outsider = Scope(factory, data.OutsiderId);

        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            switch (operation)
            {
                case 0:
                    await ActivatorUtilities.CreateInstance<CreateEventAgendaItemCommandHandler>(outsider.ServiceProvider)
                        .ExecuteAsync(new() { EventAgendaItemDto = AgendaInput(data.PublicId) }, default);
                    break;
                case 1:
                    await ActivatorUtilities.CreateInstance<UpdateEventAgendaItemCommandHandler>(outsider.ServiceProvider)
                        .ExecuteAsync(new()
                        {
                            EventAgendaItemId = id,
                            ExpectedConcurrencyStamp = stamp,
                            EventAgendaItemDto = new() { Title = new() { Value = "Unauthorized" } }
                        }, default);
                    break;
                case 2:
                    await ActivatorUtilities.CreateInstance<DeleteEventAgendaItemCommandHandler>(outsider.ServiceProvider)
                        .ExecuteAsync(new() { Id = id }, default);
                    break;
            }
        });
        var current = await outsider.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(current.Title).IsEqualTo("Opening");
        await Assert.That(current.ConcurrencyStamp).IsEqualTo(stamp);
    }

    [Test]
    public async Task AgendaNativeAuthorityRejectsCommittedInstanceGrantRevocation()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventAgendaItemRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var before = await unit.ExecuteSerializableAsync(token => repository.GetAuthorizedParentForMutationAsync(
            db.TenantContext!.TenantId, data.OutsiderId, data.PublicId, "create", token));
        await Assert.That(before).IsNotNull();
        await db.PlatformUserRoles.Where(grant => grant.Id == grantId).ExecuteDeleteAsync();

        var after = await unit.ExecuteSerializableAsync(token => repository.GetAuthorizedParentForMutationAsync(
            db.TenantContext!.TenantId, data.OutsiderId, data.PublicId, "create", token));

        await Assert.That(after).IsNull();
    }

    [Test]
    public async Task TrackedAgendaParentSubstitutionCannotReachTheRawMove()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareAgendaParent(db, data.PublicId);
        var created = await ActivatorUtilities.CreateInstance<CreateEventAgendaItemCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventAgendaItemDto = AgendaInput(data.PublicId) }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var item = await db.EventAgendaItems.SingleAsync(row => row.Id == created.Id);
        var placement = await db.EventLocations.SingleAsync(row => row.Id == item.EventLocationId);
        item.EventId = data.OtherId;
        var repository = scope.ServiceProvider.GetRequiredService<IEventAgendaItemRepository>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
            {
                await repository.MoveToEventAsync(item, data.PublicId, placement, null, token);
                return true;
            }));

        var current = await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        await Assert.That(current.EventId).IsEqualTo(data.PublicId);
        await Assert.That(current.EventLocationId).IsEqualTo(placement.Id);
    }

    [Test]
    public async Task AgendaMoveRequiresBothCurrentParentsAndRetainsAuthorizedRelocation()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareAgendaParent(db, data.PublicId);
        await PrepareAgendaParent(db, data.PrivateId);
        var created = await ActivatorUtilities.CreateInstance<CreateEventAgendaItemCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventAgendaItemDto = AgendaInput(data.PublicId) }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var original = await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        db.ChangeTracker.Clear();
        var update = ActivatorUtilities.CreateInstance<UpdateEventAgendaItemCommandHandler>(scope.ServiceProvider);

        var moved = await update.ExecuteAsync(new()
        {
            EventAgendaItemId = original.Id,
            ExpectedConcurrencyStamp = original.ConcurrencyStamp,
            EventAgendaItemDto = new() { Event = new() { EventId = data.PrivateId } }
        }, default);
        await Assert.That(moved.IsSuccess).IsTrue();
        var current = await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == original.Id);
        await Assert.That(current.EventId).IsEqualTo(data.PrivateId);
        await Assert.That((await db.EventLocations.AsNoTracking().SingleAsync(row => row.Id == current.EventLocationId)).EventId)
            .IsEqualTo(data.PrivateId);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<AuthorizationException>(() => update.ExecuteAsync(new()
        {
            EventAgendaItemId = current.Id,
            ExpectedConcurrencyStamp = current.ConcurrencyStamp,
            EventAgendaItemDto = new() { Event = new() { EventId = data.OtherId } }
        }, default));
        var foreign = await update.ExecuteAsync(new()
        {
            EventAgendaItemId = current.Id,
            ExpectedConcurrencyStamp = current.ConcurrencyStamp,
            EventAgendaItemDto = new() { Event = new() { EventId = data.ForeignId } }
        }, default);
        await Assert.That(foreign.IsSuccess).IsFalse();
        var unchanged = await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == original.Id);
        await Assert.That(unchanged.EventId).IsEqualTo(data.PrivateId);
        await Assert.That(unchanged.ConcurrencyStamp).IsEqualTo(current.ConcurrencyStamp);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AgendaSelectedCerbosOutagePreventsEveryDirectMutation(int operation)
    {
        var transport = new UnavailableInstanceKeyPolicyTransport();
        var client = CerbosClientBuilder.ForTarget("http://agenda-policy.test").WithPlaintext()
            .WithGrpcChannelOptions(new GrpcChannelOptions { HttpHandler = transport, DisposeHttpClient = true }).Build();
        await using var factory = new NativeEventSeriesFactory { InstanceCerbosClient = client };
        factory.AdditionalConfiguration["Authorization:Provider"] = "cerbos";
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareAgendaParent(db, data.PublicId);
        var item = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = db.TenantContext!.TenantId,
            Tenant = null!,
            EventId = data.PublicId,
            Event = null!,
            Title = "Opening"
        };
        var input = AgendaInput(data.PublicId);
        item.Reschedule(Explore.Domain.ValueObjects.UtcInstantRange.Create(input.StartTime, input.EndTime),
            "Europe/Brussels", new Explore.Domain.Services.Scheduling.EventScheduleProjectionCalculator());
        db.EventAgendaItems.Add(item);
        await db.SaveChangesAsync();
        Guid stamp = item.ConcurrencyStamp;
        db.ChangeTracker.Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(async () =>
        {
            switch (operation)
            {
                case 0:
                    await ActivatorUtilities.CreateInstance<CreateEventAgendaItemCommandHandler>(scope.ServiceProvider)
                        .ExecuteAsync(new() { EventAgendaItemDto = input }, deadline.Token);
                    break;
                case 1:
                    await ActivatorUtilities.CreateInstance<UpdateEventAgendaItemCommandHandler>(scope.ServiceProvider)
                        .ExecuteAsync(new()
                        {
                            EventAgendaItemId = item.Id,
                            ExpectedConcurrencyStamp = stamp,
                            EventAgendaItemDto = new() { Title = new() { Value = "Unauthorized" } }
                        }, deadline.Token);
                    break;
                case 2:
                    await ActivatorUtilities.CreateInstance<DeleteEventAgendaItemCommandHandler>(scope.ServiceProvider)
                        .ExecuteAsync(new() { Id = item.Id }, deadline.Token);
                    break;
            }
        });

        await Assert.That(transport.Requests).IsGreaterThan(0);
        var current = await db.EventAgendaItems.AsNoTracking().SingleAsync(row => row.Id == item.Id);
        await Assert.That(current.Title).IsEqualTo("Opening");
        await Assert.That(current.ConcurrencyStamp).IsEqualTo(stamp);
    }

    private static CreateEventAgendaItemDto AgendaInput(Guid eventId) => new()
    {
        EventId = eventId,
        Title = "Opening",
        StartTime = new(2026, 7, 20, 23, 30, 0, TimeSpan.Zero),
        EndTime = new(2026, 7, 21, 0, 30, 0, TimeSpan.Zero)
    };

    private static async Task PrepareAgendaParent(ExploreDbContext db, Guid eventId)
    {
        var parent = await db.Events.SingleAsync(row => row.Id == eventId);
        parent.EventTimeZoneId = "Europe/Brussels";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
