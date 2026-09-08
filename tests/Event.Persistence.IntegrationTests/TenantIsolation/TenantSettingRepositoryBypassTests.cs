using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class TenantSettingRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ExactTenantSettingBypasses_WithAmbientTenant_ReturnAndMutateOnlyExplicitTenantRows()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("settings-a");
        var tenantB = CreateTenant("settings-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        const string sharedKey = "tenant.settings.shared";
        const string lockedKey = "tenant.settings.locked";
        const string removableKey = "tenant.settings.remove";

        seedContext.TenantSettingOverrides.AddRange(
            CreateSetting(tenantA.Id, sharedKey, "tenant-a-shared", isLocked: false),
            CreateSetting(tenantA.Id, lockedKey, "tenant-a-locked", isLocked: true),
            CreateSetting(tenantA.Id, removableKey, "tenant-a-remove", isLocked: false),
            CreateSetting(tenantB.Id, sharedKey, "tenant-b-shared", isLocked: false),
            CreateSetting(tenantB.Id, lockedKey, "tenant-b-locked", isLocked: true),
            CreateSetting(tenantB.Id, removableKey, "tenant-b-remove", isLocked: false));
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.TenantSettingOverrides
            .AsNoTracking()
            .Select(setting => setting.TenantId)
            .ToListAsync();

        var repository = new TenantSettingRepository(tenantBContext,
            new RelationalSettingMutationLock(tenantBContext, new EfCoreUnitOfWork(tenantBContext)));

        var tenantAShared = await repository.GetByTenantAndKey(tenantA.Id, sharedKey);
        var missingTenantASetting = await repository.GetByTenantAndKey(tenantA.Id, "tenant.settings.missing");
        var tenantASettings = await repository.GetAllForTenant(tenantA.Id);
        var tenantALockedSettings = await repository.GetLockedForTenant(tenantA.Id);

        Guid actorId = Guid.NewGuid();
        var lockedTenantAShared = await repository.LockAsync(tenantA.Id, sharedKey, actorId);
        var unlockedTenantALocked = await repository.UnlockAsync(tenantA.Id, lockedKey, actorId);
        var removedTenantASetting = await repository.RemoveOverrideAsync(tenantA.Id, removableKey);
        var removedMissingTenantASetting = await repository.RemoveOverrideAsync(tenantA.Id, "tenant.settings.missing");

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantB.Id, tenantB.Id, tenantB.Id]);
        await Assert.That(tenantAShared).IsNotNull();
        await Assert.That(tenantAShared!.TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(SettingValueSerializer.Deserialize(tenantAShared.Value, string.Empty)).IsEqualTo("tenant-a-shared");
        await Assert.That(missingTenantASetting).IsNull();
        await Assert.That(tenantASettings.Select(setting => setting.TenantId)).IsEquivalentTo([tenantA.Id, tenantA.Id, tenantA.Id]);
        await Assert.That(tenantASettings.Select(setting => setting.SettingKey))
            .IsEquivalentTo([sharedKey, lockedKey, removableKey]);
        await Assert.That(tenantALockedSettings.Select(setting => setting.SettingKey)).IsEquivalentTo([lockedKey]);

        await Assert.That(lockedTenantAShared).IsTrue();
        await Assert.That(unlockedTenantALocked).IsTrue();
        await Assert.That(removedTenantASetting).IsTrue();
        await Assert.That(removedMissingTenantASetting).IsFalse();

        await using var verificationContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var remainingSettings = await verificationContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(setting => setting.TenantId == tenantA.Id || setting.TenantId == tenantB.Id)
            .ToListAsync();

        var tenantARemaining = remainingSettings
            .Where(setting => setting.TenantId == tenantA.Id)
            .ToDictionary(setting => setting.SettingKey);
        var tenantBRemaining = remainingSettings
            .Where(setting => setting.TenantId == tenantB.Id)
            .ToDictionary(setting => setting.SettingKey);

        await Assert.That(tenantARemaining.Keys).IsEquivalentTo([sharedKey, lockedKey]);
        await Assert.That(tenantARemaining[sharedKey].IsLocked).IsTrue();
        await Assert.That(tenantARemaining[lockedKey].IsLocked).IsFalse();

        await Assert.That(tenantBRemaining.Keys).IsEquivalentTo([sharedKey, lockedKey, removableKey]);
        await Assert.That(tenantBRemaining[sharedKey].IsLocked).IsFalse();
        await Assert.That(tenantBRemaining[lockedKey].IsLocked).IsTrue();
        await Assert.That(tenantBRemaining[removableKey].IsLocked).IsFalse();

        var tenantBVisibleAfterMutations = await verificationContext.TenantSettingOverrides
            .AsNoTracking()
            .Select(setting => setting.TenantId)
            .ToListAsync();

        await Assert.That(tenantBVisibleAfterMutations).IsEquivalentTo([tenantB.Id, tenantB.Id, tenantB.Id]);
    }

    [Test]
    public async Task GetAllForTenant_WithAmbientTenant_ReturnsExplicitTenantRowsWithoutTracking()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("settings-read-a");
        var tenantB = CreateTenant("settings-read-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();
        seedContext.TenantSettingOverrides.AddRange(
            CreateSetting(tenantA.Id, "tenant.settings.read-a", "tenant-a-value", isLocked: false),
            CreateSetting(tenantB.Id, "tenant.settings.read-b", "tenant-b-value", isLocked: false));
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var repository = new TenantSettingRepository(tenantBContext,
            new RelationalSettingMutationLock(tenantBContext, new EfCoreUnitOfWork(tenantBContext)));

        var tenantASettings = await repository.GetAllForTenant(tenantA.Id);

        await Assert.That(tenantASettings.Select(setting => setting.TenantId)).IsEquivalentTo([tenantA.Id]);
        await Assert.That(tenantBContext.ChangeTracker.Entries<TenantSetting>()).IsEmpty();
    }

    [Test]
    public async Task GetByTenantAndKeys_WithAmbientTenant_ReturnsOnlyExactTenantKeySetWithoutTracking()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("settings-exact-a");
        var tenantB = CreateTenant("settings-exact-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();
        seedContext.TenantSettingOverrides.AddRange(
            CreateSetting(tenantA.Id, "location_privacy.allow_home_locations", "tenant-a-home", isLocked: false),
            CreateSetting(tenantA.Id, "location_privacy.unrequested", "tenant-a-unrequested", isLocked: false),
            CreateSetting(tenantB.Id, "location_privacy.allow_home_locations", "tenant-b-home", isLocked: false));
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var repository = new TenantSettingRepository(tenantBContext,
            new RelationalSettingMutationLock(tenantBContext, new EfCoreUnitOfWork(tenantBContext)));

        List<TenantSetting> settings = await repository.GetByTenantAndKeys(
            tenantA.Id,
            ["location_privacy.allow_home_locations", "location_privacy.missing"]);

        await Assert.That(settings.Select(setting => setting.TenantId)).IsEquivalentTo([tenantA.Id]);
        await Assert.That(settings.Select(setting => setting.SettingKey))
            .IsEquivalentTo(["location_privacy.allow_home_locations"]);
        await Assert.That(tenantBContext.ChangeTracker.Entries<TenantSetting>()).IsEmpty();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            FullName = $"Tenant Settings {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static TenantSetting CreateSetting(Guid tenantId, string key, string value, bool isLocked)
    {
        return new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = key,
            Value = SettingValueSerializer.Serialize(value),
            IsLocked = isLocked,
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
