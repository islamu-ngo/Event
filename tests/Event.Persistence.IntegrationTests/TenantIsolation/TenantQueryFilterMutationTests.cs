// ABOUTME: Adversarial SQLite controls proving tenant isolation depends on both named filters and exact predicates.
// ABOUTME: Demonstrates that bypassing the tenant filter or deleting its replacement predicate exposes another tenant.

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed class TenantQueryFilterMutationTests
{
    [Test]
    public async Task RemovingNamedFilterOrExactPredicateExposesCrossTenantRow()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        Guid tenantAId = Guid.CreateVersion7();
        Guid tenantBId = Guid.CreateVersion7();
        await using (ExploreDbContext seedContext = CreateContext(connection, tenantAId))
        {
            seedContext.EnableTenantFilterBypass("Adversarial tenant-isolation fixture setup.");
            await seedContext.Database.EnsureCreatedAsync();
            await LookupTableSeeder.SeedAsync(seedContext);
            var tenantA = CreateTenant(tenantAId, "tenant-filter-a");
            var tenantB = CreateTenant(tenantBId, "tenant-filter-b");
            seedContext.Tenants.AddRange(tenantA, tenantB);
            seedContext.TenantSettingOverrides.AddRange(
                CreateSetting(tenantAId, "tenant.mutation.a"),
                CreateSetting(tenantBId, "tenant.mutation.b"));
            await seedContext.SaveChangesAsync();
        }

        await using ExploreDbContext tenantAContext = CreateContext(connection, tenantAId);
        List<Guid> namedFilterResult = await tenantAContext.TenantSettingOverrides
            .Select(setting => setting.TenantId)
            .ToListAsync();
        List<Guid> exactPredicateResult = await tenantAContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(setting => setting.TenantId == tenantAId)
            .Select(setting => setting.TenantId)
            .ToListAsync();
        List<Guid> predicateRemovedMutant = await tenantAContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Select(setting => setting.TenantId)
            .ToListAsync();

        await Assert.That(namedFilterResult).IsEquivalentTo([tenantAId]);
        await Assert.That(exactPredicateResult).IsEquivalentTo([tenantAId]);
        await Assert.That(predicateRemovedMutant).Contains(tenantBId);
        await Assert.That(predicateRemovedMutant.All(id => id == tenantAId)).IsFalse();
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection, Guid tenantId)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options)
        {
            TenantContext = new FixedTenantContext(tenantId),
            CurrentUserService = new FixedCurrentUser()
        };
    }

    private static Tenant CreateTenant(Guid tenantId, string slug)
    {
        return new Tenant
        {
            Id = tenantId,
            FullName = slug,
            Slug = slug,
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
    }

    private static TenantSetting CreateSetting(Guid tenantId, string key)
    {
        return new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = key,
            Value = "{}"
        };
    }

    private sealed record FixedTenantContext(Guid TenantId) : ITenantContext;

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public bool IsAuthenticated => false;
    }
}
