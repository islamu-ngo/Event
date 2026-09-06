// ABOUTME: Real-PostgreSQL acceptance for bounded EventLocation registration coverage reads.
// ABOUTME: Proves exact scope mapping, tenant and soft-delete denial, no tracking, and one-query batching.

using System.Data.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<RegistrationCoveragePostgreSqlFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class EventLocationRegistrationAccessPersistenceTests(RegistrationCoveragePostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Category("EventLocationPrivacy")]
    public async Task ResolveManyAsync_RealPostgres_MapsExactMaterializedSessionCoverage()
    {
        var graph = await SeedGraphAsync();

        var eventAccess = await LoadAndResolveAsync(graph, graph.EventUserId);
        var dayAccess = await LoadAndResolveAsync(graph, graph.DayUserId);
        var selectedAccess = await LoadAndResolveAsync(graph, graph.SessionUserId);

        await Assert.That(eventAccess[graph.SharedLocationId].EffectiveState)
            .IsEqualTo(EventLocationRegistrationEffectiveState.Confirmed);
        await Assert.That(eventAccess[graph.LaterSameDayLocationId].EffectiveState)
            .IsEqualTo(EventLocationRegistrationEffectiveState.Denied);
        await Assert.That(eventAccess[graph.CrossDayLocationId].EffectiveState)
            .IsEqualTo(EventLocationRegistrationEffectiveState.Denied);
        await Assert.That(dayAccess[graph.SharedLocationId].CoversRequestedEventLocation).IsTrue();
        await Assert.That(dayAccess[graph.LaterSameDayLocationId].CoversRequestedEventLocation).IsFalse();
        await Assert.That(dayAccess[graph.CrossDayLocationId].CoversRequestedEventLocation).IsFalse();
        await Assert.That(selectedAccess[graph.SharedLocationId].CoversRequestedEventLocation).IsTrue();
        await Assert.That(selectedAccess[graph.LaterSameDayLocationId].CoversRequestedEventLocation).IsFalse();
        await Assert.That(selectedAccess[graph.CrossDayLocationId].CoversRequestedEventLocation).IsFalse();
    }

    [Test]
    [Category("EventLocationPrivacy")]
    public async Task ResolveManyAsync_RealPostgres_DeletedParentAndForeignTenantDeny()
    {
        var graph = await SeedGraphAsync();
        await using (var deleteContext = fixture.CreateDbContext())
        {
            var order = await deleteContext.RegistrationOrders
                .SingleAsync(item => item.Id == graph.DayOrderId);
            order.IsDeleted = true;
            await deleteContext.SaveChangesAsync();
        }

        var deletedAccess = await LoadAndResolveAsync(graph, graph.DayUserId);
        await Assert.That(deletedAccess.Values.All(access => !access.CoversRequestedEventLocation)).IsTrue();

        await using var foreignContext = CreateTenantContext(graph.ForeignTenantId);
        var foreignRows = await new EventRegistrationRepository(foreignContext)
            .GetLocationAccessCoverageAsync(
                graph.ForeignTenantId,
                graph.EventId,
                graph.EventUserId,
                CancellationToken.None);
        await Assert.That(foreignRows).IsEmpty();
    }

    [Test]
    [Category("EventLocationPrivacy")]
    public async Task GetLocationAccessCoverageAsync_RealPostgres_IsOneNoTrackingQueryWithoutPerPlacementIo()
    {
        var graph = await SeedGraphAsync();
        var counter = new CommandCountingInterceptor();
        await using var context = CreateTenantContext(graph.TenantId, counter);
        var repository = new EventRegistrationRepository(context);

        var rows = await repository.GetLocationAccessCoverageAsync(
            graph.TenantId,
            graph.EventId,
            graph.EventUserId,
            CancellationToken.None);
        var requestedIds = Enumerable.Range(0, 100)
            .SelectMany(_ => graph.RequestedLocationIds)
            .ToArray();
        var access = new EventLocationRegistrationAccessService().ResolveMany(
            graph.TenantId,
            graph.EventId,
            graph.EventUserId,
            Now,
            requestedIds,
            rows);

        await Assert.That(counter.ReaderCommandCount).IsEqualTo(1);
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
        await Assert.That(access.Count).IsEqualTo(3);
    }

    private async Task<IReadOnlyDictionary<Guid, EventLocationRegistrationAccess>>
        LoadAndResolveAsync(RegistrationGraph graph, Guid userId)
    {
        await using var context = CreateTenantContext(graph.TenantId);
        var rows = await new EventRegistrationRepository(context).GetLocationAccessCoverageAsync(
            graph.TenantId,
            graph.EventId,
            userId,
            CancellationToken.None);
        return new EventLocationRegistrationAccessService().ResolveMany(
            graph.TenantId,
            graph.EventId,
            userId,
            Now,
            graph.RequestedLocationIds,
            rows);
    }

    private ExploreDbContext CreateTenantContext(Guid tenantId, DbCommandInterceptor? interceptor = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new ExploreDbContext(options.Options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
    }

    private async Task<RegistrationGraph> SeedGraphAsync()
    {
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("coverage");
        var foreignTenant = CreateTenant("foreign");
        var eventUser = CreateUser("event");
        var dayUser = CreateUser("day");
        var sessionUser = CreateUser("session");
        context.AddRange(tenant, foreignTenant, eventUser, dayUser, sessionUser);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = eventUser.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Registration coverage actor" }
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null!,
            Title = "Registration coverage event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Private,
            VisibilityType = null!
        };
        var selectedDay = CreateDay(tenant.Id, @event.Id, new DateOnly(2026, 7, 19));
        var otherDay = CreateDay(tenant.Id, @event.Id, new DateOnly(2026, 7, 20));
        var locations = new[]
        {
            CreateLocation(tenant.Id, "Shared"),
            CreateLocation(tenant.Id, "Later same day"),
            CreateLocation(tenant.Id, "Cross day")
        };
        context.AddRange(@event, selectedDay, otherDay);
        context.AddRange(locations);
        await context.SaveChangesAsync();

        var placements = locations
            .Select(location => EventLocation.CreatePhysical(
                tenant.Id,
                @event.Id,
                location.Id,
                eventUser.Id,
                Now.UtcDateTime))
            .ToArray();
        context.EventLocations.AddRange(placements);
        await context.SaveChangesAsync();

        var registered = CreateSession(tenant.Id, @event, selectedDay.Id, placements[0], "Registered");
        var laterSameDay = CreateSession(tenant.Id, @event, selectedDay.Id, placements[1], "Later same day");
        var crossDay = CreateSession(tenant.Id, @event, otherDay.Id, placements[2], "Cross day");
        var selectedOverlap = CreateSession(tenant.Id, @event, otherDay.Id, placements[0], "Selected overlap");
        context.EventSessions.AddRange(registered, laterSameDay, crossDay, selectedOverlap);
        await context.SaveChangesAsync();

        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(
            tenant.Id,
            @event.Id,
            "USD",
            versionNumber: 1);
        var eventOrder = CreateOrder(
            tenant.Id,
            @event.Id,
            eventUser.Id,
            catalog.Id,
            RegistrationOrderStatusEnum.AwaitingApproval);
        var selectedOrder = CreateOrder(
            tenant.Id,
            @event.Id,
            eventUser.Id,
            catalog.Id,
            RegistrationOrderStatusEnum.Confirmed);
        var dayOrder = CreateOrder(
            tenant.Id,
            @event.Id,
            dayUser.Id,
            catalog.Id,
            RegistrationOrderStatusEnum.Confirmed);
        var sessionOrder = CreateOrder(
            tenant.Id,
            @event.Id,
            sessionUser.Id,
            catalog.Id,
            RegistrationOrderStatusEnum.Confirmed);
        context.EventTicketCatalogVersions.Add(catalog);
        context.RegistrationOrders.AddRange(eventOrder, selectedOrder, dayOrder, sessionOrder);
        RegistrationParticipant eventParticipant = RegistrationParticipant.Create(
            tenant.Id, eventOrder.Id, eventUser.Id, ParticipantTypeEnum.Adult, null);
        RegistrationParticipant selectedParticipant = RegistrationParticipant.Create(
            tenant.Id, selectedOrder.Id, eventUser.Id, ParticipantTypeEnum.Adult, null);
        RegistrationParticipant dayParticipant = RegistrationParticipant.Create(
            tenant.Id, dayOrder.Id, dayUser.Id, ParticipantTypeEnum.Adult, null);
        RegistrationParticipant sessionParticipant = RegistrationParticipant.Create(
            tenant.Id, sessionOrder.Id, sessionUser.Id, ParticipantTypeEnum.Adult, null);
        context.RegistrationParticipants.AddRange(
            eventParticipant, selectedParticipant, dayParticipant, sessionParticipant);
        context.EventRegistrations.AddRange(
            CreateRegistration(tenant.Id, @event.Id, eventUser.Id, registered.Id, eventOrder.Id, eventParticipant.Id, ApprovalStatusEnum.Pending),
            CreateRegistration(tenant.Id, @event.Id, eventUser.Id, selectedOverlap.Id, selectedOrder.Id, selectedParticipant.Id, ApprovalStatusEnum.Approved),
            CreateRegistration(tenant.Id, @event.Id, dayUser.Id, registered.Id, dayOrder.Id, dayParticipant.Id, ApprovalStatusEnum.Approved),
            CreateRegistration(tenant.Id, @event.Id, sessionUser.Id, registered.Id, sessionOrder.Id, sessionParticipant.Id, ApprovalStatusEnum.Approved));
        await context.SaveChangesAsync();

        return new(
            tenant.Id,
            foreignTenant.Id,
            @event.Id,
            eventUser.Id,
            dayUser.Id,
            sessionUser.Id,
            dayOrder.Id,
            placements[0].Id,
            placements[1].Id,
            placements[2].Id);
    }

    private static Tenant CreateTenant(string name) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = $"Registration {name} tenant",
        Slug = $"registration-{name}-{Guid.NewGuid():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static User CreateUser(string name) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new UserPii
        {
            Email = $"registration-{name}-{Guid.NewGuid():N}@example.test",
            FirstName = name,
            LastName = "Coverage"
        }
    };

    private static EventDay CreateDay(Guid tenantId, Guid eventId, DateOnly date) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        EventId = eventId,
        Event = null!,
        LocalDate = date,
        IsPublished = true,
        AllowsDayScopeRegistration = true
    };

    private static Location CreateLocation(Guid tenantId, string name) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        FullName = name,
        Country = "BE",
        City = "Brussels"
    };

    private static EventSession CreateSession(
        Guid tenantId,
        Explore.Domain.Event @event,
        Guid dayId,
        EventLocation placement,
        string title)
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            EventId = @event.Id,
            Event = null!,
            EventDayId = dayId,
            Title = title,
            RegistrationModeId = (int)RegistrationModeEnum.Open,
        };
        session.AssignEventLocation(placement);
        return session;
    }

    private static RegistrationOrder CreateOrder(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        Guid catalogId,
        RegistrationOrderStatusEnum status)
    {
        DateTime createdAt = Now.UtcDateTime;
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId,
            eventId,
            userId,
            purchaserActorId: null,
            BookingPartyTypeEnum.Individual,
            catalogId,
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(),
                4,
                3,
                2,
                GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            registrationWorkflowVersionId: null,
            guestAccessTokenHash: null,
            "USD",
            createdAt,
            expiresAt: null);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, createdAt);
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, createdAt);

        if (status == RegistrationOrderStatusEnum.Confirmed)
        {
            order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, createdAt);
        }

        order.TransitionTo(status, createdAt);
        return order;
    }

    private static EventRegistration CreateRegistration(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        Guid sessionId,
        Guid orderId,
        Guid participantId,
        ApprovalStatusEnum status) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            EventId = eventId,
            Event = null!,
            LinkedUserId = userId,
            LinkedUser = null!,
            EventSessionId = sessionId,
            EventSession = null!,
            RegistrationOrderId = orderId,
            RegistrationParticipantId = participantId,
            RegistrationParticipant = null!,
            ApprovalStatusId = (int)status
        };

    private sealed record RegistrationGraph(
        Guid TenantId,
        Guid ForeignTenantId,
        Guid EventId,
        Guid EventUserId,
        Guid DayUserId,
        Guid SessionUserId,
        Guid DayOrderId,
        Guid SharedLocationId,
        Guid LaterSameDayLocationId,
        Guid CrossDayLocationId)
    {
        public Guid[] RequestedLocationIds =>
            [SharedLocationId, LaterSameDayLocationId, CrossDayLocationId];
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}

public sealed class RegistrationCoveragePostgreSqlFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("registration_coverage")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        await PostgresModelConstraintApplier.ApplyAsync(context);
        await LookupTableSeeder.SeedAsync(context);
    }

    public ExploreDbContext CreateDbContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Registration coverage PostgreSQL fixture seed.");
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }
}
