using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Services;
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
        await using var seedContext = fixture.CreateDbContext();

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

        await using var global = await PostgresTenantApplicationTableIsolationTests.GlobalLookupAuthority.CreateAsync(fixture);
        var lookupSource = new TenantLookupSource(global.Factory);
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
    }

    [Test]
    public async Task CurrentDomainResolutionDoesNotRetainReplacedOrSuspendedReservations()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var tenant = CreateTenant("current-domain", TenantStatusEnum.Active);
        seed.Tenants.Add(tenant);
        await seed.SaveChangesAsync();
        var domain = CreateTenantSetting(tenant.Id, GovernanceSettingKeys.Domains.TenantCustomDomain,
            "current.example.test");
        seed.TenantSettingOverrides.AddRange(domain,
            CreateTenantSetting(tenant.Id, GovernanceSettingKeys.Domains.TenantSubdomain, "current"));
        await seed.SaveChangesAsync();
        await using var global = await PostgresTenantApplicationTableIsolationTests.GlobalLookupAuthority.CreateAsync(fixture);
        var source = new TenantLookupSource(global.Factory);
        await Assert.That(await source.GetCurrentDomainTenantAsync("CURRENT.example.test", true)).IsEqualTo<Guid?>(tenant.Id);
        await Assert.That(await source.GetCurrentDomainTenantAsync("current", false)).IsEqualTo<Guid?>(tenant.Id);
        await Assert.That(await source.GetCurrentDomainTenantAsync("unreserved.example.test", true)).IsNull();

        domain.Value = SettingValueSerializer.Serialize("replacement.example.test");
        await seed.SaveChangesAsync();
        await Assert.That(await source.GetCurrentDomainTenantAsync("current.example.test", true)).IsNull();
        await Assert.That(await source.GetCurrentDomainTenantAsync("replacement.example.test", true)).IsEqualTo<Guid?>(tenant.Id);

        tenant.TenantStatusId = (int)TenantStatusEnum.Suspended;
        await seed.SaveChangesAsync();
        await Assert.That(await source.GetCurrentDomainTenantAsync("replacement.example.test", true)).IsNull();
        await Assert.That(await source.GetCurrentDomainTenantAsync("current", false)).IsNull();
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
