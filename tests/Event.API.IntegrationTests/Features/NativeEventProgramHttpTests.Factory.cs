using System.Security.Cryptography;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventProgramHttpTests
{
    private sealed class ProgramFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"event-program-ordering-{Guid.CreateVersion7():N}.db");
        private readonly PostgreSqlContainer? _database;

        private ProgramFactory(bool useSqlite)
        {
            if (!useSqlite)
            {
                _database = new PostgreSqlBuilder("postgres:18-alpine")
                    .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
                    .Build();
            }
        }
        public Guid OwnerId { get; private set; }
        public Guid OutsiderId { get; private set; }
        public Guid PublicId { get; private set; }
        public Guid PrivateId { get; private set; }
        public Guid DraftId { get; private set; }
        public Guid DeletedId { get; private set; }
        public Guid ForeignId { get; private set; }
        public Guid GroupId { get; private set; }
        public Guid LocationId { get; private set; }

        public static async Task<ProgramFactory> CreateAsync(
            int publicItemCount = 2, bool useSqlite = false, DateTime? revealFullDetailsFromUtc = null)
        {
            var factory = new ProgramFactory(useSqlite);
            try
            {
                if (factory._database is not null)
                    await factory._database.StartAsync();
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                factory.ConfigureDatabase(options);
                await using (var db = new ExploreDbContext(options.Options))
                {
                    if (useSqlite)
                    {
                        await db.Database.EnsureCreatedAsync();
                        await SqliteDatabaseInitializer.InitializeAsync(db, default);
                    }
                    else
                    {
                        await db.Database.MigrateAsync();
                        await LookupTableSeeder.SeedAsync(db);
                    }
                }
                using var scope = factory.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
                var outsider = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
                var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context);
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                tenant.SetTenant(owner.TenantId);
                factory.OwnerId = owner.UserId;
                factory.OutsiderId = outsider.UserId;
                var published = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                var privateEvent = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Private);
                var draft = Parent(owner, EventStatusEnum.Draft, VisibilityTypeEnum.Public);
                var deleted = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                foreach (var parent in new[] { published, privateEvent, draft, deleted })
                {
                    context.Events.Add(parent);
                    context.EventRoleAssignments.Add(EventRoleAssignment.Create(owner.TenantId, parent.Id, owner.UserId,
                        (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddDays(-1), null, owner.UserId));
                    context.EventSessions.Add(Session(parent, "Managed draft item", EventSessionStatusEnum.Draft));
                }
                var location = new Location
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = owner.TenantId,
                    Tenant = null!,
                    FullName = "Approved venue",
                    City = "Hidden city",
                    Country = "Hidden country",
                    Timezone = "Europe/Brussels"
                };
                location.ClassifyAs(LocationKindEnum.CommercialVenue);
                location.SetManualAddress("Hidden street", "Hidden postcode");
                context.Locations.Add(location);
                var placement = EventLocation.CreatePhysical(owner.TenantId, published.Id, location.Id, owner.UserId,
                    new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc));
                context.EventLocations.Add(placement);
                context.EventLocationDisclosureAudits.Add(placement.CreateInitialDisclosureAudit());
                context.EventLocationDisclosureAudits.Add(placement.ChangeDisclosurePolicy(
                    EventLocationDisclosureFields.VenueName, LocationDisclosureAudienceEnum.Never, revealFullDetailsFromUtc,
                    placement.PolicyVersion, owner.UserId, EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                    new DateTime(2026, 7, 2, 8, 30, 0, DateTimeKind.Utc),
                    needsPrivacyReview: false));
                var group = Group(published, "Published track", true, 1);
                group.AssignEventLocation(placement);
                var hiddenGroup = Group(published, "Draft track", false, 0);
                var deletedGroup = Group(published, "Deleted track", true, -1);
                deletedGroup.IsDeleted = true;
                context.EventSessionGroups.AddRange(group, hiddenGroup, deletedGroup);
                for (var index = 0; index < publicItemCount; index++)
                {
                    var session = Session(published, $"Public item {index:D3}", EventSessionStatusEnum.Published);
                    session.AssignEventLocation(placement);
                    context.EventSessions.Add(session);
                    // Primary selection beats a lower-order non-primary assignment.
                    context.EventSessionGroupSessions.Add(Assignment(published, session, group, true, publicItemCount - index));
                    context.EventSessionGroupSessions.Add(Assignment(published, session, hiddenGroup, false, 0));
                }
                var unassigned = Session(published, "Unassigned public item", EventSessionStatusEnum.Published);
                context.EventSessions.Add(unassigned);
                context.EventSessionGroupSessions.Add(Assignment(published, unassigned, deletedGroup, true, 0));
                var deletedSession = Session(published, "Deleted item", EventSessionStatusEnum.Published);
                deletedSession.IsDeleted = true;
                context.EventSessions.Add(deletedSession);
                var draftGroupSession = Session(published, "Draft-group public item", EventSessionStatusEnum.Published);
                context.EventSessions.Add(draftGroupSession);
                context.EventSessionGroupSessions.Add(Assignment(published, draftGroupSession, hiddenGroup, true, 1));
                var agenda = new EventAgendaItem
                {
                    Id = Guid.CreateVersion7(),
                    EventId = published.Id,
                    Event = published,
                    TenantId = owner.TenantId,
                    Tenant = null!,
                    Title = "Outside-window agenda"
                };
                agenda.Reschedule(UtcInstantRange.Create(new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero)), "Europe/Brussels", new EventScheduleProjectionCalculator());
                context.EventAgendaItems.Add(agenda);
                published.RecalculateScheduleSummaryFromSessions();
                await context.SaveChangesAsync();
                deleted.IsDeleted = true;
                await context.SaveChangesAsync();
                tenant.SetTenant(foreign.TenantId);
                var foreignEvent = Parent(foreign, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                context.Events.Add(foreignEvent);
                context.EventSessions.Add(Session(foreignEvent, "Foreign item", EventSessionStatusEnum.Published));
                await context.SaveChangesAsync();
                factory.PublicId = published.Id;
                factory.PrivateId = privateEvent.Id;
                factory.DraftId = draft.Id;
                factory.DeletedId = deleted.Id;
                factory.ForeignId = foreignEvent.Id;
                factory.GroupId = group.Id;
                factory.LocationId = location.Id;
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        private static Explore.Domain.Event Parent(TenantScenarioSeed.TenantScenarioResult owner, EventStatusEnum status, VisibilityTypeEnum visibility)
        {
            var parent = new EventBuilder().WithActorId(owner.ActorId).WithTenantId(owner.TenantId)
                .WithStatus(status).WithVisibility(visibility)
                .WithSessionDates(new DateOnly(2026, 7, 21), new DateOnly(2026, 7, 21)).Build();
            parent.ApplyScheduleTimeZone("Europe/Brussels", new EventScheduleProjectionCalculator());
            return parent;
        }

        private static EventSession Session(Explore.Domain.Event parent, string title, EventSessionStatusEnum status)
        {
            var session = new EventSession(status)
            {
                Id = Guid.CreateVersion7(),
                EventId = parent.Id,
                Event = parent,
                TenantId = parent.TenantId,
                Tenant = null!,
                Title = title
            };
            session.Reschedule(UtcInstantRange.Create(new DateTimeOffset(2026, 7, 20, 23, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 21, 0, 30, 0, TimeSpan.Zero)), "Europe/Brussels", new EventScheduleProjectionCalculator());
            return session;
        }

        private static EventSessionGroup Group(Explore.Domain.Event parent, string name, bool published, int order) => new()
        {
            Id = Guid.CreateVersion7(),
            EventId = parent.Id,
            Event = parent,
            TenantId = parent.TenantId,
            Tenant = null!,
            Name = name,
            IsPublished = published,
            SortOrder = order
        };

        private static EventSessionGroupSession Assignment(Explore.Domain.Event parent, EventSession session, EventSessionGroup group, bool primary, int order) => new()
        {
            Id = Guid.CreateVersion7(),
            EventId = parent.Id,
            Event = parent,
            TenantId = parent.TenantId,
            Tenant = null!,
            EventSessionId = session.Id,
            EventSession = session,
            EventSessionGroupId = group.Id,
            EventSessionGroup = group,
            IsPrimary = primary,
            SortOrder = order
        };

        public HttpClient Client(Guid userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
            return client;
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            if (_database is not null)
                options.UseNpgsql(_database.GetConnectionString());
            else
                PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
                { Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _sqlitePath });
            options.UseSnakeCaseNamingConvention();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return db;
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (_database is not null)
            {
                await _database.DisposeAsync();
            }
            else
            {
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                ConfigureDatabase(options);
                await using var db = new ExploreDbContext(options.Options);
                SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
                File.Delete(_sqlitePath);
                File.Delete(_sqlitePath + "-wal");
                File.Delete(_sqlitePath + "-shm");
            }
        }
    }
}
