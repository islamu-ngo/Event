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
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventAgendaItemHttpTests
{
    private sealed partial class AgendaFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"native-agenda-{Guid.CreateVersion7():N}.db");
        public Guid OwnerId { get; private set; }
        public Guid OutsiderId { get; private set; }
        public Guid PublicId { get; private set; }
        public Guid PrivateId { get; private set; }
        public Guid DraftId { get; private set; }
        public Guid DeletedId { get; private set; }
        public Guid ForeignId { get; private set; }
        public Guid UnownedId { get; private set; }
        public Guid ItemId { get; private set; }
        public Guid ForeignItemId { get; private set; }
        public Guid DeletedParentItemId { get; private set; }
        public Guid LocationId { get; private set; }
        public Guid PlacementId { get; private set; }
        public Guid DayId { get; private set; }
        public Guid DestinationDayId { get; private set; }

        public static async Task<AgendaFactory> CreateAsync()
        {
            var factory = new AgendaFactory();
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
                var outsider = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
                var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context);
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                tenant.SetTenant(owner.TenantId);
                factory.OwnerId = owner.UserId;
                factory.OutsiderId = outsider.UserId;
                var published = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                var privateEvent = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Private);
                privateEvent.ApplyScheduleTimeZone("America/New_York", new EventScheduleProjectionCalculator());
                var draft = Parent(owner, EventStatusEnum.Draft, VisibilityTypeEnum.Public);
                var deleted = Parent(owner, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                foreach (var parent in new[] { published, privateEvent, draft, deleted })
                {
                    context.Events.Add(parent);
                    context.EventRoleAssignments.Add(EventRoleAssignment.Create(owner.TenantId, parent.Id, owner.UserId,
                        (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddDays(-1), null, owner.UserId));
                    var item = Item(parent, "Seed agenda");
                    context.EventAgendaItems.Add(item);
                    if (parent == published) factory.ItemId = item.Id;
                    if (parent == deleted) factory.DeletedParentItemId = item.Id;
                }
                var unowned = Parent(outsider, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                context.Events.Add(unowned);
                context.EventRoleAssignments.Add(EventRoleAssignment.Create(outsider.TenantId, unowned.Id, outsider.UserId,
                    (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddDays(-1), null, outsider.UserId));
                var location = new Location
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = owner.TenantId,
                    Tenant = null!,
                    FullName = "Approved agenda venue",
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
                    EventLocationDisclosureFields.VenueName, LocationDisclosureAudienceEnum.Never, null,
                    placement.PolicyVersion, owner.UserId, EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                    new DateTime(2026, 7, 2, 8, 30, 0, DateTimeKind.Utc), needsPrivacyReview: false));
                context.EventAgendaItems.Local.Single(item => item.Id == factory.ItemId).AssignEventLocation(placement);
                var day = Day(published, new DateOnly(2026, 7, 21));
                var destinationDay = Day(privateEvent, new DateOnly(2026, 7, 20));
                context.EventDays.AddRange(day, destinationDay);
                await context.SaveChangesAsync();
                deleted.IsDeleted = true;
                await context.SaveChangesAsync();
                tenant.SetTenant(foreign.TenantId);
                var foreignEvent = Parent(foreign, EventStatusEnum.Published, VisibilityTypeEnum.Public);
                context.Events.Add(foreignEvent);
                var foreignItem = Item(foreignEvent, "Foreign agenda");
                context.EventAgendaItems.Add(foreignItem);
                await context.SaveChangesAsync();
                factory.PublicId = published.Id;
                factory.PrivateId = privateEvent.Id;
                factory.DraftId = draft.Id;
                factory.DeletedId = deleted.Id;
                factory.ForeignId = foreignEvent.Id;
                factory.UnownedId = unowned.Id;
                factory.ForeignItemId = foreignItem.Id;
                factory.LocationId = location.Id;
                factory.PlacementId = placement.Id;
                factory.DayId = day.Id;
                factory.DestinationDayId = destinationDay.Id;
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
                .WithStatus(status).WithVisibility(visibility).Build();
            parent.ApplyScheduleTimeZone("Europe/Brussels", new EventScheduleProjectionCalculator());
            return parent;
        }

        private static EventAgendaItem Item(Explore.Domain.Event parent, string title)
        {
            var item = new EventAgendaItem
            {
                Id = Guid.CreateVersion7(),
                TenantId = parent.TenantId,
                Tenant = null!,
                EventId = parent.Id,
                Event = parent,
                Title = title
            };
            item.Reschedule(UtcInstantRange.Create(Start, Start.AddHours(1)), parent.EventTimeZoneId!, new EventScheduleProjectionCalculator());
            return item;
        }

        private static EventDay Day(Explore.Domain.Event parent, DateOnly date) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = parent.TenantId,
            Tenant = null!,
            EventId = parent.Id,
            Event = parent,
            LocalDate = date,
            IsPublished = true
        };

        public HttpClient Client(Guid userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
            return client;
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            { Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _path });
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(WriteFailure);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                ConfigureMutationControls(services);
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
}
