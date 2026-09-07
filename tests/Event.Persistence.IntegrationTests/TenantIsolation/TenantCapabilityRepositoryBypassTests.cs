using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class TenantCapabilityRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task TenantCapabilityResolution_WithAmbientTenant_ReturnsOnlyExplicitTenantCapabilities()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("capability-a");
        var tenantB = CreateTenant("capability-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var tenantAIslamic = CreateCapability(tenantA.Id, SeedIds.ModuleIslamicId, isEnabled: true);
        var tenantATech = CreateCapability(tenantA.Id, SeedIds.ModuleTechId, isEnabled: false);
        var tenantBIslamic = CreateCapability(tenantB.Id, SeedIds.ModuleIslamicId, isEnabled: false);
        var tenantBTech = CreateCapability(tenantB.Id, SeedIds.ModuleTechId, isEnabled: true);
        seedContext.TenantCapabilities.AddRange(tenantAIslamic, tenantATech, tenantBIslamic, tenantBTech);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.TenantCapabilities
            .AsNoTracking()
            .Select(capability => capability.TenantId)
            .Distinct()
            .ToListAsync();

        var repository = new TenantCapabilityRepository(tenantBContext);
        var tenantACapabilities = await repository.GetByTenantId(tenantA.Id);
        var tenantAEnabledCapabilities = await repository.GetEnabledByTenantId(tenantA.Id);
        var tenantAIslamicEnabled = await repository.IsModuleEnabled(tenantA.Id, "Mod_Islamic");
        var tenantATechEnabled = await repository.IsModuleEnabled(tenantA.Id, "Mod_Tech");
        var tenantAIslamicCapability = await repository.GetByTenantAndModuleKey(tenantA.Id, "Mod_Islamic");

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantB.Id]);
        await Assert.That(tenantACapabilities.Select(capability => capability.TenantId)).IsEquivalentTo([tenantA.Id, tenantA.Id]);
        await Assert.That(tenantACapabilities.Select(capability => capability.Module.ModuleKey))
            .IsEquivalentTo(["Mod_Islamic", "Mod_Tech"]);

        await Assert.That(tenantAEnabledCapabilities.Select(capability => capability.TenantId)).IsEquivalentTo([tenantA.Id]);
        await Assert.That(tenantAEnabledCapabilities.Single().Module.ModuleKey).IsEqualTo("Mod_Islamic");

        await Assert.That(tenantAIslamicEnabled).IsTrue();
        await Assert.That(tenantATechEnabled).IsFalse();
        await Assert.That(tenantAIslamicCapability).IsNotNull();
        await Assert.That(tenantAIslamicCapability!.TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(tenantAIslamicCapability.Module.ModuleKey).IsEqualTo("Mod_Islamic");
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            FullName = $"Tenant Capability {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static TenantCapability CreateCapability(Guid tenantId, Guid moduleId, bool isEnabled)
    {
        return new TenantCapability
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            ModuleId = moduleId,
            Module = null!,
            IsEnabled = isEnabled,
            EnabledAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
