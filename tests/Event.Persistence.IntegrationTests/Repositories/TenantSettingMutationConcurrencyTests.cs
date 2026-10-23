using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using System.Text.Json;
using Npgsql;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantSettingMutationConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    private const string SettingKey = GovernanceSettingKeys.Branding.DisplayName;

    [Test]
    public async Task SetValueAsync_WhenOverrideIsLocked_ChangesValueWithoutUnlocking()
    {
        Guid tenantId = await SeedAsync(tenantLocked: true);
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        await authenticated.RunAsync(tenantId, session => session.ValueAsync(SettingKey, "updated", "\"updated\"", async token =>
        {
            var repository = new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary);
            await repository.SetValueAsync(tenantId, SettingKey, "\"updated\"", token, session.ActorId);
            return true;
        }));

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(saved.Value).IsEqualTo("\"updated\"");
        await Assert.That(saved.IsLocked).IsTrue();
    }

    [Test]
    public async Task UpsertLockAsync_WhenSystemSettingExists_PreservesValueAndNonLockMetadata()
    {
        await fixture.ResetAsync();
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

        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        Guid actorId = authenticated.ActorId;
        await authenticated.RunAsync(null, session => session.LockAsync(SettingKey, true, async token =>
        {
            var repository = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);

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
            }, token);
            return true;
        }));

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
    public async Task MissingSystemSetting_RequiresOriginalValueBeforeLockAndPreservesItsMetadata()
    {
        await fixture.ResetAsync();
        const string key = GovernanceSettingKeys.Branding.LogoUrl;
        const string fallbackValue = "\"https://fallback.example.test/logo.svg\"";
        await using (var seed = fixture.CreateDbContext())
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authenticated.RunAsync(null,
            session => session.LockAsync(key, true, token =>
                new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary)
                    .UpsertLockAsync(new SystemSetting { SettingKey = key, Value = fallbackValue, IsLocked = true }, token))));
        await using (var absent = fixture.CreateDbContext())
            await Assert.That(await absent.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
        SystemSetting original = await authenticated.RunAsync(null, async session =>
        {
            var repository = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);
            await session.ValueAsync(key, "https://fallback.example.test/logo.svg", fallbackValue, token =>
                repository.UpsertAsync(new SystemSetting
                {
                    SettingKey = key,
                    Value = fallbackValue,
                    CreatedBy = session.ActorId
                }, token));
            var created = (await repository.GetByKey(key))!;
            await session.LockAsync(key, true, token => repository.UpsertLockAsync(new SystemSetting
            {
                SettingKey = key,
                Value = "\"not-the-original-value\"",
                ValueType = SettingValueType.Boolean,
                IsLocked = true,
                AllowedValues = "[true,false]",
                Description = "Replacement description",
                Category = "Replacement category",
                DisplayOrder = 99,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = session.ActorId,
                UpdatedBy = session.ActorId
            }, token));
            return created;
        });

        await using ExploreDbContext readContext = fixture.CreateDbContext();
        SystemSetting saved = await readContext.SystemSettings
            .AsNoTracking()
            .SingleAsync(setting => setting.SettingKey == key);

        await Assert.That(saved.Value).IsEqualTo(fallbackValue);
        await Assert.That(saved.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(saved.IsLocked).IsTrue();
        await Assert.That(saved.Id).IsEqualTo(original.Id);
        await Assert.That(saved.AllowedValues).IsEqualTo(original.AllowedValues);
        await Assert.That(saved.Description).IsEqualTo(original.Description);
        await Assert.That(saved.Category).IsEqualTo(original.Category);
        await Assert.That(saved.DisplayOrder).IsEqualTo(original.DisplayOrder);
        await Assert.That(saved.CreatedAt).IsEqualTo(original.CreatedAt);
        await Assert.That(saved.CreatedBy).IsEqualTo(authenticated.ActorId);
        await Assert.That(saved.UpdatedAt).IsNotNull();
        await Assert.That(saved.UpdatedBy).IsEqualTo(authenticated.ActorId);
    }

    [Test]
    public async Task LockAndUnlockAsync_WhenExpectedStateDoesNotMatch_ReturnFalseWithoutMutation()
    {
        Guid tenantId = await SeedAsync(tenantLocked: true);
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        var outcome = await authenticated.RunAsync(tenantId, async session =>
        {
            var repository = new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary);
            bool lockApplied = await session.LockAsync(SettingKey, true,
                token => repository.LockAsync(tenantId, SettingKey, session.ActorId, token));
            bool unlockApplied = await session.LockAsync(SettingKey, false,
                token => repository.UnlockAsync(tenantId, SettingKey, session.ActorId, token));
            TenantSetting afterUnlock = await ReadTenantSettingAsync(tenantId);
            bool secondUnlockApplied = await session.LockAsync(SettingKey, false,
                token => repository.UnlockAsync(tenantId, SettingKey, session.ActorId, token));
            TenantSetting afterNoChange = await ReadTenantSettingAsync(tenantId);
            await Assert.That(afterNoChange.UpdatedAt).IsEqualTo(afterUnlock.UpdatedAt);
            return (lockApplied, unlockApplied, secondUnlockApplied);
        });
        TenantSetting saved = await ReadTenantSettingAsync(tenantId);

        await Assert.That(outcome.lockApplied).IsFalse();
        await Assert.That(outcome.unlockApplied).IsTrue();
        await Assert.That(outcome.secondUnlockApplied).IsFalse();
        await Assert.That(saved.UpdatedBy).IsEqualTo(authenticated.ActorId);
        await Assert.That(saved.UpdatedAt).IsNotNull();
    }

    [Test]
    public async Task UpsertManyForTenantAsync_StampsActorOnUpdatesAndInserts()
    {
        Guid tenantId = await SeedAsync(tenantLocked: false);
        string updatedKey = AppearanceSettingDefinitions.DefaultThemeMode.Key;
        string insertedKey = PublicExperienceSettingDefinitions.EventCatalogLabel.Key;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = updatedKey,
                Value = "\"light\"",
                IsLocked = false
            });
            if (!await seed.PaidEventPolicyVersions.AnyAsync())
                seed.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
            await seed.SaveChangesAsync();
        }
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        Guid actorId = authenticated.ActorId;
        await authenticated.RunAsync(tenantId, async session =>
        {
            // The production tenant import is the owner of paired value/unlocked effects.
            // It invokes the real UpsertManyForTenantAsync, rather than lending a scalar
            // proof to a test-authored replacement batch.
            var package = new TenantConfigurationPackageV1Alpha2
            {
                Schema = TenantConfigurationPackageContractMetadata.SchemaId,
                ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
                Kind = TenantConfigurationPackageContractMetadata.Kind,
                Metadata = new()
                {
                    Name = "settings-audit",
                    Source = new() { TenantName = "settings-audit" }
                },
                Spec = new()
                {
                    DisplayName = "Setting Race Tenant",
                    Settings = new Dictionary<string, JsonElement>
                    {
                        [updatedKey] = JsonSerializer.SerializeToElement("dark"),
                        [insertedKey] = JsonSerializer.SerializeToElement("Updated catalog")
                    },
                    Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(),
                    LegalDocuments = new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>()
                }
            };
            var sessions = session.Services.GetRequiredService<ConfigurationImportSessionApplicationService>();
            var created = await sessions.CreateTenantAsync(tenantId,
                TenantConfigurationPackageSerializer.Serialize(package), CancellationToken.None);
            var request = new ConfigurationImportPreviewRequest
            {
                SelectedSectionKeys = ["tenant.settings"],
                Mappings = new Dictionary<string, string>(),
                ApplyMode = ConfigurationImportApplyMode.ApplySelected,
                GrantedApprovalCodes = []
            };
            var preview = await sessions.PreviewTenantAsync(tenantId, created.SessionId,
                created.AccessToken, request, CancellationToken.None);
            await Assert.That(preview.IsApplyReady).IsTrue();
            var result = await session.Services.GetRequiredService<ConfigurationImportApplyService>()
                .ApplyTenantAsync(tenantId, created.SessionId, created.AccessToken, request,
                    session.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                    null, null, CancellationToken.None);
            await Assert.That(result.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
            return true;
        });
        await using ExploreDbContext context = fixture.CreateDbContext();
        List<TenantSetting> saved = await context.TenantSettingOverrides
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(setting => setting.TenantId == tenantId)
            .ToListAsync();
        TenantSetting updated = saved.Single(setting => setting.SettingKey == updatedKey);
        TenantSetting inserted = saved.Single(setting => setting.SettingKey == insertedKey);
        await Assert.That(updated.Value).IsEqualTo("\"dark\"");
        await Assert.That(inserted.Value).IsEqualTo("\"Updated catalog\"");
        await Assert.That(updated.UpdatedBy).IsEqualTo(actorId);
        await Assert.That(updated.UpdatedAt).IsNotNull();
        await Assert.That(inserted.CreatedBy).IsEqualTo(actorId);
        await Assert.That(inserted.CreatedAt).IsNotEqualTo(default);
    }

    [Test]
    public async Task SetThenLock_AreSerializedWithoutLosingValueOrLock()
    {
        Guid tenantId = await SeedAsync(tenantLocked: false);
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        bool lockApplied = await RunFirstThenSecondAsync(
            authenticated, tenantId,
            session => session.ValueAsync(SettingKey, "raced", "\"raced\"", async token =>
            {
                await new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary)
                    .SetValueAsync(tenantId, SettingKey, "\"raced\"", token, session.ActorId);
                return true;
            }),
            session => session.LockAsync(SettingKey, true, token =>
                new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary)
                    .LockAsync(tenantId, SettingKey, session.ActorId, token)));

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(lockApplied).IsTrue();
        await Assert.That(saved.Value).IsEqualTo("\"raced\"");
        await Assert.That(saved.IsLocked).IsTrue();
    }

    [Test]
    public async Task SystemValueThenLock_AreSerializedWithoutLosingTheCommittedValue()
    {
        await fixture.ResetAsync();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        string? previousValue = await RunFirstThenSecondAsync(
            authenticated, null,
            session => session.ValueAsync(SettingKey, "raced", "\"raced\"", async token =>
            {
                var firstRepository = new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary);
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
                return true;
            }),
            session => session.LockAsync(SettingKey, true, token =>
                new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary).UpsertLockAsync(new SystemSetting
                {
                    SettingKey = SettingKey,
                    Value = "\"fallback\"",
                    ValueType = SettingValueType.String,
                    IsLocked = true,
                    UpdatedAt = DateTime.UtcNow
                }, token)));

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
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        bool unlockApplied = await RunFirstThenSecondAsync(
            authenticated, tenantId,
            session => session.ValueAsync(SettingKey, "raced", "\"raced\"", async token =>
            {
                await new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary)
                    .SetValueAsync(tenantId, SettingKey, "\"raced\"", token, session.ActorId);
                return true;
            }),
            session => session.LockAsync(SettingKey, false, token =>
                new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary)
                    .UnlockAsync(tenantId, SettingKey, session.ActorId, token)));

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(unlockApplied).IsTrue();
        await Assert.That(saved.Value).IsEqualTo("\"raced\"");
        await Assert.That(saved.IsLocked).IsFalse();
    }

    [Test]
    public async Task LockThenUnlock_AreSerializedByExpectedState()
    {
        Guid tenantId = await SeedAsync(tenantLocked: false);
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        bool unlockApplied = await RunFirstThenSecondAsync(
            authenticated, tenantId,
            session => session.LockAsync(SettingKey, true, async token =>
            {
                bool lockApplied = await new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary)
                    .LockAsync(tenantId, SettingKey, session.ActorId, token);
                await Assert.That(lockApplied).IsTrue();
                return true;
            }),
            session => session.LockAsync(SettingKey, false, token =>
                new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary)
                    .UnlockAsync(tenantId, SettingKey, session.ActorId, token)));

        TenantSetting saved = await ReadTenantSettingAsync(tenantId);
        await Assert.That(unlockApplied).IsTrue();
        await Assert.That(saved.IsLocked).IsFalse();
    }

    [Test]
    public async Task ExecuteManyAsync_WithReverseOrder_UsesOneStableWaitOrder()
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
            [GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, SettingKey],
            async token =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(token);
                return true;
            });
        await firstEntered.Task;
        Task<bool> secondTask = secondLock.ExecuteManyAsync(
            [SettingKey, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes],
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
            [GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, SettingKey],
            token => mutationLock.ExecuteManyAsync(
                [SettingKey, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes],
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
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(() => fixture.CreateDbContext(), fixture);
        BaseCommandResponse<Guid> result = await RunFirstThenSecondAsync(
            authenticated, tenantId,
            session => session.LockAsync(SettingKey, true, async token =>
            {
                await LockSystemSettingAsync(
                    new SystemSettingRepository(session.Context, session.MutationLock, session.Boundary), token);
                return true;
            }),
            session => ExecuteTenantMutationAsync(
                mutation, tenantId,
                new TenantSettingRepository(session.Context, session.MutationLock, session.Boundary),
                SystemSettingRepository.CreateReadOnly(session.Context, session.MutationLock),
                session.MutationLock, session.ActorId),
            firstIsInstance: true);

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
        AuthenticatedSettingsFixture authenticated,
        Guid? tenantId,
        Func<AuthenticatedSettingsFixture.Session, Task> firstMutation,
        Func<AuthenticatedSettingsFixture.Session, Task<T>> secondMutation,
        bool firstIsInstance = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstMutated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondBackend = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> firstTask = authenticated.RunAsync(firstIsInstance ? null : tenantId, session =>
            session.MutationLock.ExecuteOrderedGroupsAsync([[SettingKey]], async token =>
            {
                // The outer native session lock precedes observation and BEGIN. The second
                // command observes only after acquiring that same lock, never a stale proof.
                await firstMutation(session);
                firstMutated.SetResult();
                await releaseFirst.Task.WaitAsync(token);
                return true;
            }, deadline.Token));
        Task<T>? secondTask = null;
        try
        {
            await Task.WhenAny(firstMutated.Task, firstTask).WaitAsync(deadline.Token);
            if (!firstMutated.Task.IsCompleted)
                await firstTask;
            await firstMutated.Task.WaitAsync(deadline.Token);
            secondTask = authenticated.RunAsync(tenantId, async session =>
            {
                secondBackend.SetResult(await GetBackendPidAsync(session.Context));
                return await session.MutationLock.ExecuteOrderedGroupsAsync([[SettingKey]],
                    _ => secondMutation(session), deadline.Token);
            });
            await Task.WhenAny(secondBackend.Task, secondTask).WaitAsync(deadline.Token);
            if (!secondBackend.Task.IsCompleted)
                await secondTask;
            int secondBackendPid = await secondBackend.Task.WaitAsync(deadline.Token);
            await WaitUntilAdvisoryLockIsWaitingAsync(secondBackendPid, secondTask);
            await Assert.That(secondTask.IsCompleted).IsFalse();
        }
        finally
        {
            releaseFirst.TrySetResult();
            await firstTask.WaitAsync(deadline.Token);
        }
        return await secondTask!.WaitAsync(deadline.Token);
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
                await secondTask;
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

            await Task.Yield();
        }

        throw new TimeoutException(
            $"Backend {backendPid} did not report waiting on an advisory lock within 10 seconds.");
    }

    private static async Task LockSystemSettingAsync(
        ISystemSettingRepository repository,
        CancellationToken cancellationToken)
    {
        await repository.UpsertLockAsync(new SystemSetting
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
        ISettingMutationLock mutationLock,
        Guid actorId)
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
                        await tenantRepository.LockAsync(tenantId, SettingKey, actorId, cancellationToken);
                        break;
                    case TenantMutation.Unlock:
                        await tenantRepository.UnlockAsync(tenantId, SettingKey, actorId, cancellationToken);
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
