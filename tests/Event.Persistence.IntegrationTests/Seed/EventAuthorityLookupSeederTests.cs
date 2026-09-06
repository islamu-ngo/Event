// ABOUTME: Verifies EF model and runtime-seeder parity for event provenance and authority lookups.
// ABOUTME: Covers exact IDs/codes, missing-row repair, idempotency, named filters, and model seed prohibition.

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Seed;

[Category("EventAuthorityLookup")]
public sealed class EventAuthorityLookupSeederTests
{
    [Test]
    public async Task TenantAndSoftDeleteFilters_FailClosedAndRemainIndependent()
    {
        string databaseName = $"event-authority-filters-{Guid.NewGuid():N}";
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();

        await using (var seedContext = CreateSharedSeederContext(databaseName))
        {
            seedContext.Set<EventPublicAction>().AddRange(
                CreateAction(tenantA, isDeleted: false),
                CreateAction(tenantA, isDeleted: true),
                CreateAction(tenantB, isDeleted: false));
            await seedContext.SaveChangesAsync();
        }

        await using var missingTenantContext = CreateSharedSeederContext(databaseName);
        await Assert.That(await missingTenantContext.Set<EventPublicAction>().CountAsync()).IsEqualTo(0);

        await using var tenantContext = CreateSharedSeederContext(databaseName);
        tenantContext.TenantContext = new TestTenantContext(tenantA);
        await Assert.That(await tenantContext.Set<EventPublicAction>().CountAsync()).IsEqualTo(1);
        await Assert.That(await tenantContext.Set<EventPublicAction>()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .CountAsync())
            .IsEqualTo(2);
    }

    [Test]
    public async Task EfModelUsesNormalizedLookupsAndNamedAggregateFilters()
    {
        await using var context = CreateModelContext();

        await AssertLookupModelAsync<EventProvenanceType>(context, "event_provenance_types");
        await AssertLookupModelAsync<EventPublicActionKind>(context, "event_public_action_kinds");
        await AssertLookupModelAsync<EventPublicActionHealthState>(context, "event_public_action_health_states");
        await AssertLookupModelAsync<EventOrganizerClaimStatus>(context, "event_organizer_claim_statuses");

        foreach (var aggregateType in new[] { typeof(EventPublicAction), typeof(EventOrganizerClaim) })
        {
            var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(aggregateType)!;
            await Assert.That(entityType.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
            await Assert.That(entityType.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
            await Assert.That(entityType.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken).IsTrue();
        }

        var actionType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(EventPublicAction))!;
        await Assert.That(actionType.GetIndexes().Any(index =>
            !index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "EventId"])
            && index.GetFilter() is null)).IsTrue();
    }

    [Test]
    public async Task PublicRuntimeSeederSeedsExactEventAuthorityRows()
    {
        await using var context = CreateSeederContext("event-authority-public-seeder");

        await LookupTableSeeder.SeedEventAuthorityLookupsAsync(context, default);

        await AssertExactRowsAsync(context);
    }

    [Test]
    public async Task RuntimeSeederRepairsMissingRowsAndRemainsIdempotent()
    {
        await using var context = CreateSeederContext("event-authority-repair");
        await LookupTableSeeder.SeedEventAuthorityLookupsAsync(context, default);

        context.Set<EventProvenanceType>().Remove(await context.Set<EventProvenanceType>().SingleAsync(row =>
            row.Id == (int)EventProvenanceTypeEnum.CommunityReported));
        context.Set<EventPublicActionKind>().Remove(await context.Set<EventPublicActionKind>().SingleAsync(row =>
            row.Id == (int)EventPublicActionKindEnum.ExternalRegistration));
        context.Set<EventPublicActionHealthState>().Remove(await context.Set<EventPublicActionHealthState>().SingleAsync(row =>
            row.Id == (int)EventPublicActionHealthStateEnum.Active));
        context.Set<EventOrganizerClaimStatus>().Remove(await context.Set<EventOrganizerClaimStatus>().SingleAsync(row =>
            row.Id == (int)EventOrganizerClaimStatusEnum.Pending));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedEventAuthorityLookupsAsync(context, default);
        await LookupTableSeeder.SeedEventAuthorityLookupsAsync(context, default);

        await AssertExactRowsAsync(context);
    }

    [Test]
    [Category("Phase43Ticketing")]
    public async Task RuntimeSeeder_GrantsManageTicketsOnlyToOwnerAndManagerIdempotently()
    {
        await using var context = CreateSeederContext("event-ticket-management-seeder");

        await LookupTableSeeder.SeedAsync(context, default);
        await LookupTableSeeder.SeedAsync(context, default);

        var permission = await context.Permissions.SingleAsync(row =>
            row.MasterCode == PermissionCodes.EventManageTickets);
        var grantedRoleCodes = await (
            from grant in context.RolePermissions
            join role in context.Roles on grant.RoleId equals role.Id
            where grant.PermissionId == permission.Id
            orderby role.MasterCode
            select role.MasterCode)
            .ToArrayAsync();

        await Assert.That(grantedRoleCodes)
            .IsEquivalentTo(["event.manager", "event.owner"]);
        await Assert.That(grantedRoleCodes.Length).IsEqualTo(2);
    }

    private static ExploreDbContext CreateModelContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=event_authority_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new EventAuthorityTestDbContext(options);
    }

    private static ExploreDbContext CreateSeederContext(string prefix)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"{prefix}-{Guid.NewGuid():N}")
            .Options;

        return new EventAuthorityTestDbContext(options);
    }

    private readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot _databaseRoot = new();

    private ExploreDbContext CreateSharedSeederContext(string databaseName)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(databaseName, _databaseRoot)
            .Options;

        return new EventAuthorityTestDbContext(options);
    }

    private static EventPublicAction CreateAction(Guid tenantId, bool isDeleted)
    {
        var action = new EventPublicAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = Guid.CreateVersion7(),
            EventPublicActionKindId = (int)EventPublicActionKindEnum.OriginalSource,
            HealthStateId = (int)EventPublicActionHealthStateEnum.Active,
            IsDeleted = isDeleted,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        action.SetDestination(ExternalActionUrl.Create($"https://{action.Id:N}.example.org"));
        return action;
    }

    private static async Task AssertLookupModelAsync<TLookup>(ExploreDbContext context, string tableName)
        where TLookup : class
    {
        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TLookup))!;

        await Assert.That(entityType.GetTableName()).IsEqualTo(tableName);
        await Assert.That(entityType.FindPrimaryKey()!.Properties.Single().ClrType).IsEqualTo(typeof(int));
        await Assert.That(entityType.FindProperty("Id")!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
        await Assert.That(entityType.FindProperty("MasterCode")!.GetMaxLength()).IsEqualTo(100);
        await Assert.That(entityType.FindProperty("FullName")!.GetMaxLength()).IsEqualTo(200);
        await Assert.That(entityType.FindProperty("Description")!.GetMaxLength()).IsEqualTo(500);
        await Assert.That(entityType.GetIndexes().Any(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["MasterCode"]))).IsTrue();
        await Assert.That(entityType.GetSeedData().Count).IsEqualTo(0);
    }

    private static async Task AssertExactRowsAsync(ExploreDbContext context)
    {
        await AssertRowsAsync(context.Set<EventProvenanceType>(),
        [
            (1, "ORGANIZER_CREATED"),
            (2, "COMMUNITY_REPORTED"),
            (3, "TENANT_CURATED"),
            (4, "IMPORTED"),
            (5, "FEDERATED")
        ]);
        await AssertRowsAsync(context.Set<EventPublicActionKind>(),
        [
            (1, "ORIGINAL_SOURCE"),
            (2, "EXTERNAL_EVENT_PAGE"),
            (3, "EXTERNAL_REGISTRATION"),
            (4, "OPTIONAL_QUESTIONNAIRE"),
            (5, "LIVESTREAM"),
            (6, "ORGANIZER_CONTACT")
        ]);
        await AssertRowsAsync(context.Set<EventPublicActionHealthState>(),
        [
            (1, "PENDING_REVIEW"),
            (2, "ACTIVE"),
            (3, "BROKEN"),
            (4, "UNSAFE"),
            (5, "DISABLED"),
            (6, "EXPIRED")
        ]);
        await AssertRowsAsync(context.Set<EventOrganizerClaimStatus>(),
        [
            (1, "PENDING"),
            (2, "EVIDENCE_REQUIRED"),
            (3, "APPROVED"),
            (4, "REJECTED"),
            (5, "WITHDRAWN"),
            (6, "EXPIRED")
        ]);
    }

    private static async Task AssertRowsAsync<TLookup>(DbSet<TLookup> set, (int Id, string MasterCode)[] expected)
        where TLookup : class
    {
        var rows = await set
            .OrderBy(row => EF.Property<int>(row, "Id"))
            .Select(row => new
            {
                Id = EF.Property<int>(row, "Id"),
                MasterCode = EF.Property<string>(row, "MasterCode")
            })
            .ToArrayAsync();

        await Assert.That(rows.Select(row => (row.Id, row.MasterCode)).SequenceEqual(expected)).IsTrue();
    }

    private sealed class EventAuthorityTestDbContext(DbContextOptions<ExploreDbContext> options)
        : ExploreDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Actor>()
                .Ignore(actor => actor.MergesFrom)
                .Ignore(actor => actor.MergesInto);
            modelBuilder.Ignore<ActorMerge>();
        }
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
