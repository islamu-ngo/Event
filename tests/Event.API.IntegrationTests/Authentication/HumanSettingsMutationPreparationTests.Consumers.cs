using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("user-update", "unchanged")]
    [Arguments("user-update", "binding-revoked")]
    [Arguments("user-update", "terminal-expiry")]
    [Arguments("user-batch", "unchanged")]
    [Arguments("user-batch", "binding-revoked")]
    [Arguments("user-batch", "terminal-expiry")]
    [Arguments("user-reset", "unchanged")]
    [Arguments("user-reset", "binding-revoked")]
    [Arguments("user-reset", "terminal-expiry")]
    [Arguments("instance-lock", "unchanged")]
    [Arguments("instance-lock", "binding-revoked")]
    [Arguments("instance-lock", "terminal-expiry")]
    [Arguments("instance-unlock", "unchanged")]
    [Arguments("instance-unlock", "binding-revoked")]
    [Arguments("instance-unlock", "terminal-expiry")]
    [Arguments("tenant-set", "unchanged")]
    [Arguments("tenant-set", "binding-revoked")]
    [Arguments("tenant-set", "terminal-expiry")]
    [Arguments("tenant-lock", "unchanged")]
    [Arguments("tenant-lock", "binding-revoked")]
    [Arguments("tenant-lock", "terminal-expiry")]
    [Arguments("tenant-unlock", "unchanged")]
    [Arguments("tenant-unlock", "binding-revoked")]
    [Arguments("tenant-unlock", "terminal-expiry")]
    public async Task OrdinaryConsumersCommitOnlyWithOriginalLiveOwner(string operation, string change)
    {
        var beforeOwner = new ConsumerBeforeOwnerTransaction();
        var terminal = new ConsumerBeforeSettlement();
        IInterceptor interceptor = change == "terminal-expiry" ? terminal : beforeOwner;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await SeedPlatformAdministratorAsync(fixture);
        const string key = "event_list.page_size";
        bool initiallyLocked = operation.EndsWith("-unlock", StringComparison.Ordinal);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var system = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (system is null)
            {
                system = new SystemSetting { SettingKey = key, Value = "24", ValueType = SettingValueType.Integer };
                seed.SystemSettings.Add(system);
            }
            system.Value = "24";
            system.IsLocked = operation.StartsWith("instance-", StringComparison.Ordinal) && initiallyLocked;
            if (operation.StartsWith("user-", StringComparison.Ordinal))
                seed.UserPreferences.Add(new UserPreference
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    SettingKey = key,
                    Value = "24",
                    CreatedBy = fixture.Binding.LocalSubjectId,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            if (operation.StartsWith("tenant-", StringComparison.Ordinal))
                seed.Set<TenantSetting>().Add(new TenantSetting
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    SettingKey = key,
                    Value = "24",
                    IsLocked = initiallyLocked,
                    CreatedBy = fixture.Binding.LocalSubjectId,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            await seed.SaveChangesAsync();
        }

        await using var request = await Request.CreateAsync(fixture);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(operation.StartsWith("instance-", StringComparison.Ordinal)
                ? PolicyMutationScope.Instance : new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selection.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var owner = services.GetRequiredService<ExploreDbContext>();
        var notifications = new ConsumerNotifications(owner);
        var values = new Dictionary<string, string>
        {
            [key] = "12",
            ["event_list.browse_mode"] = "infinite-scroll"
        };
        var batch = new UpdateSettingBatchCommand
        {
            Category = "EventList",
            Scope = SettingScope.User,
            Values = values
        };
        beforeOwner.Owner = owner;
        beforeOwner.BeforeStart = async token =>
        {
            // The caller's mutable dictionary is not the prepared command or its persisted value.
            values[key] = "999";
            if (change == "binding-revoked")
            {
                await using var revoker = fixture.Native.CreateDatabase();
                await revoker.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            }
        };
        terminal.Owner = owner;
        terminal.BeforeSettlement = () =>
        {
            var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>()
                .HumanLifetime!;
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
            {
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
                return Task.CompletedTask;
            }, _ => { });
        };

        var execution = ExecuteConsumerAsync();
        if (change == "unchanged")
            await Assert.That(await execution).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(change == "terminal-expiry" ? terminal.Observed : beforeOwner.Observed).IsTrue();

        // A separate connection observes settlement, not the callback's tracked entities.
        await using var observer = fixture.Native.CreateDatabase();
        bool committed = change == "unchanged";
        if (operation.StartsWith("user-", StringComparison.Ordinal))
        {
            var row = await observer.UserPreferences.AsNoTracking().SingleOrDefaultAsync(item =>
                item.TenantId == PlatformDefaults.DefaultTenantId && item.UserId == fixture.Binding.LocalSubjectId
                && item.SettingKey == key);
            if (operation == "user-reset" && committed)
                await Assert.That(row).IsNull();
            else
            {
                await Assert.That(row!.Value).IsEqualTo(committed ? "12" : "24");
                await Assert.That(row.UpdatedBy ?? row.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
            }
            if (operation == "user-batch")
            {
                var browse = await observer.UserPreferences.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.TenantId == PlatformDefaults.DefaultTenantId && item.UserId == fixture.Binding.LocalSubjectId
                    && item.SettingKey == "event_list.browse_mode");
                await Assert.That(browse?.Value).IsEqualTo(committed ? "\"infinite-scroll\"" : null);
            }
        }
        else if (operation.StartsWith("instance-", StringComparison.Ordinal))
        {
            var row = await observer.SystemSettings.AsNoTracking().SingleAsync(item => item.SettingKey == key);
            await Assert.That(row.Value).IsEqualTo("24");
            await Assert.That(row.IsLocked).IsEqualTo(committed ? !initiallyLocked : initiallyLocked);
            if (committed)
                await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        }
        else
        {
            var row = await observer.Set<TenantSetting>().AsNoTracking().SingleAsync(item =>
                item.TenantId == PlatformDefaults.DefaultTenantId && item.SettingKey == key);
            await Assert.That(row.Value).IsEqualTo(committed && operation == "tenant-set" ? "12" : "24");
            await Assert.That(row.IsLocked).IsEqualTo(committed && operation != "tenant-set"
                ? !initiallyLocked : initiallyLocked);
            if (committed)
                await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        }
        await Assert.That(notifications.Published.Count).IsEqualTo(committed ? operation == "user-batch" ? 2 : 1 : 0);
        await Assert.That(notifications.Published.All(item => item.ActorUserId == fixture.Binding.LocalSubjectId)).IsTrue();

        async Task<bool> ExecuteConsumerAsync()
        {
            object sink = (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications];
            return operation switch
            {
                "user-update" => (await ActivatorUtilities.CreateInstance<UpdateSettingCommandHandler>(services, sink)
                    .ExecuteAsync(new() { Key = key, Value = "12", Scope = SettingScope.User }, CancellationToken.None)).IsSuccess,
                "user-batch" => (await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services, sink)
                    .ExecuteAsync(batch, CancellationToken.None)).Success,
                "user-reset" => (await ActivatorUtilities.CreateInstance<ResetSettingCommandHandler>(services, sink)
                    .ExecuteAsync(new() { Key = key, Scope = SettingScope.User }, CancellationToken.None)).IsSuccess,
                "instance-lock" => (await ActivatorUtilities.CreateInstance<LockSettingCommandHandler>(services, sink)
                    .ExecuteAsync(new() { Key = key, Scope = SettingScope.Instance }, CancellationToken.None)).IsSuccess,
                "instance-unlock" => (await ActivatorUtilities.CreateInstance<UnlockSettingCommandHandler>(services, sink)
                    .ExecuteAsync(new() { Key = key, Scope = SettingScope.Instance }, CancellationToken.None)).IsSuccess,
                "tenant-set" => (await ActivatorUtilities.CreateInstance<SetInstanceTenantSettingCommandHandler>(services, sink)
                    .ExecuteAsync(new(PlatformDefaults.DefaultTenantId, key, "12"), CancellationToken.None)).IsSuccess,
                "tenant-lock" => (await ActivatorUtilities.CreateInstance<LockInstanceTenantSettingCommandHandler>(services, sink)
                    .ExecuteAsync(new(PlatformDefaults.DefaultTenantId, key), CancellationToken.None)).IsSuccess,
                "tenant-unlock" => (await ActivatorUtilities.CreateInstance<UnlockInstanceTenantSettingCommandHandler>(services, sink)
                    .ExecuteAsync(new(PlatformDefaults.DefaultTenantId, key), CancellationToken.None)).IsSuccess,
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }
    }

    [Test]
    [Arguments(BatchUpdateMode.Strict)]
    [Arguments(BatchUpdateMode.BestEffort)]
    public async Task OrdinaryBatchKeepsLockedMembersUnchangedAndSettlesBeforePublication(BatchUpdateMode mode)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == "event_list.page_size");
            if (setting is null)
            {
                setting = new SystemSetting
                {
                    SettingKey = "event_list.page_size",
                    Value = "24",
                    ValueType = SettingValueType.Integer
                };
                seed.SystemSettings.Add(setting);
            }
            setting.IsLocked = true;
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var notifications = new ConsumerNotifications(services.GetRequiredService<ExploreDbContext>());
        var handler = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications]);
        var response = await handler.ExecuteAsync(new()
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
        bool applied = mode == BatchUpdateMode.BestEffort;
        await Assert.That(response.Success).IsEqualTo(applied);
        await Assert.That(response.Results.Single(result => result.Key == "event_list.page_size").Applied).IsFalse();
        await Assert.That(response.Results.Single(result => result.Key == "event_list.browse_mode").Applied).IsEqualTo(applied);
        await using var observer = fixture.Native.CreateDatabase();
        var rows = await observer.UserPreferences.AsNoTracking().Where(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId).ToListAsync();
        await Assert.That(rows.Any(row => row.SettingKey == "event_list.page_size")).IsFalse();
        await Assert.That(rows.SingleOrDefault(row => row.SettingKey == "event_list.browse_mode")?.Value)
            .IsEqualTo(applied ? "\"infinite-scroll\"" : null);
        await Assert.That(notifications.Published.Count).IsEqualTo(applied ? 1 : 0);
    }

    [Test]
    public async Task OrdinaryResetWithoutOverrideReturnsFailureWithoutPublication()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var notifications = new ConsumerNotifications(services.GetRequiredService<ExploreDbContext>());
        var handler = ActivatorUtilities.CreateInstance<ResetSettingCommandHandler>(services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications]);
        var response = await handler.ExecuteAsync(new()
        {
            Key = "event_list.page_size",
            Scope = SettingScope.User
        }, CancellationToken.None);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(notifications.Published).IsEmpty();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AnyAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId
            && row.SettingKey == "event_list.page_size")).IsFalse();
    }

    private sealed class ConsumerBeforeOwnerTransaction : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal Func<CancellationToken, Task>? BeforeStart { get; set; }
        internal bool Observed { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner) && BeforeStart is { } callback)
            {
                BeforeStart = null;
                Observed = true;
                await callback(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ConsumerBeforeSettlement : DbCommandInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal Action? BeforeSettlement { get; set; }
        internal bool Observed { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            ObserveBusinessCommand(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            ObserveBusinessCommand(command, eventData);
            return ValueTask.FromResult(result);
        }

        private void ObserveBusinessCommand(DbCommand command, CommandExecutedEventData eventData)
        {
            if (ReferenceEquals(eventData.Context, Owner) && Owner?.Database.CurrentTransaction is not null
                && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is "event_list.page_size")
                && BeforeSettlement is { } callback)
            {
                BeforeSettlement = null;
                Observed = true;
                callback();
            }
        }
    }

    private sealed class ConsumerNotifications(ExploreDbContext owner) : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];

        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (owner.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("A consumer cannot publish its provisional callback result.");
            Published.Add(notification);
            return Task.CompletedTask;
        }
    }
}
