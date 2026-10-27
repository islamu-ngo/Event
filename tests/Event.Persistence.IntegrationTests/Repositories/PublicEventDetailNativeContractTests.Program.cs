using System.Data;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventPrograms.Handlers.Queries;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PublicEventDetailNativeContractTests
{
    [Test]
    public async Task RuntimeRepositoriesAndPublicHandlerReadOnlyPublishedProgramWithoutPrivatePrivileges()
    {
        var seeded = await SeedAsync();
        var program = await SeedProgramAsync(seeded.Tenant, seeded.Event);
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        using var services = ProgramReaderServices(context);
        var bindingFactory = new PublicEventDiscoveryReadBindingFactory(services);
        var events = new EventRepository(context, publicDiscoveryBindings: bindingFactory,
            nativeEnrollment: fixture.NativeQueueEnrollment);
        var sessions = new EventSessionRepository(context, new HttpContextAccessor(), bindingFactory,
            fixture.NativeQueueEnrollment);
        IReadOnlyCollection<EventLocationDisclosureRequest>? disclosureRequests = null;
        var disclosure = Substitute.For<IEventLocationDisclosureService>();
        disclosure.ResolveManyAsync(Arg.Any<IReadOnlyCollection<EventLocationDisclosureRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var requests = call.Arg<IReadOnlyCollection<EventLocationDisclosureRequest>>()
                    ?? throw new InvalidOperationException("Public disclosure requests are required.");
                disclosureRequests = requests;
                IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results = requests
                    .DistinctBy(request => request.EventLocationId).ToDictionary(request => request.EventLocationId,
                        request => EventLocationDisclosureResult.Suppressed(request.EventLocationId,
                            EventLocationDisclosurePurpose.Public, EventLocationDisclosureState.ToBeAnnounced));
                return Task.FromResult(results);
            });
        var handler = ProgramHandler(context, events, sessions, disclosure);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var detail = (await events.GetPublicEventWithDetailsByIdAsync(seeded.Event, default))!;
            await Assert.That(detail.Sessions.Single().Id).IsEqualTo(program.Session);
            await Assert.That(detail.SessionGroups.Single().Id).IsEqualTo(program.Group);
            await Assert.That(detail.AgendaItems.Single().Id).IsEqualTo(program.Agenda);
            var session = (await sessions.GetPublicSessionsByEventAsync(seeded.Event, default)).Single();
            await Assert.That(session.Id).IsEqualTo(program.Session);
            await Assert.That(session.Event.Id).IsEqualTo(seeded.Event);
            await Assert.That(session.SessionGroups.Single().EventSessionGroup.Id).IsEqualTo(program.Group);
            await Assert.That(session.EventLocationId).IsEqualTo(program.Placement);
            await Assert.That(session.Location).IsNull();
            await Assert.That(session.Room).IsNull();
            var summary = (await handler.QueryAsync(new GetEventProgramSummaryRequest(seeded.Event), default))!;
            var section = summary.Sections.Single().SessionGroups.Single();
            await Assert.That(section.Title).IsEqualTo("Public track");
            await Assert.That(section.Days.Single().Items.Single().SessionId).IsEqualTo(program.Session);
            await Assert.That(section.LocationName).IsNull();
            await Assert.That(section.RoomName).IsNull();
            await Assert.That(section.EventLocation!.EventLocationId).IsEqualTo(program.Placement);
            await Assert.That(section.EventLocation.State).IsEqualTo(EventLocationDisclosureState.ToBeAnnounced);
            await Assert.That(disclosureRequests!.All(request => request.TenantId == seeded.Tenant
                && request.EventId == seeded.Event && request.EventLocationId == program.Placement
                && request.RequesterUserId is null && request.Purpose == EventLocationDisclosurePurpose.Public)).IsTrue();
            await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }
        foreach (var type in new[] { typeof(Explore.Domain.Event), typeof(EventSession),
                     typeof(EventSessionGroup), typeof(EventSessionGroupSession), typeof(EventAgendaItem) })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
                $"SELECT * FROM {new HumanPrincipalSourceSql(context).Table(type)} LIMIT 1"));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    [Arguments("private")]
    [Arguments("deleted")]
    [Arguments("membership")]
    public async Task RuntimeProgramAndSessionsStopAtCurrentPublicParentAuthority(string withdrawal)
    {
        var seeded = await SeedAsync();
        await SeedProgramAsync(seeded.Tenant, seeded.Event);
        await using (var first = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
        {
            await Assert.That((await ReadAsync(first, seeded.Event))!.Sessions.Count).IsEqualTo(1);
        }
        await using (var writer = fixture.CreateDbContext())
        {
            var entity = await writer.Events.SingleAsync(row => row.Id == seeded.Event);
            switch (withdrawal)
            {
                case "private": entity.VisibilityTypeId = (int)VisibilityTypeEnum.Private; break;
                case "deleted": entity.IsDeleted = true; break;
                case "membership":
                    (await writer.TenantUsers.SingleAsync(row => row.TenantId == seeded.Tenant)).IsDeleted = true;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(withdrawal));
            }
            await writer.SaveChangesAsync();
        }
        await using var current = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        using var services = ProgramReaderServices(current);
        var factory = new PublicEventDiscoveryReadBindingFactory(services);
        var events = new EventRepository(current, publicDiscoveryBindings: factory,
            nativeEnrollment: fixture.NativeQueueEnrollment);
        var sessions = new EventSessionRepository(current, new HttpContextAccessor(), factory, fixture.NativeQueueEnrollment);
        await Assert.That(await ProgramHandler(current, events, sessions)
            .QueryAsync(new GetEventProgramSummaryRequest(seeded.Event), default)).IsNull();
        await Assert.That((await sessions.GetPublicSessionsByEventAsync(seeded.Event, default)).Count).IsEqualTo(0);
        await Assert.That((await sessions.GetPublicSessionsByEventAsync(seeded.Hidden, default)).Count).IsEqualTo(0);
    }

    private static GetEventProgramSummaryRequestHandler ProgramHandler(
        ExploreDbContext context, EventRepository events, EventSessionRepository sessions,
        IEventLocationDisclosureService? disclosure = null) =>
        new(events, sessions, new EventSessionGroupRepository(context),
            new EventAgendaItemRepository(context, new HttpContextAccessor()),
            disclosure ?? Substitute.For<IEventLocationDisclosureService>());

    private ServiceProvider ProgramReaderServices(ExploreDbContext context) => new ServiceCollection()
        .AddSingleton(context)
        .AddSingleton(new ExploreDbContextExecutionLease())
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Migrator:Username"] = fixture.NativeQueueEnrollment.MigratorLogin,
            ["Database:Global:Username"] = fixture.NativeQueueEnrollment.GlobalLogin,
            ["Database:Runtime:Username"] = fixture.NativeQueueEnrollment.RuntimeLogin,
            ["Database:EmailPolicyInstanceMutation:Username"] = fixture.NativeQueueEnrollment.EmailInstanceMutationLogin,
            ["Database:EmailPolicyTenantMutation:Username"] = fixture.NativeQueueEnrollment.EmailTenantMutationLogin
        }).Build()).BuildServiceProvider();

    private async Task<(Guid Session, Guid Group, Guid Agenda, Guid Placement)> SeedProgramAsync(Guid tenantId, Guid eventId)
    {
        await using var writer = fixture.CreateDbContext();
        var parent = await writer.Events.SingleAsync(row => row.Id == eventId);
        var now = new DateTimeOffset(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var calculator = new EventScheduleProjectionCalculator();
        var hiddenDay = new EventDay
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            EventId = eventId,
            Event = parent,
            LocalDate = new DateOnly(2028, 6, 15),
            IsPublished = false
        };
        writer.Add(hiddenDay);
        var visible = AddSession(EventSessionStatusEnum.Published);
        AddSession(EventSessionStatusEnum.Draft);
        AddSession(EventSessionStatusEnum.Published).IsDeleted = true;
        AddSession(EventSessionStatusEnum.Published).EventDayId = hiddenDay.Id;
        var open = AddSession(EventSessionStatusEnum.Published);
        open.EndTime = null;
        open.EndTimeType = SessionEndTimeType.OpenEnded;
        open.ReprojectLocalTimes("UTC", calculator);
        var group = AddGroup(true);
        Guid userId = await writer.TenantUsers.Where(row => row.TenantId == tenantId).Select(row => row.UserId).SingleAsync();
        var placement = EventLocation.CreateToBeAnnounced(tenantId, eventId, userId, now.UtcDateTime);
        writer.Add(placement);
        visible.AssignEventLocation(placement);
        group.AssignEventLocation(placement);
        var hiddenGroup = AddGroup(false);
        AddGroup(true).IsDeleted = true;
        foreach (var assigned in new[] { group, hiddenGroup })
            writer.Add(new EventSessionGroupSession
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                EventId = eventId,
                Event = parent,
                EventSessionId = visible.Id,
                EventSession = visible,
                EventSessionGroupId = assigned.Id,
                EventSessionGroup = assigned,
                IsPrimary = assigned == group
            });
        var agenda = AddAgenda();
        AddAgenda().EventDayId = hiddenDay.Id;
        AddAgenda().IsDeleted = true;
        await writer.SaveChangesAsync();
        return (visible.Id, group.Id, agenda.Id, placement.Id);

        EventSession AddSession(EventSessionStatusEnum status)
        {
            var entity = new EventSession(status)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                EventId = eventId,
                Event = parent,
                Title = "Public program session",
                StartTime = now,
                EndTime = now.AddHours(1),
                EndTimeType = SessionEndTimeType.Fixed
            };
            entity.ReprojectLocalTimes("UTC", calculator);
            writer.Add(entity);
            return entity;
        }
        EventSessionGroup AddGroup(bool published)
        {
            var entity = new EventSessionGroup
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                EventId = eventId,
                Event = parent,
                Name = "Public track",
                IsPublished = published
            };
            writer.Add(entity);
            return entity;
        }
        EventAgendaItem AddAgenda()
        {
            var entity = new EventAgendaItem
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                EventId = eventId,
                Event = parent,
                Title = "Public agenda",
                StartTime = now,
                EndTime = now.AddMinutes(5)
            };
            entity.ReprojectLocalTimes("UTC", calculator);
            writer.Add(entity);
            return entity;
        }
    }
}
