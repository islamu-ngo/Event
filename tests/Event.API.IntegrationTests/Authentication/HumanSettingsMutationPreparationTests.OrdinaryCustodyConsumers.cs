using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("instance-update")]
    [Arguments("instance-lock")]
    [Arguments("instance-unlock")]
    [Arguments("tenant-update")]
    [Arguments("tenant-reset")]
    [Arguments("tenant-lock")]
    [Arguments("tenant-unlock")]
    [Arguments("user-update")]
    [Arguments("user-reset")]
    public async Task GenuineOrdinaryHandlersRetainValueLockResetAndPostCommitPublication(string operation)
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedMixedTenantAdministratorAsync(fixture);
        const string key = "event_list.page_size";
        Guid systemId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            seed.SystemSettings.Add(new SystemSetting
            {
                Id = systemId,
                SettingKey = key,
                Value = "24",
                ValueType = SettingValueType.Integer,
                IsLocked = operation == "instance-unlock",
                Description = "Retained",
                Category = "EventList",
                AllowedValues = "[12,24]",
                DisplayOrder = 7,
                CreatedAt = created,
                CreatedBy = fixture.Binding.LocalSubjectId
            });
            seed.Set<TenantSetting>().Add(new TenantSetting
            {
                Id = tenantId,
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                SettingKey = key,
                Value = "24",
                IsLocked = operation == "tenant-unlock",
                CreatedAt = created,
                CreatedBy = fixture.Binding.LocalSubjectId
            });
            seed.UserPreferences.Add(new UserPreference
            {
                Id = userId,
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                SettingKey = key,
                Value = "24",
                CreatedAt = created,
                CreatedBy = fixture.Binding.LocalSubjectId
            });
            await seed.SaveChangesAsync();
        }
        var scope = operation.StartsWith("instance-", StringComparison.Ordinal) ? SettingScope.Instance
            : operation.StartsWith("tenant-", StringComparison.Ordinal) ? SettingScope.Tenant : SettingScope.User;
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(scope == SettingScope.Instance ? PolicyMutationScope.Instance
                : new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selected.Services;
        var notifications = new ConsumerNotifications(services.GetRequiredService<ExploreDbContext>());
        object sink = (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications];
        bool accepted = operation.EndsWith("-update", StringComparison.Ordinal)
            ? (await ActivatorUtilities.CreateInstance<UpdateSettingCommandHandler>(services, sink).ExecuteAsync(
                new() { Key = key, Scope = scope, Value = "12" }, CancellationToken.None)).IsSuccess
            : operation.EndsWith("-reset", StringComparison.Ordinal)
                ? (await ActivatorUtilities.CreateInstance<ResetSettingCommandHandler>(services, sink).ExecuteAsync(
                    new() { Key = key, Scope = scope }, CancellationToken.None)).IsSuccess
                : operation.EndsWith("-unlock", StringComparison.Ordinal)
                    ? (await ActivatorUtilities.CreateInstance<UnlockSettingCommandHandler>(services, sink).ExecuteAsync(
                        new() { Key = key, Scope = scope }, CancellationToken.None)).IsSuccess
                    : (await ActivatorUtilities.CreateInstance<LockSettingCommandHandler>(services, sink).ExecuteAsync(
                        new() { Key = key, Scope = scope }, CancellationToken.None)).IsSuccess;
        await Assert.That(accepted).IsTrue();
        await Assert.That(notifications.Published.Count).IsEqualTo(1);
        await Assert.That(notifications.Published[0].ActorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await using var observer = fixture.Native.CreateDatabase();
        var system = await observer.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        await Assert.That(system.Id).IsEqualTo(systemId);
        await Assert.That(system.Value).IsEqualTo(operation == "instance-update" ? "12" : "24");
        await Assert.That(system.IsLocked).IsEqualTo(operation == "instance-lock");
        await Assert.That(system.SettingValueTypeId).IsEqualTo((int)SettingValueType.Integer);
        await Assert.That(system.Description).IsEqualTo("Retained");
        await Assert.That(system.Category).IsEqualTo("EventList");
        await Assert.That(system.AllowedValues).IsEqualTo("[12,24]");
        await Assert.That(system.DisplayOrder).IsEqualTo(7);
        await Assert.That(system.CreatedAt).IsEqualTo(created);
        await Assert.That(system.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        var tenant = await observer.Set<TenantSetting>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId);
        if (operation == "tenant-reset") await Assert.That(tenant).IsNull();
        else
        {
            await Assert.That(tenant!.Value).IsEqualTo(operation == "tenant-update" ? "12" : "24");
            await Assert.That(tenant.IsLocked).IsEqualTo(operation == "tenant-lock");
            await Assert.That(tenant.CreatedAt).IsEqualTo(created);
            await Assert.That(tenant.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        }
        var user = await observer.UserPreferences.AsNoTracking().SingleOrDefaultAsync(row => row.Id == userId);
        if (operation == "user-reset") await Assert.That(user).IsNull();
        else
        {
            await Assert.That(user!.Value).IsEqualTo(operation == "user-update" ? "12" : "24");
            await Assert.That(user.CreatedAt).IsEqualTo(created);
            await Assert.That(user.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        }
    }

    [Test]
    [Arguments(BatchUpdateMode.Strict)]
    [Arguments(BatchUpdateMode.BestEffort)]
    public async Task OrdinaryPrivateCompletionRetainsStrictRollbackAndBestEffortSkippedSources(BatchUpdateMode mode)
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == "event_list.page_size").ExecuteDeleteAsync();
            seed.SystemSettings.Add(new SystemSetting
            {
                SettingKey = "event_list.page_size",
                Value = "24",
                ValueType = SettingValueType.Integer,
                IsLocked = true
            });
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var notifications = new ConsumerNotifications(services.GetRequiredService<ExploreDbContext>());
        object sink = (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications];
        var result = await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services, sink).ExecuteAsync(new()
        {
            Category = "EventList",
            Scope = SettingScope.User,
            Mode = mode,
            Values = new Dictionary<string, string>
            {
                ["event_list.page_size"] = "12",
                ["event_list.browse_mode"] = "infinite-scroll"
            }
        }, CancellationToken.None);
        await Assert.That(result.Success).IsEqualTo(mode == BatchUpdateMode.BestEffort);
        await using var observer = fixture.Native.CreateDatabase();
        var preferences = await observer.UserPreferences.AsNoTracking().Where(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId).ToListAsync();
        await Assert.That(preferences.Any(row => row.SettingKey == "event_list.page_size")).IsFalse();
        await Assert.That(preferences.SingleOrDefault(row => row.SettingKey == "event_list.browse_mode")?.Value)
            .IsEqualTo(mode == BatchUpdateMode.BestEffort ? "\"infinite-scroll\"" : null);
        await Assert.That(notifications.Published.Count).IsEqualTo(mode == BatchUpdateMode.BestEffort ? 1 : 0);
    }

    [Test]
    public async Task OrdinaryDataAndPrivateCompletionExpireAfterTerminalSourceAwait()
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.User }, "12");
        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task terminalEntered = entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        IOrdinarySettingsSource? escaped = null;
        var execution = preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            escaped = preparation.CurrentOrdinarySource;
            await services.GetRequiredService<IUserPreferenceRepository>().Create(new UserPreference
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                SettingKey = "event_list.page_size",
                Value = "12",
                CreatedBy = fixture.Binding.LocalSubjectId
            });
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async sourceToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), sourceToken);
            }, _ => { });
            return true;
        });
        try
        {
            await terminalEntered;
            fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId
            && row.UserId == fixture.Binding.LocalSubjectId && row.SettingKey == "event_list.page_size")).IsFalse();
        await Assert.That(preparation.CurrentOrdinarySource).IsNull();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            services.GetRequiredService<IOrdinarySettingsMutationStore>().RequireCompleteAsync(escaped!, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preparation.ExecutePreparedAsync(proof, (_, _) => Task.FromResult(true)));
    }

    [Test]
    public async Task StandaloneRepositoryCannotBorrowAnUnexecutedOrdinarySource()
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.User }, "12");
        await services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async _ =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<IUserPreferenceRepository>().Create(new UserPreference
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    SettingKey = "event_list.page_size",
                    Value = "12",
                    CreatedBy = fixture.Binding.LocalSubjectId
                }));
            return true;
        });
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId
            && row.UserId == fixture.Binding.LocalSubjectId && row.SettingKey == "event_list.page_size")).IsFalse();
    }

    [Test]
    public async Task OrdinaryMissingParentLockRemainsExcludedUntilPhysicalCommit()
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        const string key = "event_list.page_size";
        await using (var seed = fixture.Native.CreateDatabase())
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = key, Value = "12", Scope = SettingScope.User }, "12");
        int calls = 0;
        await preparation.ExecutePreparedAsync(proof, async (_, _) =>
        {
            calls++;
            await services.GetRequiredService<IUserPreferenceRepository>().Create(new UserPreference
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                SettingKey = key,
                Value = "12",
                CreatedBy = fixture.Binding.LocalSubjectId
            });
            await using var competing = fixture.Native.CreateDatabase();
            // This is a native lock-result assertion, not a sleep used to schedule a race.
            // The independent attempt is bounded and must return SQLITE_BUSY while the
            // actual owner still holds its original negative-source write reservation.
            competing.Database.SetCommandTimeout(1);
            competing.SystemSettings.Add(new SystemSetting
            {
                SettingKey = key,
                Value = "24",
                ValueType = SettingValueType.Integer,
                IsLocked = true
            });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                competing.SaveChangesAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            await Assert.That(failure!.InnerException is SqliteException { SqliteErrorCode: 5 }).IsTrue();
            return true;
        });
        await Assert.That(calls).IsEqualTo(1);
        await using var afterCommit = fixture.Native.CreateDatabase();
        afterCommit.SystemSettings.Add(new SystemSetting
        {
            SettingKey = key,
            Value = "24",
            ValueType = SettingValueType.Integer,
            IsLocked = true
        });
        await afterCommit.SaveChangesAsync();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key && row.IsLocked)).IsTrue();
        await Assert.That(await observer.UserPreferences.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId
            && row.UserId == fixture.Binding.LocalSubjectId && row.SettingKey == key && row.Value == "12")).IsTrue();
    }

    [Test]
    public async Task OrdinaryLockCannotCreateAValueOrAuditRowFromItsCaller()
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await SeedPlatformAdministratorAsync(fixture);
        const string key = "event_list.page_size";
        await using (var seed = fixture.Native.CreateDatabase())
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(new LockSettingCommand { Key = key, Scope = SettingScope.Instance });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await services.GetRequiredService<ISystemSettingRepository>().UpsertLockAsync(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = "999",
                IsLocked = true,
                CreatedBy = fixture.Binding.LocalSubjectId
            }, token);
            return true;
        }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
    }

    [Test]
    public async Task OrdinaryCallbackCannotHideAnotherRowBesideItsPrivatelyCompletedEffect()
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = "event_list.page_size", Value = "12", Scope = SettingScope.User }, "12");
        string other = $"unadmitted-{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await services.GetRequiredService<IUserPreferenceRepository>().Create(new UserPreference
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                SettingKey = "event_list.page_size",
                Value = "12",
                CreatedBy = fixture.Binding.LocalSubjectId
            });
            var context = services.GetRequiredService<ExploreDbContext>();
            context.SystemSettings.Add(new SystemSetting { SettingKey = other, Value = "unadmitted" });
            await context.SaveChangesAsync(token);
            return true;
        }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == other)).IsFalse();
        await Assert.That(await observer.UserPreferences.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId
            && row.UserId == fixture.Binding.LocalSubjectId && row.SettingKey == "event_list.page_size")).IsFalse();
    }
}
