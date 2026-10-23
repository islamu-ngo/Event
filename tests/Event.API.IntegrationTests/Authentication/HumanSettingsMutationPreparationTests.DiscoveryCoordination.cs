using System.Data.Common;
using System.Diagnostics;
using Event.Api.IntegrationTests.Fixtures;
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
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TenantSettingsWaitForDiscoveryBeforeBeginAndRetainCustodyThroughSettlement(bool mixed)
    {
        using var observation = new SettingsDiscoveryDiagnosticObserver();
        var mixedObservation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, mixedObservation));
        await SeedMixedTenantAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.BindTenantLease();
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"Events\"");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                "\"FullRegistrationAndAuth\"");
            await seed.TenantSettingOverrides.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                && (row.SettingKey == GovernanceSettingKeys.PublicExperience.EventCatalogLabel
                    || row.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode)).ExecuteDeleteAsync();
            seed.TenantSettingOverrides.AddRange(
                new TenantSetting
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
                    Value = "\"Events\"",
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    CreatedBy = fixture.Binding.LocalSubjectId
                },
                new TenantSetting
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                    Value = "\"FullRegistrationAndAuth\"",
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    CreatedBy = fixture.Binding.LocalSubjectId
                });
            await seed.SaveChangesAsync();
        }

        await ExerciseAsync(cancel: true);
        await ExerciseAsync(cancel: false);

        async Task ExerciseAsync(bool cancel)
        {
            await using var request = await Request.CreateAsync(fixture);
            await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(mixed ? PolicyMutationScope.MixedSettingsTenant(PlatformDefaults.DefaultTenantId)
                    : new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
            var services = selected.Services;
            var owner = services.GetRequiredService<ExploreDbContext>();
            // Direct handler invocation retains the same genuine Human owner that the
            // production command wrapper acquires before constructing this preparation.
            var human = services.GetRequiredService<Func<IHumanSecuritySourceGate>>()();
            await using var ownedHuman = !human.TryRequireCurrent() ? await human.AcquireAsync() : null;
            human.RequireCurrent();
            var before = await ReadImageAsync();
            var notifications = new DiscoveryCoordinationNotifications();
            object sink = (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications];
            var state = new SettingsDiscoveryAttempt(owner, pauseCommit: !cancel);
            observation.Current = state;
            await using var holder = fixture.Native.CreateDatabase();
            holder.BindTenantLease();
            await using var held = await new EventDiscoveryEnrollmentGate(holder).AcquireAsync();
            await using var contender = fixture.Native.CreateDatabase();
            contender.BindTenantLease();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<IAsyncDisposable>? competing = null;
            Task<bool>? attempt = null;
            bool observedAttempt = false;
            try
            {
                // The real synchronous diagnostic pauses the provider's commit path;
                // a separate task lets this test control it without concurrent owner use.
                attempt = Task.Run(() => ExecuteAsync(cancel ? "Cancelled label" : "Published label", cancellation.Token));
                await Task.WhenAny(state.Begin.Task, attempt,
                    Task.Delay(TimeSpan.FromMilliseconds(150), cancellation.Token));
                if (attempt.IsFaulted)
                {
                    observedAttempt = true;
                    await attempt;
                }
                bool beganWhileExcluded = state.Begin.Task.IsCompleted;
                bool plannedWhileExcluded = state.RevisionCommands != 0;
                bool completedWhileExcluded = attempt.IsCompleted;
                if (cancel)
                {
                    cancellation.Cancel();
                    observedAttempt = true;
                    await Assert.ThrowsAsync<OperationCanceledException>(() => attempt);
                    var after = await ReadImageAsync();
                    await Assert.That(before.Same(after)).IsTrue();
                    await Assert.That(notifications.Published.Count).IsEqualTo(0);
                    await held.DisposeAsync();
                    using var releaseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await using var released = await new EventDiscoveryEnrollmentGate(contender)
                        .AcquireAsync(releaseDeadline.Token);
                }
                else
                {
                    await held.DisposeAsync();
                    await Task.WhenAny(state.Committing.Task, attempt).WaitAsync(TimeSpan.FromSeconds(10));
                    if (attempt.IsCompleted)
                    {
                        observedAttempt = true;
                        await attempt;
                    }
                    await state.Committing.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    competing = new EventDiscoveryEnrollmentGate(contender).AcquireAsync(cancellation.Token);
                    await Task.WhenAny(competing, Task.Delay(TimeSpan.FromMilliseconds(150), cancellation.Token));
                    bool blockedAtSettlement = !competing.IsCompleted;
                    state.AllowCommit.TrySetResult();
                    observedAttempt = true;
                    await Assert.That(await attempt).IsTrue();
                    await using var released = await competing.WaitAsync(TimeSpan.FromSeconds(5));
                    var after = await ReadImageAsync();
                    await Assert.That(after.Settings.Single(row => row.Key == GovernanceSettingKeys.PublicExperience.EventCatalogLabel).Value)
                        .IsEqualTo("\"Published label\"");
                    await Assert.That(after.Settings.Single(row => row.Key == GovernanceSettingKeys.PublicExperience.VisitorAccessMode).Value)
                        .IsEqualTo(mixed ? "\"AnonymousOnly\"" : "\"FullRegistrationAndAuth\"");
                    await Assert.That(after.RevisionId).IsEqualTo(before.RevisionId);
                    await Assert.That(after.IdentityEpoch).IsEqualTo(before.IdentityEpoch);
                    await Assert.That(after.DisclosureEpoch).IsEqualTo(before.DisclosureEpoch + 1);
                    await Assert.That(notifications.Published.Count).IsEqualTo(mixed ? 2 : 1);
                    await Assert.That(state.HeldBeforeBegin && state.HeldBeforeCommit && state.HeldAfterCommit).IsTrue();
                    await Assert.That(blockedAtSettlement).IsTrue();
                }
                await Assert.That(beganWhileExcluded).IsFalse();
                await Assert.That(plannedWhileExcluded).IsFalse();
                await Assert.That(completedWhileExcluded).IsFalse();
                await Assert.That(state.OriginalOwnerPreserved).IsTrue();
                await Assert.That(owner.Database.CurrentTransaction).IsNull();
                await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(owner)).IsFalse();
                human.RequireCurrent();
            }
            finally
            {
                state.AllowCommit.TrySetResult();
                cancellation.Cancel();
                await held.DisposeAsync();
                try
                {
                    if (!observedAttempt && attempt is not null)
                    {
                        try { await attempt; }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                    }
                }
                finally
                {
                    if (competing is not null)
                    {
                        try { await using var released = await competing; }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                    }
                    observation.Current = null;
                }
            }

            async Task<bool> ExecuteAsync(string label, CancellationToken token)
            {
                if (!mixed)
                    return (await ActivatorUtilities.CreateInstance<UpdateSettingCommandHandler>(services, sink)
                        .ExecuteAsync(new UpdateSettingCommand
                        {
                            Key = GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
                            Scope = SettingScope.Tenant,
                            Value = label
                        }, token)).IsSuccess;
                var command = new UpdateSettingBatchCommand
                {
                    Category = "PublicExperience",
                    Scope = SettingScope.Tenant,
                    Mode = BatchUpdateMode.Strict,
                    Values = new Dictionary<string, string>
                    {
                        [GovernanceSettingKeys.PublicExperience.VisitorAccessMode] = "AnonymousOnly",
                        [GovernanceSettingKeys.PublicExperience.EventCatalogLabel] = label
                    }
                };
                mixedObservation.Command = command;
                return (await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services, sink)
                    .ExecuteAsync(command, token)).Success;
            }
        }

        async Task<SettingsDiscoveryImage> ReadImageAsync()
        {
            await using var reader = fixture.Native.CreateDatabase();
            var settings = await reader.TenantSettingOverrides.AsNoTracking()
                .IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                    && (row.SettingKey == GovernanceSettingKeys.PublicExperience.EventCatalogLabel
                        || row.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode))
                .OrderBy(row => row.SettingKey)
                .Select(row => new SettingsDiscoveryRow(row.Id, row.SettingKey, row.Value, row.IsLocked,
                    row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync();
            var revision = await reader.Set<EventDiscoveryRevision>().AsNoTracking()
                .IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                .SingleAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId);
            var audits = await reader.AuditLogs.AsNoTracking()
                .IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
            var outbox = await reader.OutboxMessages.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
            return new(settings, revision.Id, revision.IdentityEpoch, revision.DisclosureEpoch, audits, outbox);
        }
    }

    private sealed record SettingsDiscoveryRow(Guid Id, string Key, string Value, bool Locked,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);

    private sealed record SettingsDiscoveryImage(SettingsDiscoveryRow[] Settings, Guid RevisionId,
        long IdentityEpoch, long DisclosureEpoch, Guid[] Audits, Guid[] Outbox)
    {
        internal bool Same(SettingsDiscoveryImage other) => Settings.SequenceEqual(other.Settings)
            && RevisionId == other.RevisionId && IdentityEpoch == other.IdentityEpoch
            && DisclosureEpoch == other.DisclosureEpoch && Audits.SequenceEqual(other.Audits) && Outbox.SequenceEqual(other.Outbox);
    }

    private sealed class DiscoveryCoordinationNotifications : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class SettingsDiscoveryAttempt(ExploreDbContext owner, bool pauseCommit)
    {
        private readonly DbContextId contextId = owner.ContextId;
        private readonly DbConnection connector = owner.Database.GetDbConnection();
        private readonly Guid? selectedTenant = owner.TenantFilterTenantId;
        private readonly string revisionTable = owner.Model.FindEntityType(typeof(EventDiscoveryRevision))!.GetTableName()!;
        private int revisionCommands;
        internal TaskCompletionSource Begin { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Committing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool PauseCommit => pauseCommit;
        internal int RevisionCommands => Volatile.Read(ref revisionCommands);
        internal bool HeldBeforeBegin { get; set; }
        internal bool HeldBeforeCommit { get; set; }
        internal bool HeldAfterCommit { get; set; }
        internal bool OriginalOwnerPreserved { get; private set; } = true;
        internal bool IsOriginal(DbContext? context) => ReferenceEquals(context, owner);
        internal bool ObserveOwner(DbContext? context)
        {
            if (!IsOriginal(context)) return false;
            owner.RequireBoundTenantLease();
            OriginalOwnerPreserved &= owner.ContextId == contextId && owner.TenantFilterTenantId == selectedTenant
                && ReferenceEquals(owner.Database.GetDbConnection(), connector);
            return EventDiscoveryEnrollmentGate.TryRequireCurrent(owner);
        }
        internal void ObserveCommand(DbCommand command, DbContext? context)
        {
            // The exact model relation is disclosure planning, not unrelated principal,
            // route or authentication queries performed before setting observation.
            if (IsOriginal(context) && command.CommandText.Contains(revisionTable, StringComparison.Ordinal))
                Interlocked.Increment(ref revisionCommands);
        }
    }

    private sealed class SettingsDiscoveryDiagnosticObserver : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable allListeners;
        private readonly List<IDisposable> subscriptions = [];
        private bool disposed;

        internal SettingsDiscoveryDiagnosticObserver() => allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        internal SettingsDiscoveryAttempt? Current { get; set; }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name)
            {
                lock (subscriptions)
                    if (!disposed) subscriptions.Add(listener.Subscribe(this));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (Current is not { } current) return;
            if (value.Key == RelationalEventId.CommandExecuting.Name && value.Value is CommandEventData command)
            {
                current.ObserveCommand(command.Command, command.Context);
                return;
            }
            if (value.Key == RelationalEventId.TransactionStarting.Name
                && value.Value is TransactionStartingEventData starting && current.IsOriginal(starting.Context))
            {
                current.HeldBeforeBegin = current.ObserveOwner(starting.Context);
                current.Begin.TrySetResult();
            }
            else if (value.Key == RelationalEventId.TransactionCommitting.Name
                && value.Value is TransactionEventData committing && current.IsOriginal(committing.Context))
            {
                current.HeldBeforeCommit = current.ObserveOwner(committing.Context);
                current.Committing.TrySetResult();
                if (current.PauseCommit)
                    current.AllowCommit.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
            }
            else if (value.Key == RelationalEventId.TransactionCommitted.Name
                && value.Value is TransactionEndEventData committed && current.IsOriginal(committed.Context))
                current.HeldAfterCommit = current.ObserveOwner(committed.Context);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) => throw new InvalidOperationException("Settings discovery observation failed.", error);

        public void Dispose()
        {
            Current = null;
            allListeners.Dispose();
            lock (subscriptions)
            {
                disposed = true;
                foreach (var subscription in subscriptions) subscription.Dispose();
            }
        }
    }
}
