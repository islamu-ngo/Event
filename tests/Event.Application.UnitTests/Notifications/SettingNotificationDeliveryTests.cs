using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Notifications;

public sealed class SettingNotificationDeliveryTests
{
    private static SettingChangedNotification Notification() => new(
        GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "false", "true",
        SettingSource.TenantOverride, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);

    [Test]
    public async Task ApplicationComposition_InvalidatesBeforeAudit()
    {
        var trace = new List<string>();
        var resolver = Substitute.For<IHierarchicalSettingsResolver>();
        resolver.When(value => value.InvalidateCache(Arg.Any<SettingScope?>(), Arg.Any<Guid?>()))
            .Do(_ => trace.Add("invalidate"));
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(new ConfigurationBuilder().Build());
        services.AddSingleton(resolver);
        services.AddSingleton<ILogger<SettingAuditLogHandler>>(new AuditSink(() => trace.Add("audit")));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetServices<INotificationHandler<SettingChangedNotification>>()
            .HandleAsync(Notification(), CancellationToken.None);

        await Assert.That(string.Join(",", trace)).IsEqualTo("invalidate,invalidate,audit");
    }

    [Test]
    public async Task Delivery_AwaitsEachConsumerBeforeStartingNext()
    {
        var trace = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notification = Notification();
        using var cancellation = new CancellationTokenSource();
        INotificationHandler<SettingChangedNotification>[] handlers =
        [
            new Consumer(async (value, token) =>
            {
                await Assert.That(value).IsSameReferenceAs(notification);
                await Assert.That(token).IsEqualTo(cancellation.Token);
                trace.Add("first-enter");
                entered.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                trace.Add("first-exit");
            }),
            new Consumer((_, _) => { trace.Add("second"); return Task.CompletedTask; })
        ];

        Task delivery = DeliverAsync(handlers, notification, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(delivery.IsCompleted).IsFalse();
            await Assert.That(trace.SequenceEqual(["first-enter"])).IsTrue();
        }
        finally
        {
            release.TrySetResult();
            await delivery.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await Assert.That(trace.SequenceEqual(["first-enter", "first-exit", "second"])).IsTrue();
    }

    [Test]
    public async Task Delivery_PropagatesOriginalFirstFailureWithoutLaterConsumers()
    {
        var trace = new List<string>();
        var failure = new InvalidOperationException("consumer failure");
        INotificationHandler<SettingChangedNotification>[] handlers =
        [
            new Consumer((_, _) => { trace.Add("first"); return Task.CompletedTask; }),
            new Consumer((_, _) => { trace.Add("failure"); return Task.FromException(failure); }),
            new Consumer((_, _) => { trace.Add("forbidden"); return Task.CompletedTask; })
        ];
        Exception? observed = null;
        try { await DeliverAsync(handlers, Notification(), CancellationToken.None); }
        catch (Exception exception) { observed = exception; }
        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(trace.SequenceEqual(["first", "failure"])).IsTrue();
    }

    [Test]
    public async Task Delivery_ForwardsCancellationAndStopsAtCancelledConsumer()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool laterRan = false;
        INotificationHandler<SettingChangedNotification>[] handlers =
        [
            new Consumer(async (_, token) =>
            {
                using var subscription = token.Register(() => cancelled.TrySetCanceled(token));
                entered.SetResult();
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }),
            new Consumer((_, _) => { laterRan = true; return Task.CompletedTask; })
        ];
        Task delivery = DeliverAsync(handlers, Notification(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        OperationCanceledException? observed = null;
        try { await delivery.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException exception) { observed = exception; }
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(laterRan).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Upsert_CommitSurvivesDisconnectAndNotificationFailure(bool withMetadata)
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new CommittedSettings(() => cancellation.Cancel());
        var failure = new InvalidOperationException("post-commit consumer failure");
        var changed = new TaskCompletionSource<SettingChangedNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool laterRan = false;
        var consumer = new Consumer(async (notification, token) =>
        {
            await Assert.That(cancellation.IsCancellationRequested).IsTrue();
            await Assert.That(token.CanBeCanceled).IsFalse();
            await Assert.That((await repository.GetByKey(notification.Key, token))!.Value).IsEqualTo("true");
            changed.SetResult(notification);
            throw failure;
        });
        using var provider = new ServiceCollection()
            .AddSingleton<INotificationHandler<SettingChangedNotification>>(consumer)
            .AddSingleton<INotificationHandler<SettingChangedNotification>>(new Consumer((_, _) =>
            { laterRan = true; return Task.CompletedTask; }))
            .BuildServiceProvider();
        var service = new SettingUpsertService(repository,
            provider.GetServices<INotificationHandler<SettingChangedNotification>>(),
            Substitute.For<IPublicationPolicyMutationBoundary>(), Substitute.For<IEmailDeliverySettingsWriter>(),
            Substitute.For<IEventResourceSettingsWriter>());
        Exception? observed = null;
        try
        {
            if (withMetadata)
                await service.UpsertSystemSettingAsync(GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                    "true", SettingValueType.Boolean, false, "Federation", 0, "Discovery", cancellationToken: cancellation.Token);
            else
                await service.UpsertValueAsync(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "true", cancellationToken: cancellation.Token);
        }
        catch (Exception exception) { observed = exception; }

        await Assert.That(observed).IsSameReferenceAs(failure);
        var notification = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(notification.OldValue).IsEqualTo("false");
        await Assert.That(notification.NewValue).IsEqualTo("true");
        await Assert.That((await repository.GetByKey(notification.Key))!.Value).IsEqualTo("true");
        await Assert.That(laterRan).IsFalse();
    }

    [Test]
    public async Task PolicyDelivery_InvalidatesOnlyTheTargetScopeThroughApplicationComposition()
    {
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddDistributedMemoryCache();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        Guid tenant = Guid.CreateVersion7();
        Guid otherTenant = Guid.CreateVersion7();
        string targetKey = $"policy:tenant:{tenant}";
        string otherKey = $"policy:tenant:{otherTenant}";
        await cache.SetStringAsync(targetKey, "stale");
        await cache.SetStringAsync(otherKey, "other");
        await cache.SetStringAsync("policy:instance", "instance");
        await scope.ServiceProvider.GetServices<INotificationHandler<PolicyChangedNotification>>()
            .HandleAsync(new(SettingScope.Tenant, tenant, "operator", DateTimeOffset.UtcNow), CancellationToken.None);
        await Assert.That(await cache.GetStringAsync(targetKey)).IsNull();
        await Assert.That(await cache.GetStringAsync(otherKey)).IsEqualTo("other");
        await Assert.That(await cache.GetStringAsync("policy:instance")).IsEqualTo("instance");
    }

    [Test]
    public async Task Upsert_PreCommitCancellationDoesNotWriteOrNotify()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool committed = false;
        bool notified = false;
        var repository = new CommittedSettings(() => committed = true);
        var service = new SettingUpsertService(repository,
            [new Consumer((_, _) => { notified = true; return Task.CompletedTask; })],
            Substitute.For<IPublicationPolicyMutationBoundary>(), Substitute.For<IEmailDeliverySettingsWriter>(),
            Substitute.For<IEventResourceSettingsWriter>());
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.UpsertValueAsync(
            GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "true", cancellationToken: cancellation.Token));
        await Assert.That(committed).IsFalse();
        await Assert.That(notified).IsFalse();
        await Assert.That((await repository.GetByKey(GovernanceSettingKeys.Federation.AtprotoEventsEnabled))!.Value).IsEqualTo("false");
    }

    [Test]
    public async Task Delivery_EmptyConsumersDoNotInventCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await DeliverAsync([], Notification(), cancellation.Token);
    }

    private static async Task DeliverAsync(IEnumerable<INotificationHandler<SettingChangedNotification>> handlers,
        SettingChangedNotification notification, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers) services.AddSingleton(handler);
        using var provider = services.BuildServiceProvider();
        await provider.GetServices<INotificationHandler<SettingChangedNotification>>()
            .HandleAsync(notification, cancellationToken);
    }

    private sealed class Consumer(Func<SettingChangedNotification, CancellationToken, Task> consume)
        : INotificationHandler<SettingChangedNotification>
    {
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken) => consume(notification, cancellationToken);
    }

    private sealed class AuditSink(Action onAudit) : ILogger<SettingAuditLogHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => onAudit();
    }

    private sealed class CommittedSettings(Action onCommit) : ISystemSettingRepository
    {
        private SystemSetting _setting = new() { SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled, Value = "false" };
        public Task<SystemSetting?> GetByKey(string key, CancellationToken cancellationToken = default) => Task.FromResult<SystemSetting?>(_setting.SettingKey == key ? _setting : null);
        public Task<string?> UpsertAsync(SystemSetting setting, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string previous = _setting.Value;
            _setting = setting;
            onCommit();
            return Task.FromResult<string?>(previous);
        }
        public Task<string?> UpsertInCurrentTransactionAsync(SystemSetting setting, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> UpsertLockAsync(SystemSetting setting, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<SystemSetting>> GetAllSettings(string? category = null, CancellationToken cancellationToken = default) => Task.FromResult(new List<SystemSetting> { _setting });
        public Task<bool> IsLocked(string key, CancellationToken cancellationToken = default) => Task.FromResult(_setting.IsLocked);
    }
}
