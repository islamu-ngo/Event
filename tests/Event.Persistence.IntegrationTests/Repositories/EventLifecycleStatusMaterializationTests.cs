// ABOUTME: PostgreSQL and file-backed SQLite round-trip coverage for private event and session lifecycle setters.
// ABOUTME: Proves explicit statuses and schedule projections materialize under tenant filtering.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<ProjectionTestContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventLifecycleStatusMaterializationTests(ProjectionTestContainerFixture fixture)
{
    [Test]
    public async Task ExplicitLifecycleStatusesRoundTripThroughPrivateSetters()
    {
        Guid tenantId;
        Guid eventId;
        Guid sessionId;
        await using (var seedContext = fixture.CreateDbContext())
        {
            await Assert.That(await seedContext.EventStatuses.FindAsync((int)EventStatusEnum.Published)).IsNotNull();
            await Assert.That(await seedContext.EventSessionStatuses.FindAsync((int)EventSessionStatusEnum.Published)).IsNotNull();
            var tenant = new Tenant
            {
                FullName = "Lifecycle materialization tenant",
                Slug = $"lifecycle-materialization-{Guid.NewGuid():N}",
                TenantStatusId = 2,
                TenantStatus = null!
            };
            var user = new User
            {
                Pii = new UserPii
                {
                    Email = $"lifecycle-materialization-{Guid.NewGuid():N}@example.test",
                    FirstName = "Lifecycle",
                    LastName = "Owner"
                }
            };
            seedContext.AddRange(tenant, user);
            await seedContext.SaveChangesAsync();

            var actor = new Actor
            {
                Pii = new ActorPii { DisplayName = "Lifecycle materialization owner" },
                ActorTypeId = 1,
                ActorType = null!,
                UserId = user.Id
            };
            seedContext.Actors.Add(actor);
            await seedContext.SaveChangesAsync();

            var @event = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                Title = "Lifecycle materialization event",
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                ActorId = actor.Id,
                Actor = null!,
                TenantId = tenant.Id,
                Tenant = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            seedContext.Events.Add(@event);
            await seedContext.SaveChangesAsync();

            DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(7);
            var session = new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                EventId = @event.Id,
                Event = null!,
                TenantId = tenant.Id,
                Tenant = null!,
                Title = "Lifecycle materialization session",
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
            seedContext.EventSessions.Add(session);
            await seedContext.SaveChangesAsync();

            tenantId = tenant.Id;
            eventId = @event.Id;
            sessionId = session.Id;
            seedContext.ChangeTracker.Clear();
        }

        await using var verificationContext = fixture.CreateDbContext(new TestTenantContext(tenantId));
        Explore.Domain.Event reloadedEvent = await verificationContext.Events
            .AsNoTracking()
            .Include(entity => entity.EventStatus)
            .SingleAsync(entity => entity.Id == eventId);
        EventSession reloadedSession = await verificationContext.EventSessions
            .AsNoTracking()
            .Include(entity => entity.EventSessionStatus)
            .SingleAsync(entity => entity.Id == sessionId);

        await Assert.That(reloadedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(reloadedEvent.EventStatus.Id).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(reloadedSession.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Published);
        await Assert.That(reloadedSession.EventSessionStatus!.Id).IsEqualTo((int)EventSessionStatusEnum.Published);
        await Assert.That(reloadedSession.EventId).IsEqualTo(reloadedEvent.Id);
        await Assert.That(reloadedSession.TenantId).IsEqualTo(reloadedEvent.TenantId);
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}

public sealed class EventLifecycleStatusSqliteMaterializationTests
{
    [Test]
    public async Task ExplicitLifecycleStatusesAndScheduleRoundTripThroughPrivateSetters()
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"event-lifecycle-materialization-{Guid.CreateVersion7():N}.db");
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid sessionId = Guid.CreateVersion7();
        DateTimeOffset start = new(DateTime.UtcNow.Date.AddDays(7).AddHours(10).AddMinutes(30));
        DateTimeOffset end = start.AddMinutes(90);

        try
        {
            await using (ExploreDbContext seedContext = CreateDbContext(databasePath))
            {
                seedContext.EnableTenantFilterBypass("SQLite lifecycle materialization test setup.");
                await seedContext.Database.EnsureCreatedAsync();
                await SeedRequiredLookupsAsync(seedContext);

                Guid userId = Guid.CreateVersion7();
                var tenant = new Tenant
                {
                    Id = tenantId,
                    FullName = "SQLite lifecycle materialization tenant",
                    Slug = $"sqlite-lifecycle-materialization-{tenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                };
                var user = new User
                {
                    Id = userId,
                    Pii = new UserPii
                    {
                        Email = $"sqlite-lifecycle-materialization-{userId:N}@example.test",
                        FirstName = "SQLite",
                        LastName = "Owner"
                    }
                };
                seedContext.AddRange(tenant, user);
                await seedContext.SaveChangesAsync();

                Guid actorId = Guid.CreateVersion7();
                seedContext.Actors.Add(new Actor
                {
                    Id = actorId,
                    Pii = new ActorPii { DisplayName = "SQLite lifecycle materialization owner" },
                    ActorTypeId = (int)ActorTypeEnum.User,
                    ActorType = null!,
                    UserId = userId
                });
                await seedContext.SaveChangesAsync();

                seedContext.Events.Add(new Explore.Domain.Event(EventStatusEnum.Published)
                {
                    Id = eventId,
                    Title = "SQLite lifecycle materialization event",
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    ActorId = actorId,
                    Actor = null!,
                    TenantId = tenantId,
                    Tenant = null!,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventStatus = null!,
                    EventFormatId = (int)EventFormatEnum.Local,
                    EventFormat = null!,
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
                await seedContext.SaveChangesAsync();

                var session = new EventSession(EventSessionStatusEnum.Published)
                {
                    Id = sessionId,
                    EventId = eventId,
                    Event = null!,
                    TenantId = tenantId,
                    Tenant = null!,
                    Title = "SQLite lifecycle materialization session",
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                session.Reschedule(UtcInstantRange.Create(start, end), "UTC", new EventScheduleProjectionCalculator());
                seedContext.EventSessions.Add(session);
                await seedContext.SaveChangesAsync();
            }

            await using ExploreDbContext verificationContext = CreateDbContext(databasePath);
            verificationContext.TenantContext = new TestTenantContext(tenantId);
            Explore.Domain.Event reloadedEvent = await verificationContext.Events
                .AsNoTracking()
                .Include(entity => entity.EventStatus)
                .SingleAsync(entity => entity.Id == eventId);
            EventSession reloadedSession = await verificationContext.EventSessions
                .AsNoTracking()
                .Include(entity => entity.EventSessionStatus)
                .Include(entity => entity.Event)
                .SingleAsync(entity => entity.Id == sessionId);

            await Assert.That(reloadedEvent.Title).IsEqualTo("SQLite lifecycle materialization event");
            await Assert.That(reloadedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
            await Assert.That(reloadedEvent.EventStatus.Id).IsEqualTo((int)EventStatusEnum.Published);
            await Assert.That(reloadedSession.Title).IsEqualTo("SQLite lifecycle materialization session");
            await Assert.That(reloadedSession.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Published);
            await Assert.That(reloadedSession.EventSessionStatus!.Id).IsEqualTo((int)EventSessionStatusEnum.Published);
            await Assert.That(reloadedSession.StartTime).IsEqualTo(start);
            await Assert.That(reloadedSession.EndTime).IsEqualTo(end);
            await Assert.That(reloadedSession.LocalStartDate).IsEqualTo(DateOnly.FromDateTime(start.UtcDateTime));
            await Assert.That(reloadedSession.LocalEndDate).IsEqualTo(DateOnly.FromDateTime(end.UtcDateTime));
            await Assert.That(reloadedSession.LocalStartTime).IsEqualTo(new TimeOnly(10, 30));
            await Assert.That(reloadedSession.LocalEndTime).IsEqualTo(new TimeOnly(12, 0));
            await Assert.That(reloadedSession.LocalStartMinuteOfDay).IsEqualTo(630);
            await Assert.That(reloadedSession.LocalEndMinuteOfDay).IsEqualTo(720);
            await Assert.That(reloadedSession.EventId).IsEqualTo(eventId);
            await Assert.That(reloadedSession.Event.Id).IsEqualTo(eventId);
            await Assert.That(reloadedEvent.TenantId).IsEqualTo(tenantId);
            await Assert.That(reloadedSession.TenantId).IsEqualTo(tenantId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private static async Task SeedRequiredLookupsAsync(ExploreDbContext context)
    {
        context.AddRange(
            new ActorType
            {
                Id = (int)ActorTypeEnum.User,
                MasterCode = "USER",
                FullName = "User"
            },
            new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active,
                MasterCode = "ACTIVE",
                FullName = "Active"
            },
            new EventStatus
            {
                Id = (int)EventStatusEnum.Published,
                MasterCode = "PUBLISHED",
                FullName = "Published"
            },
            new EventProvenanceType
            {
                Id = (int)EventProvenanceTypeEnum.OrganizerCreated,
                MasterCode = "ORGANIZER_CREATED",
                FullName = "Organizer created"
            },
            new EventSessionStatus
            {
                Id = (int)EventSessionStatusEnum.Published,
                MasterCode = "PUBLISHED",
                FullName = "Published"
            },
            new EventFormat
            {
                Id = (int)EventFormatEnum.Local,
                MasterCode = "LOCAL",
                FullName = "Local"
            },
            new VisibilityType
            {
                Id = (int)VisibilityTypeEnum.Public,
                MasterCode = "PUBLIC",
                FullName = "Public"
            });
        await context.SaveChangesAsync();
    }

    private static ExploreDbContext CreateDbContext(string databasePath) => new(
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .UseSnakeCaseNamingConvention()
            .Options);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
