using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventDayHttpTests
{
    private sealed partial class DayFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"native-event-days-{Guid.CreateVersion7():N}.db");
        public Guid OwnerId { get; private set; }
        public Guid OutsiderId { get; private set; }
        public Guid PublicEventId { get; private set; }
        public Guid PrivateEventId { get; private set; }
        public Guid DraftEventId { get; private set; }
        public Guid DeletedEventId { get; private set; }
        public Guid ForeignEventId { get; private set; }
        public Guid ForeignTenantId { get; private set; }
        public Guid UnownedEventId { get; private set; }
        public Guid PublicDayId { get; private set; }
        public Guid ForeignDayId { get; private set; }
        public Guid DeletedParentDayId { get; private set; }

        public static async Task<DayFactory> CreateAsync()
        {
            var factory = new DayFactory();
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
                factory.OwnerId = owner.UserId;
                factory.OutsiderId = outsider.UserId;
                factory.ForeignTenantId = foreign.TenantId;
                var publicEvent = Parent(owner, VisibilityTypeEnum.Public);
                var privateEvent = Parent(owner, VisibilityTypeEnum.Private);
                var draftEvent = new EventBuilder().WithActorId(owner.ActorId).WithTenantId(owner.TenantId)
                    .WithStatus(EventStatusEnum.Draft).Build();
                var deletedEvent = Parent(owner, VisibilityTypeEnum.Public);
                var foreignEvent = Parent(foreign, VisibilityTypeEnum.Public);
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                tenant.SetTenant(owner.TenantId);
                var unownedEvent = Parent(outsider, VisibilityTypeEnum.Public);
                context.Events.Add(unownedEvent);
                factory.UnownedEventId = unownedEvent.Id;
                foreach (var parent in new[] { publicEvent, privateEvent, draftEvent, deletedEvent })
                {
                    context.Events.Add(parent);
                    context.EventRoleAssignments.Add(EventRoleAssignment.Create(owner.TenantId, parent.Id, owner.UserId,
                        (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddDays(-1), null, owner.UserId));
                    context.EventDays.Add(Day(parent, 1));
                }
                var publicDay = Day(publicEvent, 2);
                publicDay.IsPublished = false;
                context.EventDays.Add(publicDay);
                await context.SaveChangesAsync();
                factory.PublicDayId = publicDay.Id;
                factory.DeletedParentDayId = context.EventDays.Local.Single(day => day.EventId == deletedEvent.Id).Id;
                deletedEvent.IsDeleted = true;
                await context.SaveChangesAsync();
                tenant.SetTenant(foreign.TenantId);
                context.Events.Add(foreignEvent);
                var foreignDay = Day(foreignEvent, 1);
                context.EventDays.Add(foreignDay);
                await context.SaveChangesAsync();
                factory.PublicEventId = publicEvent.Id;
                factory.PrivateEventId = privateEvent.Id;
                factory.DraftEventId = draftEvent.Id;
                factory.DeletedEventId = deletedEvent.Id;
                factory.ForeignEventId = foreignEvent.Id;
                factory.ForeignDayId = foreignDay.Id;
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        private static Explore.Domain.Event Parent(TenantScenarioSeed.TenantScenarioResult owner, VisibilityTypeEnum visibility) =>
            new EventBuilder().WithActorId(owner.ActorId).WithTenantId(owner.TenantId)
                .WithStatus(EventStatusEnum.Published).WithVisibility(visibility).Build();

        private static EventDay Day(Explore.Domain.Event parent, int order) => new()
        {
            Id = Guid.CreateVersion7(), TenantId = parent.TenantId, EventId = parent.Id, Event = parent, Tenant = null!,
            LocalDate = new DateOnly(2027, 1, order), SortOrder = order, Label = $"Day {order}", IsPublished = true
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
            options.AddInterceptors(StorageReads, DayWrites);
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
}
