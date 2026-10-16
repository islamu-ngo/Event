using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class TenantLookupSourceBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GetTenantLookupsAsync_WithAmbientTenant_ReturnsOnlyActiveTenantsAndTheirDomainSettings()
    {
        await fixture.ResetAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var seedContext = new ExploreDbContext(options.Options);

        var tenantA = CreateTenant("lookup-a", TenantStatusEnum.Active);
        var tenantB = CreateTenant("lookup-b", TenantStatusEnum.Active);
        var inactiveTenant = CreateTenant("lookup-inactive", TenantStatusEnum.Suspended);
        seedContext.Tenants.AddRange(tenantA, tenantB, inactiveTenant);
        await seedContext.SaveChangesAsync();

        seedContext.TenantSettingOverrides.AddRange(
            CreateTenantSetting(tenantA.Id, GovernanceSettingKeys.Domains.TenantSubdomain, "alpha"),
            CreateTenantSetting(tenantB.Id, GovernanceSettingKeys.Domains.TenantCustomDomain, "bravo.example.com"),
            CreateTenantSetting(inactiveTenant.Id, GovernanceSettingKeys.Domains.TenantSubdomain, "inactive"));
        await seedContext.SaveChangesAsync();

        await using var filteredContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));
        var visibleSettingsWithoutBypass = await filteredContext.TenantSettingOverrides
            .AsNoTracking()
            .Select(setting => setting.TenantId)
            .ToListAsync();

        var lookupSource = new TenantLookupSource(new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)));
        var lookups = await lookupSource.GetTenantLookupsAsync();

        await Assert.That(visibleSettingsWithoutBypass).IsEquivalentTo([tenantA.Id]);
        await Assert.That(lookups.Select(lookup => lookup.TenantId)).IsEquivalentTo([tenantA.Id, tenantB.Id]);

        var tenantALookup = lookups.Single(lookup => lookup.TenantId == tenantA.Id);
        var tenantBLookup = lookups.Single(lookup => lookup.TenantId == tenantB.Id);

        await Assert.That(tenantALookup.Slug).IsEqualTo(tenantA.Slug);
        await Assert.That(tenantALookup.Subdomain).IsEqualTo("alpha");
        await Assert.That(tenantALookup.CustomDomain).IsNull();

        await Assert.That(tenantBLookup.Slug).IsEqualTo(tenantB.Slug);
        await Assert.That(tenantBLookup.Subdomain).IsNull();
        await Assert.That(tenantBLookup.CustomDomain).IsEqualTo("bravo.example.com");

        var lifecycle = new Explore.Application.Services.TenantLifecycleAccessService(
            NSubstitute.Substitute.For<Explore.Application.Contracts.Persistence.ITenantRepository>(),
            NSubstitute.Substitute.For<Explore.Application.Contracts.Identity.IAdminContext>(),
            NSubstitute.Substitute.For<Explore.Application.Contracts.Persistence.IInstanceBootstrapStateRepository>(),
            lookupSource);
        await Assert.That(await lifecycle.IsPublicAsync(tenantA.Id)).IsTrue();
        await Assert.That(await lifecycle.IsPublicAsync(inactiveTenant.Id)).IsFalse();
        await Assert.That(await lookupSource.GetCurrentDomainTenantAsync("BRAVO.example.com", true))
            .IsEqualTo<Guid?>(tenantB.Id);

        var reservation = await seedContext.TenantSettingOverrides.SingleAsync(
            setting => setting.TenantId == tenantB.Id);
        reservation.Value = SettingValueSerializer.Serialize("replacement.example.com");
        await seedContext.SaveChangesAsync();
        await Assert.That(await lookupSource.GetCurrentDomainTenantAsync("bravo.example.com", true)).IsNull();
        await Assert.That(await lookupSource.GetCurrentDomainTenantAsync("replacement.example.com", true))
            .IsEqualTo<Guid?>(tenantB.Id);
        tenantB.TenantStatusId = (int)TenantStatusEnum.Suspended;
        await seedContext.SaveChangesAsync();
        await Assert.That(await lifecycle.IsPublicAsync(tenantB.Id)).IsFalse();
        await Assert.That(await lookupSource.GetCurrentDomainTenantAsync("replacement.example.com", true)).IsNull();
    }

    private static Tenant CreateTenant(string slugPrefix, TenantStatusEnum status)
    {
        return new Tenant
        {
            FullName = $"Tenant Lookup {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)status,
            TenantStatus = null!,
        };
    }

    private static TenantSetting CreateTenantSetting(Guid tenantId, string key, string value)
    {
        return new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = key,
            Value = SettingValueSerializer.Serialize(value),
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
