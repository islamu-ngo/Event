using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantSettingMutationConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    private const string SettingKey = GovernanceSettingKeys.Email.SmtpHost;
    private readonly Guid _actorId = Guid.NewGuid();

    [Test]
    public async Task SetValueAsync_WhenOverrideIsLocked_ChangesValueWithoutUnlocking()
    {
        Guid tenantId = await SeedAsync(tenantLocked: true);
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new TenantSettingRepository(context);

        await repository.SetValueAsync(tenantId, SettingKey, "\"updated\"");

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(saved.Value).IsEqualTo("\"updated\"");
        await Assert.That(saved.IsLocked).IsTrue();
    }

    [Test]
    public async Task UpsertLockAsync_WhenSystemSettingExists_PreservesValueAndNonLockMetadata()
    {
        await fixture.ResetAsync();
        var actorId = Guid.NewGuid();
        var originalCreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var originalUpdatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        Guid originalId;
        Guid originalCreatorId = Guid.NewGuid();

        await using (ExploreDbContext setupContext = fixture.CreateDbContext())
        {
            SystemSetting existing = await setupContext.SystemSettings.SingleAsync(setting => setting.SettingKey == SettingKey);
            existing.Value = "\"persisted\"";
            existing.ValueType = SettingValueType.String;
            existing.IsLocked = false;
            existing.AllowedValues = "[\"persisted\"]";
            existing.Description = "Persisted description";
            existing.Category = "Persisted category";
            existing.DisplayOrder = 99;
            existing.CreatedAt = originalCreatedAt;
            existing.CreatedBy = originalCreatorId;
            existing.UpdatedAt = originalUpdatedAt;
            existing.UpdatedBy = Guid.NewGuid();
            await setupContext.SaveChangesAsync();
            originalId = existing.Id;
        }

        await using (ExploreDbContext mutationContext = fixture.CreateDbContext())
        {
            var repository = new SystemSettingRepository(mutationContext, CreateMutationLock(mutationContext));

            await repository.UpsertLockAsync(new SystemSetting
            {
                SettingKey = SettingKey,
                Value = "\"must-not-replace-persisted-value\"",
                ValueType = SettingValueType.Boolean,
                IsLocked = true,
                AllowedValues = "[true,false]",
                Description = "Replacement description",
                Category = "Replacement category",
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = Guid.NewGuid(),
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = actorId
            }, CancellationToken.None);
        }

        await using ExploreDbContext readContext = fixture.CreateDbContext();
        SystemSetting saved = await readContext.SystemSettings
            .AsNoTracking()
            .SingleAsync(setting => setting.SettingKey == SettingKey);

        await Assert.That(saved.Id).IsEqualTo(originalId);
        await Assert.That(saved.Value).IsEqualTo("\"persisted\"");
        await Assert.That(saved.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(saved.AllowedValues).IsEqualTo("[\"persisted\"]");
        await Assert.That(saved.Description).IsEqualTo("Persisted description");
        await Assert.That(saved.Category).IsEqualTo("Persisted category");
        await Assert.That(saved.DisplayOrder).IsEqualTo(99);
        await Assert.That(saved.CreatedAt).IsEqualTo(originalCreatedAt);
        await Assert.That(saved.CreatedBy).IsEqualTo(originalCreatorId);
        await Assert.That(saved.IsLocked).IsTrue();
        await Assert.That(saved.UpdatedBy).IsEqualTo(actorId);
        await Assert.That(saved.UpdatedAt.HasValue).IsTrue();
        await Assert.That(saved.UpdatedAt!.Value).IsGreaterThan(originalUpdatedAt);
    }

    [Test]
    public async Task UpsertLockAsync_WhenSystemSettingIsMissing_InsertsFallbackValueAndMetadata()
    {
        await fixture.ResetAsync();
        const string key = "test.system-setting-lock-fallback";
        const string fallbackValue = "\"fallback\"";
        var actorId = Guid.NewGuid();
        var createdAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (ExploreDbContext mutationContext = fixture.CreateDbContext())
        {
            var repository = new SystemSettingRepository(mutationContext, CreateMutationLock(mutationContext));

            await repository.UpsertLockAsync(new SystemSetting
            {
                SettingKey = key,
                Value = fallbackValue,
                ValueType = SettingValueType.String,
                IsLocked = true,
                AllowedValues = "[\"fallback\"]",
                Description = "Fallback description",
                Category = "Fallback category",
                DisplayOrder = 7,
                CreatedAt = createdAt,
                CreatedBy = actorId,
                UpdatedAt = updatedAt,
                UpdatedBy = actorId
            }, CancellationToken.None);
        }

        await using ExploreDbContext readContext = fixture.CreateDbContext();
        SystemSetting saved = await readContext.SystemSettings
            .AsNoTracking()
            .SingleAsync(setting => setting.SettingKey == key);

        await Assert.That(saved.Value).IsEqualTo(fallbackValue);
        await Assert.That(saved.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(saved.IsLocked).IsTrue();
        await Assert.That(saved.AllowedValues).IsEqualTo("[\"fallback\"]");
        await Assert.That(saved.Description).IsEqualTo("Fallback description");
        await Assert.That(saved.Category).IsEqualTo("Fallback category");
        await Assert.That(saved.DisplayOrder).IsEqualTo(7);
        await Assert.That(saved.CreatedAt).IsEqualTo(createdAt);
        await Assert.That(saved.CreatedBy).IsEqualTo(actorId);
        await Assert.That(saved.UpdatedAt).IsEqualTo(updatedAt);
        await Assert.That(saved.UpdatedBy).IsEqualTo(actorId);
    }

    [Test]
    public async Task LockAndUnlockAsync_WhenExpectedStateDoesNotMatch_ReturnFalseWithoutMutation()
    {
        Guid tenantId = await SeedAsync(tenantLocked: true);
        Guid deniedLockActor = Guid.NewGuid();
        Guid unlockActor = Guid.NewGuid();
        Guid deniedUnlockActor = Guid.NewGuid();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new TenantSettingRepository(context);

        bool lockApplied = await repository.LockAsync(tenantId, SettingKey, deniedLockActor);
        bool unlockApplied = await repository.UnlockAsync(tenantId, SettingKey, unlockActor);
        bool secondUnlockApplied = await repository.UnlockAsync(tenantId, SettingKey, deniedUnlockActor);
        TenantSetting saved = await ReadTenantSettingAsync(tenantId);

        await Assert.That(lockApplied).IsFalse();
        await Assert.That(unlockApplied).IsTrue();
        await Assert.That(secondUnlockApplied).IsFalse();
        await Assert.That(saved.UpdatedBy).IsEqualTo(unlockActor);
        await Assert.That(saved.UpdatedAt).IsNotNull();
    }

    [Test]
    public async Task UpsertManyForTenantAsync_StampsActorOnUpdatesAndInserts()
    {
        Guid tenantId = await SeedAsync(tenantLocked: false);
        Guid actorId = Guid.NewGuid();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new TenantSettingRepository(context);

        await repository.UpsertManyForTenantAsync(
            tenantId,
            [
                new TenantSettingOverrideUpsert(SettingKey, "\"updated\"", true),
                new TenantSettingOverrideUpsert(GovernanceSettingKeys.Email.SmtpPort, "2525", false)
            ],
            actorId);

        List<TenantSetting> saved = await context.TenantSettingOverrides
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(setting => setting.TenantId == tenantId)
            .ToListAsync();
        TenantSetting updated = saved.Single(setting => setting.SettingKey == SettingKey);
        TenantSetting inserted = saved.Single(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpPort);
        await Assert.That(updated.UpdatedBy).IsEqualTo(actorId);
        await Assert.That(updated.UpdatedAt).IsNotNull();
        await Assert.That(inserted.CreatedBy).IsEqualTo(actorId);
        await Assert.That(inserted.CreatedAt).IsNotEqualTo(default);
    }

    [Test]
    public async Task SetThenLock_AreSerializedWithoutLosingValueOrLock()
    {
        Guid tenantId = await SeedAsync(tenantLocked: false);
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        var firstRepository = new TenantSettingRepository(firstContext);
        var secondRepository = new TenantSettingRepository(secondContext);
        RelationalSettingMutationLock firstLock = CreateMutationLock(firstContext);
        RelationalSettingMutationLock secondLock = CreateMutationLock(secondContext);

        bool lockApplied = await RunFirstThenSecondAsync(
            firstLock,
            secondContext,
            token => firstRepository.SetValueAsync(tenantId, SettingKey, "\"raced\"", token),
            () => secondLock.ExecuteAsync(
                SettingKey,
                token => secondRepository.LockAsync(tenantId, SettingKey, _actorId, token)));

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(lockApplied).IsTrue();
        await Assert.That(saved.Value).IsEqualTo("\"raced\"");
        await Assert.That(saved.IsLocked).IsTrue();
    }

    [Test]
    public async Task SystemValueThenLock_AreSerializedWithoutLosingTheCommittedValue()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        RelationalSettingMutationLock firstLock = CreateMutationLock(firstContext);
        RelationalSettingMutationLock secondLock = CreateMutationLock(secondContext);
        var firstRepository = new SystemSettingRepository(firstContext, firstLock);
        var secondRepository = new SystemSettingRepository(secondContext, secondLock);

        string? previousValue = await RunFirstThenSecondAsync(
            firstLock,
            secondContext,
            async token =>
            {
                await firstRepository.UpsertAsync(new SystemSetting
                {
                    SettingKey = SettingKey,
                    Value = "\"raced\"",
                    ValueType = SettingValueType.String,
                    IsLocked = false,
                    Description = "SMTP host",
                    Category = "Email",
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }, token);
            },
            () => secondRepository.UpsertLockAsync(new SystemSetting
            {
                SettingKey = SettingKey,
                Value = "\"fallback\"",
                ValueType = SettingValueType.String,
                IsLocked = true,
                UpdatedAt = DateTime.UtcNow
            }));

        await using ExploreDbContext readContext = fixture.CreateDbContext();
        SystemSetting saved = await readContext.SystemSettings
            .AsNoTracking()
            .SingleAsync(setting => setting.SettingKey == SettingKey);
        await Assert.That(previousValue).IsEqualTo("\"raced\"");
        await Assert.That(saved.Value).IsEqualTo("\"raced\"");
        await Assert.That(saved.IsLocked).IsTrue();
    }

    [Test]
    public async Task SetThenUnlock_AreSerializedWithoutRestoringStaleLockState()
    {
        Guid tenantId = await SeedAsync(tenantLocked: true);
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        var firstRepository = new TenantSettingRepository(firstContext);
        var secondRepository = new TenantSettingRepository(secondContext);
        RelationalSettingMutationLock firstLock = CreateMutationLock(firstContext);
        RelationalSettingMutationLock secondLock = CreateMutationLock(secondContext);

        bool unlockApplied = await RunFirstThenSecondAsync(
            firstLock,
            secondContext,
            token => firstRepository.SetValueAsync(tenantId, SettingKey, "\"raced\"", token),
            () => secondLock.ExecuteAsync(
                SettingKey,
                token => secondRepository.UnlockAsync(tenantId, SettingKey, _actorId, token)));

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(unlockApplied).IsTrue();
        await Assert.That(saved.Value).IsEqualTo("\"raced\"");
        await Assert.That(saved.IsLocked).IsFalse();
    }

    [Test]
    public async Task LockThenUnlock_AreSerializedByExpectedState()
    {
        Guid tenantId = await SeedAsync(tenantLocked: false);
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        var firstRepository = new TenantSettingRepository(firstContext);
        var secondRepository = new TenantSettingRepository(secondContext);
        RelationalSettingMutationLock firstLock = CreateMutationLock(firstContext);
        RelationalSettingMutationLock secondLock = CreateMutationLock(secondContext);

        bool unlockApplied = await RunFirstThenSecondAsync(
            firstLock,
            secondContext,
            async token =>
            {
                bool lockApplied = await firstRepository.LockAsync(tenantId, SettingKey, _actorId, token);
                await Assert.That(lockApplied).IsTrue();
            },
            () => secondLock.ExecuteAsync(
                SettingKey,
                token => secondRepository.UnlockAsync(tenantId, SettingKey, _actorId, token)));

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(unlockApplied).IsTrue();
        await Assert.That(saved.IsLocked).IsFalse();
    }

    [Test]
    public async Task ExecuteManyAsync_WithReverseOrder_UsesOneCanonicalWaitOrder()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        RelationalSettingMutationLock firstLock = CreateMutationLock(firstContext);
        RelationalSettingMutationLock secondLock = CreateMutationLock(secondContext);
        int secondBackendPid = await GetBackendPidAsync(secondContext);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<bool> firstTask = firstLock.ExecuteManyAsync(
            [GovernanceSettingKeys.Email.SmtpPort, SettingKey],
            async token =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(token);
                return true;
            });
        await firstEntered.Task;
        Task<bool> secondTask = secondLock.ExecuteManyAsync(
            [SettingKey, GovernanceSettingKeys.Email.SmtpPort],
            _ => Task.FromResult(true));

        await WaitUntilAdvisoryLockIsWaitingAsync(secondBackendPid, secondTask);
        await Assert.That(secondTask.IsCompleted).IsFalse();
        releaseFirst.SetResult();

        await Assert.That(await firstTask).IsTrue();
        await Assert.That(await secondTask).IsTrue();
    }

    [Test]
    public async Task ExecuteManyAsync_WhenNested_ReusesActiveTransaction()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        RelationalSettingMutationLock mutationLock = CreateMutationLock(context);

        bool result = await mutationLock.ExecuteManyAsync(
            [GovernanceSettingKeys.Email.SmtpPort, SettingKey],
            token => mutationLock.ExecuteManyAsync(
                [SettingKey, GovernanceSettingKeys.Email.SmtpPort],
                _ => Task.FromResult(true),
                token));

        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments(TenantMutation.Set, false)]
    [Arguments(TenantMutation.Lock, false)]
    [Arguments(TenantMutation.Unlock, true)]
    public async Task SystemLockThenTenantMutation_RecheckRejectsWithoutTenantMutation(
        TenantMutation mutation,
        bool tenantInitiallyLocked)
    {
        Guid tenantId = await SeedAsync(tenantInitiallyLocked);
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        RelationalSettingMutationLock firstLock = CreateMutationLock(firstContext);
        RelationalSettingMutationLock secondLock = CreateMutationLock(secondContext);
        var firstSystemRepository = new SystemSettingRepository(firstContext, firstLock);
        var secondSystemRepository = new SystemSettingRepository(secondContext, secondLock);
        var secondTenantRepository = new TenantSettingRepository(secondContext);

        BaseCommandResponse<Guid> result = await RunFirstThenSecondAsync(
            firstLock,
            secondContext,
            token => LockSystemSettingAsync(firstSystemRepository, token),
            () => ExecuteTenantMutationAsync(
                mutation,
                tenantId,
                secondTenantRepository,
                secondSystemRepository,
                secondLock));

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(result.FailureCode).IsEqualTo("setting_system_locked");
        await Assert.That(saved.Value).IsEqualTo("\"baseline\"");
        await Assert.That(saved.IsLocked).IsEqualTo(tenantInitiallyLocked);
    }

    private async Task<Guid> SeedAsync(bool tenantLocked)
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        SystemSetting systemSetting = await context.SystemSettings.SingleAsync(setting => setting.SettingKey == SettingKey);
        systemSetting.Value = "\"system\"";
        systemSetting.IsLocked = false;

        var tenant = new Tenant
        {
            FullName = "Setting Race Tenant",
            Slug = $"setting-race-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        context.TenantSettingOverrides.Add(new TenantSetting
        {
            TenantId = tenant.Id,
            Tenant = null!,
            SettingKey = SettingKey,
            Value = "\"baseline\"",
            IsLocked = tenantLocked
        });
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<TenantSetting> ReadTenantSettingAsync(Guid tenantId)
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        return await context.TenantSettingOverrides
            .AsNoTracking()
            .SingleAsync(setting => setting.TenantId == tenantId && setting.SettingKey == SettingKey);
    }

    private static RelationalSettingMutationLock CreateMutationLock(ExploreDbContext context) =>
        new(context, new EfCoreUnitOfWork(context));

    private async Task<T> RunFirstThenSecondAsync<T>(
        ISettingMutationLock firstLock,
        ExploreDbContext secondContext,
        Func<CancellationToken, Task> firstMutation,
        Func<Task<T>> secondMutation)
    {
        int secondBackendPid = await GetBackendPidAsync(secondContext);
        var firstMutated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> firstTask = firstLock.ExecuteAsync(
            SettingKey,
            async token =>
            {
                await firstMutation(token);
                firstMutated.SetResult();
                await releaseFirst.Task.WaitAsync(token);
                return true;
            });

        await firstMutated.Task;
        Task<T> secondTask = secondMutation();
        await WaitUntilAdvisoryLockIsWaitingAsync(secondBackendPid, secondTask);
        await Assert.That(secondTask.IsCompleted).IsFalse();
        releaseFirst.SetResult();
        await firstTask;
        return await secondTask;
    }

    private static async Task<int> GetBackendPidAsync(ExploreDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        object? result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task WaitUntilAdvisoryLockIsWaitingAsync(int backendPid, Task secondTask)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var observer = new NpgsqlConnection(fixture.ConnectionString);
        await observer.OpenAsync(timeout.Token);

        while (!timeout.IsCancellationRequested)
        {
            if (secondTask.IsCompleted)
            {
                throw new InvalidOperationException(
                    "The competing setting mutation completed before waiting on the advisory lock.");
            }

            await using var command = observer.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_locks
                    WHERE pid = @pid
                      AND locktype = 'advisory'
                      AND NOT granted)
                """;
            command.Parameters.AddWithValue("pid", backendPid);
            if (await command.ExecuteScalarAsync(timeout.Token) is true)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        throw new TimeoutException(
            $"Backend {backendPid} did not report waiting on an advisory lock within 10 seconds.");
    }

    private static async Task LockSystemSettingAsync(
        ISystemSettingRepository repository,
        CancellationToken cancellationToken)
    {
        await repository.UpsertAsync(new SystemSetting
        {
            SettingKey = SettingKey,
            Value = "\"system\"",
            ValueType = SettingValueType.String,
            IsLocked = true,
            Description = "SMTP host",
            Category = "Email",
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private Task<BaseCommandResponse<Guid>> ExecuteTenantMutationAsync(
        TenantMutation mutation,
        Guid tenantId,
        ITenantSettingRepository tenantRepository,
        ISystemSettingRepository systemRepository,
        ISettingMutationLock mutationLock)
        => mutationLock.ExecuteAsync(
            SettingKey,
            async cancellationToken =>
            {
                if (await systemRepository.IsLocked(SettingKey, cancellationToken))
                {
                    return BaseCommandResponse.Failure(
                        "setting_system_locked",
                        id: tenantId);
                }

                switch (mutation)
                {
                    case TenantMutation.Set:
                        await tenantRepository.SetValueAsync(
                            tenantId, SettingKey, "\"changed\"", cancellationToken);
                        break;
                    case TenantMutation.Lock:
                        await tenantRepository.LockAsync(tenantId, SettingKey, _actorId, cancellationToken);
                        break;
                    case TenantMutation.Unlock:
                        await tenantRepository.UnlockAsync(tenantId, SettingKey, _actorId, cancellationToken);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
                }

                return BaseCommandResponse.Success(tenantId);
            },
            CancellationToken.None);

    public enum TenantMutation
    {
        Set,
        Lock,
        Unlock
    }
}
