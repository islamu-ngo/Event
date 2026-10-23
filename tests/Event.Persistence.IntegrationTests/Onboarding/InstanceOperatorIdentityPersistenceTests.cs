namespace Event.Persistence.IntegrationTests.Onboarding;

using Explore.Persistence.Services;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Exceptions;
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
public sealed class InstanceOperatorIdentityPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(SystemMutation.Upsert)]
    [Arguments(SystemMutation.UpsertLock)]
    public async Task GenericSystemSettingMutationsRejectOperatorIdentityKeyBeforeSave(SystemMutation mutation)
    {
        await fixture.ResetAsync();

        await using ExploreDbContext verificationContext = fixture.CreateDbContext();
        var operatorIdentity = verificationContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity)
            .Select(setting => new { setting.Id, setting.Value, setting.IsLocked, setting.UpdatedAt });
        var original = await operatorIdentity.SingleOrDefaultAsync();

        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(saveObserver);
        var repository = new SystemSettingRepository(
            context,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)),
            new HumanSettingsDataBoundary(new NoOperatorMutationServices()));
        var candidate = new SystemSetting
        {
            SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
            Value = "{}",
            ValueType = SettingValueType.Json,
            IsLocked = false,
            CreatedAt = DateTime.UtcNow
        };
        Func<Task> guardedMutation = mutation switch
        {
            SystemMutation.Upsert => () => repository.UpsertAsync(candidate),
            SystemMutation.UpsertLock => () => repository.UpsertLockAsync(candidate),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(guardedMutation);
        await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
        await Assert.That(context.ChangeTracker.Entries().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();

        await Assert.That(await operatorIdentity.SingleOrDefaultAsync()).IsEqualTo(original);
    }

    [Test]
    public async Task TenantSettingMutationsRejectOperatorIdentityKeyBeforeSave()
    {
        await fixture.ResetAsync();
        Guid tenantId;
        await using (ExploreDbContext seedContext = fixture.CreateDbContext())
        {
            var tenant = new Tenant
            {
                FullName = "Operator Identity Guard Tenant",
                Slug = $"operator-identity-{Guid.NewGuid():N}",
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
            var repository = new TenantSettingRepository(context,
                new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)),
                new HumanSettingsDataBoundary(new NoOperatorMutationServices()));

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SetValueAsync(
                tenantId,
                InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                "{}"));
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
        }

        await using ExploreDbContext verificationContext = fixture.CreateDbContext();
        await Assert.That(await verificationContext.TenantSettingOverrides
            .AsNoTracking()
            .AnyAsync(setting =>
                setting.TenantId == tenantId
                && setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity))
            .IsFalse();
    }

    [Test]
    public async Task ConcurrentSerializableIdentityWritesCommitOnceAndReturnTypedConflict()
    {
        await fixture.ResetAsync();
        bool created;
        string? originalValue;
        SettingValueType? originalValueType;
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            SystemSetting? identity = await seed.SystemSettings.SingleOrDefaultAsync(setting =>
                setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity);
            created = identity is null;
            originalValue = identity?.Value;
            originalValueType = identity?.ValueType;
            if (identity is null)
            {
                identity = new SystemSetting
                {
                    SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                    Value = "{}",
                    CreatedAt = DateTime.UtcNow
                };
                seed.SystemSettings.Add(identity);
            }
            identity.Value = "{}";
            identity.ValueType = SettingValueType.Json;
            await seed.SaveChangesAsync();
        }

        try
        {
            await using ExploreDbContext firstContext = fixture.CreateDbContext();
            await using ExploreDbContext secondContext = fixture.CreateDbContext();
            var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<Exception?> WriteAsync(
                ExploreDbContext context,
                string value,
                TaskCompletionSource ownRead,
                Task otherRead)
            {
                try
                {
                    await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(
                        async token =>
                        {
                            SystemSetting setting = await context.SystemSettings.SingleAsync(
                                item => item.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                                token);
                            ownRead.SetResult();
                            await otherRead.WaitAsync(TimeSpan.FromSeconds(10), token);
                            setting.Value = value;
                            await context.SaveChangesAsync(token);
                            return true;
                        });
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }

            Exception?[] results = await Task.WhenAll(
                WriteAsync(firstContext, "first", firstRead, secondRead.Task),
                WriteAsync(secondContext, "second", secondRead, firstRead.Task))
                .WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
            await Assert.That(results.Count(result => result is ConcurrencyConflictException))
                .IsEqualTo(1);

            await using ExploreDbContext verification = fixture.CreateDbContext();
            string? committed = await verification.SystemSettings
                .Where(setting => setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity)
                .Select(setting => setting.Value)
                .SingleAsync();
            await Assert.That(committed is "first" or "second").IsTrue();
        }
        finally
        {
            await using ExploreDbContext cleanup = fixture.CreateDbContext();
            SystemSetting identity = await cleanup.SystemSettings.SingleAsync(setting =>
                setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity);
            if (created)
            {
                cleanup.SystemSettings.Remove(identity);
            }
            else
            {
                identity.Value = originalValue!;
                identity.ValueType = originalValueType!.Value;
            }
            await cleanup.SaveChangesAsync();
        }
    }

    public enum SystemMutation
    {
        Upsert,
        UpsertLock
    }

    private sealed class NoOperatorMutationServices : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            throw new UnauthorizedAccessException("This native scope guard fixture cannot issue mutation authority.");
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
}
