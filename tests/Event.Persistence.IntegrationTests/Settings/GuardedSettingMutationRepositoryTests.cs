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
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(
            () => fixture.CreateDbContext(), fixture, saveObserver);
        await authenticated.RunAsync(tenantId, async session =>
        {
            var context = session.Context;
            var repository = new TenantSettingRepository(context, session.MutationLock, session.Boundary);
            async Task<bool> Check(CancellationToken token)
            {
                Func<Task> guardedMutation = mutation switch
                {
                    TenantMutation.Set => () => repository.SetValueAsync(tenantId, GuardedKey, "true", token, session.ActorId),
                    TenantMutation.Remove => () => repository.RemoveOverrideAsync(tenantId, GuardedKey, token),
                    TenantMutation.Lock => () => repository.LockAsync(tenantId, GuardedKey, session.ActorId, token),
                    TenantMutation.Unlock => () => repository.UnlockAsync(tenantId, GuardedKey, session.ActorId, token),
                    _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
                };
                var rejected = await Assert.ThrowsAsync<InvalidOperationException>(guardedMutation);
                await Assert.That(rejected!.Message).Contains("publication-policy");
                return true;
            }
            _ = mutation switch
            {
                TenantMutation.Set => await session.ValueAsync(GuardedKey, "true", "true", Check),
                TenantMutation.Remove => await session.RemoveAsync(GuardedKey, Check),
                TenantMutation.Lock => await session.LockAsync(GuardedKey, true, Check),
                TenantMutation.Unlock => await session.LockAsync(GuardedKey, false, Check),
                _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
            };
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
            return true;
        });

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
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(
            () => fixture.CreateDbContext(), fixture, saveObserver);
        await authenticated.RunAsync(tenantId, session => session.ValueAsync(GuardedKey, "true", "true", async token =>
        {
            var context = session.Context;
            var repository = new TenantSettingRepository(context, session.MutationLock, session.Boundary);
            await session.Boundary.RequireAsync(context, Explore.Domain.Settings.SettingScope.Tenant,
                tenantId, null, GuardedKey, HumanSettingsMutationKind.SetValue, "true",
                auditActorId: session.ActorId, cancellationToken: token);
            var guarded = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SetValueAsync(tenantId, GuardedKey, "true", token, session.ActorId));
            await Assert.That(guarded!.Message).Contains("publication-policy");
            // Tenant publication effects never carry a lock grant. A real value proof
            // cannot be widened into this replacement batch, even by its own actor.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.UpsertManyForTenantAsync(
                tenantId,
                [
                    new TenantSettingOverrideUpsert(GuardedKey, "true", true),
                    new TenantSettingOverrideUpsert("email.smtp_port", "2525", false)
                ],
                session.ActorId, token));

            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
            return true;
        }));

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
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(
            () => fixture.CreateDbContext(), fixture, saveObserver);
        await authenticated.RunAsync(null, async session =>
        {
            var context = session.Context;
            var repository = new SystemSettingRepository(context, RejectingMutationLock.Instance, session.Boundary);
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
                UpdatedBy = session.ActorId
            };
            Func<Task> guardedMutation = mutation switch
            {
                SystemMutation.Upsert => async () => { await repository.UpsertAsync(candidate); }
                ,
                SystemMutation.UpsertLock => async () => { await repository.UpsertLockAsync(candidate); }
                ,
                _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
            };

            async Task<bool> Check(CancellationToken token)
            {
                var rejected = await Assert.ThrowsAsync<InvalidOperationException>(guardedMutation);
                await Assert.That(rejected!.Message).Contains("publication-policy");
                return true;
            }
            if (mutation == SystemMutation.Upsert)
                await session.ValueAsync(GuardedKey, "true", "true", Check);
            else
                await session.LockAsync(GuardedKey, candidate.IsLocked, Check);
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
            return true;
        });

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
            string settingKey,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            throw new MutationLockReachedException();

        public Task<T> ExecuteManyAsync<T>(
            IEnumerable<string> settingKeys,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            throw new MutationLockReachedException();
    }

    private sealed class MutationLockReachedException : Exception;
}
