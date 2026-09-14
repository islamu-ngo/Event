using System.Data.Common;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeAgendaProjectionHttpTests
{
    private sealed class ProjectionFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"agenda-projection-{Guid.CreateVersion7():N}.db");
        public Guid OwnerId { get; private set; }
        public Guid PublicId { get; private set; }
        public Guid PrivateId { get; private set; }
        public Guid DraftId { get; private set; }
        public Guid DeletedId { get; private set; }
        public Guid ForeignId { get; private set; }
        public Guid LocationId { get; private set; }
        public Guid RoomId { get; private set; }
        public Guid PlacementId { get; private set; }
        public Guid EmptyDayId { get; private set; }
        public Guid ScheduleDayId { get; private set; }
        public TokenObservation Tokens { get; } = new();

        public static async Task<ProjectionFactory> CreateAsync()
        {
            var factory = new ProjectionFactory();
            try
            {
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                factory.ConfigureDatabase(options);
                await using (var db = new ExploreDbContext(options.Options))
                {
                    await db.Database.EnsureCreatedAsync();
                    await SqliteDatabaseInitializer.InitializeAsync(db, default);
                }
                using var scope = factory.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
                var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context);
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                tenant.SetTenant(owner.TenantId);
                factory.OwnerId = owner.UserId;
                var published = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                var privateEvent = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Private);
                var draft = Parent(owner, EventStatusEnum.Draft, VisibilityTypeEnum.Public);
                var deleted = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                foreach (var parent in new[] { published, privateEvent, draft, deleted })
                {
                    context.Events.Add(parent);
                    context.EventRoleAssignments.Add(EventRoleAssignment.Create(owner.TenantId, parent.Id, owner.UserId,
                        (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active,
                        new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), null, owner.UserId));
                    if (parent != published)
                        context.EventAgendaItems.Add(Item(parent, "Ineligible parent canary", 21, 7, 0));
                }
                var location = new Location
                {
                    Id = Guid.CreateVersion7(), TenantId = owner.TenantId, Tenant = null!, FullName = "Approved projection venue",
                    City = "Private city canary", Country = "Private country canary", Timezone = "Europe/Brussels"
                };
                location.ClassifyAs(LocationKindEnum.CommercialVenue);
                location.SetManualAddress("Private street canary", "Private postcode canary");
                var room = new LocationRoom
                {
                    Id = Guid.CreateVersion7(), TenantId = owner.TenantId, Tenant = null!, LocationId = location.Id,
                    Location = location, Name = "Private room canary"
                };
                context.Locations.Add(location);
                context.LocationRooms.Add(room);
                var placement = EventLocation.CreatePhysical(owner.TenantId, published.Id, location.Id, owner.UserId,
                    new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc));
                context.EventLocations.Add(placement);
                context.EventLocationDisclosureAudits.Add(placement.CreateInitialDisclosureAudit());
                context.EventLocationDisclosureAudits.Add(placement.ChangeDisclosurePolicy(
                    EventLocationDisclosureFields.VenueName, LocationDisclosureAudienceEnum.Never, null,
                    placement.PolicyVersion, owner.UserId, EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                    new DateTime(2026, 7, 2, 8, 30, 0, DateTimeKind.Utc), needsPrivacyReview: false));
                var day = Day(published, 21, 2);
                var empty = Day(published, 23, -1);
                empty.Label = "Opening day";
                empty.Description = "Published description";
                empty.AllowsDayScopeRegistration = true;
                var hidden = Day(published, 20, -2);
                hidden.IsPublished = false;
                hidden.Label = "Draft day canary";
                var deletedDay = Day(published, 24, -3);
                deletedDay.IsDeleted = true;
                context.EventDays.AddRange(day, empty, hidden, deletedDay);
                var early = Session(published, "Local early", 20, 23, 30, 90);
                early.EventDay = day;
                early.EventDayId = day.Id;
                early.AssignEventLocation(placement);
                early.RoomId = room.Id;
                var tie = Session(published, "Tie session", 21, 7, 0, 5);
                var hiddenSession = Session(published, "Draft-day session canary", 20, 7, 0, 0);
                hiddenSession.EventDay = hidden;
                hiddenSession.EventDayId = hidden.Id;
                var draftSession = Session(published, "Draft session canary", 21, 8, 0, 0, EventSessionStatusEnum.Draft);
                var deletedSession = Session(published, "Deleted session canary", 21, 10, 0, 0);
                deletedSession.IsDeleted = true;
                var unscheduled = new EventSession(EventSessionStatusEnum.Published)
                {
                    Id = Guid.CreateVersion7(), EventId = published.Id, Event = published, TenantId = owner.TenantId,
                    Tenant = null!, Title = "Unscheduled canary"
                };
                context.EventSessions.AddRange(early, tie, hiddenSession, draftSession, deletedSession, unscheduled);
                var tieItem = Item(published, "Tie agenda", 21, 7, -5);
                tieItem.AssignEventLocation(placement);
                tieItem.RoomId = room.Id;
                var hiddenItem = Item(published, "Draft-day agenda canary", 20, 7, 0);
                hiddenItem.EventDay = hidden;
                hiddenItem.EventDayId = hidden.Id;
                var deletedItem = Item(published, "Deleted agenda canary", 21, 11, 0);
                deletedItem.IsDeleted = true;
                context.EventAgendaItems.AddRange(tieItem, Item(published, "Local later", 21, 12, -90),
                    Item(published, "Unlabelled day", 22, 7, 0), hiddenItem, deletedItem);
                await context.SaveChangesAsync();
                deleted.IsDeleted = true;
                await context.SaveChangesAsync();
                tenant.SetTenant(foreign.TenantId);
                var foreignEvent = Parent(foreign, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                context.Events.Add(foreignEvent);
                context.EventAgendaItems.Add(Item(foreignEvent, "Foreign agenda canary", 21, 7, 0));
                await context.SaveChangesAsync();
                factory.PublicId = published.Id;
                factory.PrivateId = privateEvent.Id;
                factory.DraftId = draft.Id;
                factory.DeletedId = deleted.Id;
                factory.ForeignId = foreignEvent.Id;
                factory.LocationId = location.Id;
                factory.RoomId = room.Id;
                factory.PlacementId = placement.Id;
                factory.EmptyDayId = empty.Id;
                factory.ScheduleDayId = day.Id;
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
                .WithTitle("Projection event").WithStatus(status).WithVisibility(visibility).Build();
            parent.ApplyScheduleTimeZone("Europe/Brussels", new EventScheduleProjectionCalculator());
            return parent;
        }

        private static EventSession Session(Explore.Domain.Event parent, string title, int day, int hour, int minute, int order,
            EventSessionStatusEnum status = EventSessionStatusEnum.Published)
        {
            var session = new EventSession(status)
            {
                Id = Guid.CreateVersion7(), EventId = parent.Id, Event = parent, TenantId = parent.TenantId,
                Tenant = null!, Title = title, SortOrder = order
            };
            var start = new DateTimeOffset(2026, 7, day, hour, minute, 0, TimeSpan.Zero);
            session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "Europe/Brussels", new EventScheduleProjectionCalculator());
            return session;
        }

        private static EventAgendaItem Item(Explore.Domain.Event parent, string title, int day, int hour, int order)
        {
            var item = new EventAgendaItem
            {
                Id = Guid.CreateVersion7(), EventId = parent.Id, Event = parent, TenantId = parent.TenantId,
                Tenant = null!, Title = title, SortOrder = order
            };
            var start = new DateTimeOffset(2026, 7, day, hour, 0, 0, TimeSpan.Zero);
            item.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "Europe/Brussels", new EventScheduleProjectionCalculator());
            return item;
        }

        private static EventDay Day(Explore.Domain.Event parent, int day, int order) => new()
        {
            Id = Guid.CreateVersion7(), EventId = parent.Id, Event = parent, TenantId = parent.TenantId,
            Tenant = null!, LocalDate = new DateOnly(2026, 7, day), IsPublished = true, SortOrder = order
        };

        public HttpClient OwnerClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(OwnerId));
            return client;
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            { Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _path });
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(Tokens);
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
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
        }
    }

    private sealed class TokenObservation : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public List<CancellationToken> Observed { get; } = [];
        public CancellationTokenSource? CancelAtTokenBearingRead { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Observed.Add(cancellationToken);
                if (cancellationToken.CanBeCanceled)
                    CancelAtTokenBearingRead?.Cancel();
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
