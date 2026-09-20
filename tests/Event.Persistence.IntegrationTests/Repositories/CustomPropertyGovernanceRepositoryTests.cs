using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class CustomPropertyGovernanceSqliteRepositoryTests
{
    [Test]
    public async Task ReportUsesPersistedRowsAndFiltersBeforeCountingAndPaging()
    {
        var path = Path.Combine(Path.GetTempPath(), $"governance-matrix-{Guid.CreateVersion7():N}.db");
        try
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = path
            });
            await using var db = new ExploreDbContext(options.Options);
            await db.Database.MigrateAsync();
            await CustomPropertyGovernanceRepositoryContract.AssertAsync(db);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
public sealed class CustomPropertyGovernancePostgreSqlRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ReportUsesPersistedRowsAndFiltersBeforeCountingAndPaging()
    {
        await using var db = fixture.CreateDbContext();
        await CustomPropertyGovernanceRepositoryContract.AssertAsync(db);
    }
}

[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
[NotInParallel("CustomPropertyGovernanceProviderMatrix")]
public sealed class CustomPropertyGovernanceExternalRepositoryTests(AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ReportUsesPersistedRowsAndFiltersBeforeCountingAndPaging(PrimaryDatabaseProvider provider)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.CreateOptions(provider, PrimaryDatabaseRole.Migrator));
        await using var db = new ExploreDbContext(options.Options);
        await db.Database.MigrateAsync();
        await CustomPropertyGovernanceRepositoryContract.AssertAsync(db);
    }
}

internal static class CustomPropertyGovernanceRepositoryContract
{
    internal static async Task AssertAsync(ExploreDbContext db)
    {
        db.EnableTenantFilterBypass("Seed isolated governance report provider graphs.");
        await LookupTableSeeder.SeedAsync(db);
        var tenantId = await SeedAsync(db);
        var foreignTenantId = await SeedAsync(db);
        db.ChangeTracker.Clear();
        db.TenantContext = new TenantContext(tenantId);
        var repository = new Explore.Persistence.Repositories.CustomPropertyGovernanceRepository(db);
        await Assert.That(await repository.GetTotalEventCountForTenantAsync(tenantId, default)).IsEqualTo(1);
        await Assert.That(await repository.GetTotalEventCountForTenantAsync(foreignTenantId, default)).IsEqualTo(0);
        var (rows, count) = await repository.GetGovernanceRowsAsync(tenantId, null, 1, 20, null, 1, default);
        await Assert.That(count).IsEqualTo(4);
        await Assert.That(rows.Select(row => row.Key)).IsEquivalentTo(new[] { "a-none", "b-search", "c-layer1", "d-layer2" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(rows.All(row => row.TenantId == tenantId)).IsTrue();
        await Assert.That(rows[2].ActiveInstanceCount).IsEqualTo(1);
        await Assert.That(rows[2].LastUsedAt).IsEqualTo(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));
        await Assert.That(rows[0].LastUsedAt).IsNull();
        await Assert.That(rows[3].EntityScope).IsEqualTo("EventSession");
        foreach (var (recommendation, key) in new[]
        {
            (Explore.Domain.Enums.PromotionRecommendation.None, "a-none"),
            (Explore.Domain.Enums.PromotionRecommendation.ConsiderProjectionFirst, "b-search"),
            (Explore.Domain.Enums.PromotionRecommendation.ConsiderLayer1Promotion, "c-layer1"),
            (Explore.Domain.Enums.PromotionRecommendation.ConsiderLayer2Promotion, "d-layer2")
        })
        {
            var filtered = await repository.GetGovernanceRowsAsync(tenantId, null, 1, 1, recommendation, 1, default);
            await Assert.That(filtered.TotalCount).IsEqualTo(1);
            await Assert.That(filtered.Items.Single().Key).IsEqualTo(key);
            await Assert.That(Explore.Application.Features.CustomPropertyGovernance.Handlers.Queries
                .GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(filtered.Items.Single(), 1)).IsEqualTo(recommendation);
            var beyond = await repository.GetGovernanceRowsAsync(tenantId, null, 2, 1, recommendation, 1, default);
            await Assert.That(beyond.TotalCount).IsEqualTo(1);
            await Assert.That(beyond.Items).IsEmpty();
        }
        var scoped = await repository.GetGovernanceRowsAsync(tenantId, "EventSession", 1, 1, null, 1, default);
        await Assert.That(scoped.TotalCount).IsEqualTo(1);
        await Assert.That(scoped.Items.Single().Key).IsEqualTo("d-layer2");
        var foreign = await repository.GetGovernanceRowsAsync(foreignTenantId, null, 1, 20, null, 1, default);
        await Assert.That(foreign.TotalCount).IsEqualTo(0);
        await Assert.That(foreign.Items).IsEmpty();
    }

    private static async Task<Guid> SeedAsync(ExploreDbContext db)
    {
        var tenant = new Explore.Domain.Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Governance matrix",
            Slug = $"governance-{Guid.CreateVersion7():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var user = new Explore.Domain.User
        {
            Id = Guid.CreateVersion7(),
            Pii = new Explore.Domain.UserPii
            {
                Email = $"governance-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Governance",
                LastName = "Matrix"
            }
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var actor = new Explore.Domain.Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
            Pii = new Explore.Domain.ActorPii { DisplayName = "Governance matrix actor" }
        };
        db.Actors.Add(actor);
        await db.SaveChangesAsync();
        var eventEntity = new Explore.Domain.Event(Explore.Domain.Enums.EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Governance matrix event",
            TenantId = tenant.Id,
            Tenant = tenant,
            ActorId = actor.Id,
            Actor = actor,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            ConcurrencyStamp = Guid.CreateVersion7(),
            EventProvenanceTypeId = (int)Explore.Domain.Enums.EventProvenanceTypeEnum.OrganizerCreated
        };
        db.Events.Add(eventEntity);
        var session = new Explore.Domain.EventSession(Explore.Domain.Enums.EventSessionStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            EventId = eventEntity.Id,
            Event = eventEntity,
            Title = "Governance session",
            EventSessionKindId = 1,
            RegistrationModeId = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        db.EventSessions.Add(session);
        var none = Definition("a-none");
        var search = Definition("b-search");
        search.IsSearchable = true;
        var layer1 = Definition("c-layer1");
        layer1.IsSearchable = layer1.IsModerationRelevant = true;
        var inactive = Definition("inactive");
        inactive.IsActive = false;
        var deleted = Definition("deleted");
        deleted.IsDeleted = true;
        db.EventCustomPropertyDefinitions.AddRange(none, search, layer1, inactive, deleted);
        db.EventSessionCustomPropertyDefinitions.Add(new Explore.Domain.EventSessionCustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            EventSessionId = session.Id,
            Namespace = "tenant.custom",
            Key = "d-layer2",
            DisplayName = "Session analytics",
            IsActive = true,
            IsAnalyticsRelevant = true,
            PropertyType = Explore.Domain.Enums.PropertyType.Text,
            ExposureLevel = Explore.Domain.Enums.ExposureLevel.TenantAdminOnly,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        db.EventCustomPropertyValues.AddRange(new Explore.Domain.EventCustomPropertyValue
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            EventId = eventEntity.Id,
            EventCustomPropertyDefinitionId = layer1.Id,
            TextValue = "not-report-content",
            UpdatedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            ConcurrencyStamp = Guid.CreateVersion7()
        }, new Explore.Domain.EventCustomPropertyValue
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            EventId = eventEntity.Id,
            EventCustomPropertyDefinitionId = layer1.Id,
            Ordinal = 1,
            TextValue = "deleted-not-report-content",
            IsDeleted = true,
            UpdatedAt = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        await db.SaveChangesAsync();
        return tenant.Id;

        Explore.Domain.EventCustomPropertyDefinition Definition(string key) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            EventId = eventEntity.Id,
            Namespace = "tenant.custom",
            Key = key,
            DisplayName = key,
            IsActive = true,
            PropertyType = Explore.Domain.Enums.PropertyType.Text,
            ExposureLevel = Explore.Domain.Enums.ExposureLevel.Public,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    private sealed record TenantContext(Guid TenantId) : ITenantContext;
}
