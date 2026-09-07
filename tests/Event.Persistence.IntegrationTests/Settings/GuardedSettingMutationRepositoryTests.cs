namespace Event.Persistence.IntegrationTests.Settings;

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class GuardedSettingMutationRepositoryTests(PostgreSqlContainerFixture fixture)
{
    private static string GuardedKey => PublicationPolicySettingKeys.All[1];

    [Test]
    [Arguments(TenantMutation.Set)]
    [Arguments(TenantMutation.Remove)]
    [Arguments(TenantMutation.Lock)]
    [Arguments(TenantMutation.Unlock)]
    public async Task TenantMutationsRejectGuardedKeysBeforeSaveOrRowMutation(TenantMutation mutation)
    {
        await fixture.ResetAsync();
        Guid tenantId;
        bool initiallyLocked = mutation is TenantMutation.Unlock;
        await using (ExploreDbContext seedContext = fixture.CreateDbContext())
        {
            var tenant = new Tenant
            {
                FullName = "Guarded Setting Tenant",
                Slug = $"guarded-setting-{Guid.NewGuid():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            seedContext.Tenants.Add(tenant);
            await seedContext.SaveChangesAsync();
            tenantId = tenant.Id;
            seedContext.TenantSettingOverrides.Add(new TenantSetting
            {
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = GuardedKey,
                Value = "false",
                IsLocked = initiallyLocked
            });
            await seedContext.SaveChangesAsync();
        }

        var saveObserver = new SaveObserver();
        await using (ExploreDbContext context = fixture.CreateDbContext(saveObserver))
        {
            var repository = new TenantSettingRepository(context);
            Func<Task> guardedMutation = mutation switch
            {
                TenantMutation.Set => () => repository.SetValueAsync(tenantId, GuardedKey, "true"),
                TenantMutation.Remove => async () => { await repository.RemoveOverrideAsync(tenantId, GuardedKey); },
                TenantMutation.Lock => async () => { await repository.LockAsync(tenantId, GuardedKey, Guid.NewGuid()); },
                TenantMutation.Unlock => async () => { await repository.UnlockAsync(tenantId, GuardedKey, Guid.NewGuid()); },
                _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
            };

            await Assert.ThrowsAsync<InvalidOperationException>(guardedMutation);
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
        }

        await using ExploreDbContext verificationContext = fixture.CreateDbContext();
        TenantSetting saved = await verificationContext.TenantSettingOverrides
            .AsNoTracking()
            .SingleAsync(setting => setting.TenantId == tenantId && setting.SettingKey == GuardedKey);
        await Assert.That(saved.Value).IsEqualTo("false");
        await Assert.That(saved.IsLocked).IsEqualTo(initiallyLocked);
    }

    [Test]
    public async Task UpsertManyForTenantAsyncRejectsWholeBatchBeforeSaveOrRowMutation()
    {
        await fixture.ResetAsync();
        Guid tenantId;
        await using (ExploreDbContext seedContext = fixture.CreateDbContext())
        {
            var tenant = new Tenant
            {
                FullName = "Guarded Batch Tenant",
                Slug = $"guarded-batch-{Guid.NewGuid():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            seedContext.Tenants.Add(tenant);
            await seedContext.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        var saveObserver = new SaveObserver();
        await using (ExploreDbContext context = fixture.CreateDbContext(saveObserver))
        {
            var repository = new TenantSettingRepository(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertManyForTenantAsync(
                tenantId,
                [
                    new TenantSettingOverrideUpsert("email.smtp_port", "2525", false),
                    new TenantSettingOverrideUpsert(GuardedKey, "true", true)
                ],
                Guid.NewGuid()));

            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
        }

        await using ExploreDbContext verificationContext = fixture.CreateDbContext();
        int savedCount = await verificationContext.TenantSettingOverrides
            .CountAsync(setting => setting.TenantId == tenantId);
        await Assert.That(savedCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(SystemMutation.Upsert)]
    [Arguments(SystemMutation.UpsertLock)]
    public async Task SystemMutationsRejectGuardedKeysBeforeLockSaveOrRowMutation(SystemMutation mutation)
    {
        await fixture.ResetAsync();
        await using ExploreDbContext seedContext = fixture.CreateDbContext();
        SystemSetting original = await seedContext.SystemSettings
            .AsNoTracking()
            .SingleAsync(setting => setting.SettingKey == GuardedKey);

        var saveObserver = new SaveObserver();
        await using (ExploreDbContext context = fixture.CreateDbContext(saveObserver))
        {
            var repository = new SystemSettingRepository(context, RejectingMutationLock.Instance);
            var candidate = new SystemSetting
            {
                SettingKey = GuardedKey,
                Value = "true",
                ValueType = SettingValueType.Boolean,
                IsLocked = !original.IsLocked,
                Category = "Changed",
                Description = "Changed",
                DisplayOrder = 999,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = Guid.NewGuid()
            };
            Func<Task> guardedMutation = mutation switch
            {
                SystemMutation.Upsert => async () => { await repository.UpsertAsync(candidate); },
                SystemMutation.UpsertLock => async () => { await repository.UpsertLockAsync(candidate); },
                _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
            };

            await Assert.ThrowsAsync<InvalidOperationException>(guardedMutation);
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
        }

        await using ExploreDbContext verificationContext = fixture.CreateDbContext();
        SystemSetting saved = await verificationContext.SystemSettings
            .AsNoTracking()
            .SingleAsync(setting => setting.SettingKey == GuardedKey);
        await Assert.That(saved.Value).IsEqualTo(original.Value);
        await Assert.That(saved.IsLocked).IsEqualTo(original.IsLocked);
        await Assert.That(saved.Category).IsEqualTo(original.Category);
        await Assert.That(saved.Description).IsEqualTo(original.Description);
        await Assert.That(saved.DisplayOrder).IsEqualTo(original.DisplayOrder);
    }

    public enum TenantMutation
    {
        Set,
        Remove,
        Lock,
        Unlock
    }

    public enum SystemMutation
    {
        Upsert,
        UpsertLock
    }

    private sealed class SaveObserver : SaveChangesInterceptor
    {
        public int SaveCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RejectingMutationLock : ISettingMutationLock
    {
        internal static readonly RejectingMutationLock Instance = new();

        public Task<T> ExecuteAsync<T>(
            string canonicalSettingKey,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            throw new MutationLockReachedException();

        public Task<T> ExecuteManyAsync<T>(
            IEnumerable<string> canonicalSettingKeys,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            throw new MutationLockReachedException();
    }

    private sealed class MutationLockReachedException : Exception;
}
