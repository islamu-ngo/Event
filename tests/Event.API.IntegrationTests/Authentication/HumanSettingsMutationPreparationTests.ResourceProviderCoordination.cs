using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SettingsOrderedLockCancellationClosesOriginalPreparationBeforeScopeDisposal(bool mixed)
    {
        var mixedObservation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, mixedObservation));
        await SeedMixedTenantAdministratorAsync(fixture);
        string labelKey = GovernanceSettingKeys.PublicExperience.EventCatalogLabel;
        string visitorKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.BindTenantLease();
            await SeedSpecializedSettingAsync(seed, labelKey, "\"Events\"");
            await SeedSpecializedSettingAsync(seed, visitorKey, "\"FullRegistrationAndAuth\"");
            await seed.TenantSettingOverrides.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                && (row.SettingKey == labelKey || row.SettingKey == visitorKey)).ExecuteDeleteAsync();
            seed.TenantSettingOverrides.AddRange(
                new TenantSetting
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    SettingKey = labelKey,
                    Value = "\"Events\"",
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    CreatedBy = fixture.Binding.LocalSubjectId
                },
                new TenantSetting
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    SettingKey = visitorKey,
                    Value = "\"FullRegistrationAndAuth\"",
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    CreatedBy = fixture.Binding.LocalSubjectId
                });
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(mixed ? PolicyMutationScope.MixedSettingsTenant(PlatformDefaults.DefaultTenantId)
                : new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selected.Services;
        var original = services.GetRequiredService<ExploreDbContext>();
        var human = services.GetRequiredService<Func<IHumanSecuritySourceGate>>()();
        await using var ownedHuman = human.TryRequireCurrent() ? null : await human.AcquireAsync();
        var before = await ReadImageAsync();
        var notifications = new DiscoveryCoordinationNotifications();
        object sink = (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications];
        using var observation = new SettingsDiscoveryDiagnosticObserver();
        var state = new SettingsDiscoveryAttempt(original, pauseCommit: false);
        observation.Current = state;
        var lockHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var blockerContext = fixture.Native.CreateDatabase();
        blockerContext.BindTenantLease();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        Task<bool> blocker = new RelationalSettingMutationLock(blockerContext, new EfCoreUnitOfWork(blockerContext))
            .ExecuteOrderedGroupsAsync([[labelKey]], async token =>
            {
                lockHeld.TrySetResult();
                await releaseLock.Task.WaitAsync(token);
                return true;
            }, deadline.Token);
        Task<bool>? operation = null;
        bool observed = false;
        try
        {
            await lockHeld.Task.WaitAsync(deadline.Token);
            var actualLock = new RelationalSettingMutationLock(original, services.GetRequiredService<IUnitOfWork>(),
                (key, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    human.RequireCurrent();
                    EventDiscoveryEnrollmentGate.RequireCurrent(original);
                    if (original.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("Settings began before their ordered lock.");
                    if (StringComparer.OrdinalIgnoreCase.Equals(key, labelKey)) reachedLock.TrySetResult();
                    return Task.CompletedTask;
                });
            operation = ExecuteAsync(actualLock, cancellation.Token);
            await Task.WhenAny(reachedLock.Task, operation).WaitAsync(deadline.Token);
            if (operation.IsCompleted)
            {
                observed = true;
                await operation;
            }
            await reachedLock.Task.WaitAsync(deadline.Token);
            await Assert.That(operation.IsCompleted).IsFalse();
            cancellation.Cancel();
            observed = true;
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            await Assert.That(state.Begin.Task.IsCompleted).IsFalse();
            await Assert.That(original.Database.CurrentTransaction).IsNull();
            await Assert.That(before.Same(await ReadImageAsync())).IsTrue();
            await Assert.That(notifications.Published.Count).IsEqualTo(0);
            await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(original)).IsFalse();
            human.RequireCurrent();
            await using var contender = fixture.Native.CreateDatabase();
            contender.BindTenantLease();
            using var releaseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var acquired = await new EventDiscoveryEnrollmentGate(contender).AcquireAsync(releaseDeadline.Token);
            EventDiscoveryEnrollmentGate.RequireCurrent(contender);
            await Assert.That(blocker.IsCompleted).IsFalse();
        }
        finally
        {
            cancellation.Cancel();
            releaseLock.TrySetResult();
            try
            {
                if (!observed && operation is not null)
                {
                    try { await operation; }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                }
            }
            finally { await blocker; observation.Current = null; }
        }

        async Task<bool> ExecuteAsync(ISettingMutationLock actualLock, CancellationToken token)
        {
            if (!mixed)
                return (await ActivatorUtilities.CreateInstance<UpdateSettingCommandHandler>(services, actualLock, sink)
                    .ExecuteAsync(new UpdateSettingCommand { Key = labelKey, Scope = SettingScope.Tenant, Value = "Cancelled label" }, token)).IsSuccess;
            var command = new UpdateSettingBatchCommand
            {
                Category = "PublicExperience",
                Scope = SettingScope.Tenant,
                Mode = BatchUpdateMode.Strict,
                Values = new Dictionary<string, string> { [labelKey] = "Cancelled label", [visitorKey] = "AnonymousOnly" }
            };
            mixedObservation.Command = command;
            return (await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services, actualLock, sink)
                .ExecuteAsync(command, token)).Success;
        }

        async Task<SettingsDiscoveryImage> ReadImageAsync()
        {
            await using var reader = fixture.Native.CreateDatabase();
            reader.BindTenantLease();
            var settings = await reader.TenantSettingOverrides.AsNoTracking()
                .IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                    && (row.SettingKey == labelKey || row.SettingKey == visitorKey)).OrderBy(row => row.SettingKey)
                .Select(row => new SettingsDiscoveryRow(row.Id, row.SettingKey, row.Value, row.IsLocked,
                    row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync();
            var revision = await reader.Set<EventDiscoveryRevision>().AsNoTracking()
                .IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                .SingleAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId);
            var audits = await reader.AuditLogs.AsNoTracking().IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
            var outbox = await reader.OutboxMessages.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
            return new(settings, revision.Id, revision.IdentityEpoch, revision.DisclosureEpoch, audits, outbox);
        }
    }

    [Test]
    public async Task ResourceProviderCancellationBeforeOrderedLockReleasesPreparedDiscoveryBeforeScopeDisposal()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var original = services.GetRequiredService<ExploreDbContext>();
        var human = services.GetRequiredService<Func<IHumanSecuritySourceGate>>()();
        await using var ownedHuman = human.TryRequireCurrent() ? null : await human.AcquireAsync();
        human.RequireCurrent();
        Guid deployment = Guid.CreateVersion7();
        string before = await ReadImageAsync();
        var lockHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var blockerContext = fixture.Native.CreateDatabase();
        blockerContext.BindTenantLease();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        Task<bool> blocker = new RelationalSettingMutationLock(blockerContext, new EfCoreUnitOfWork(blockerContext))
            .ExecuteOrderedGroupsAsync([[EventResourceProviderBindingDocument.SettingKey]], async token =>
            {
                lockHeld.TrySetResult();
                await releaseLock.Task.WaitAsync(token);
                return true;
            }, deadline.Token);
        Task<EventResourceProviderOperation>? operation = null;
        bool observed = false;
        try
        {
            await lockHeld.Task.WaitAsync(deadline.Token);
            var actualLock = new RelationalSettingMutationLock(original, services.GetRequiredService<IUnitOfWork>(),
                (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    human.RequireCurrent();
                    EventDiscoveryEnrollmentGate.RequireCurrent(original);
                    if (original.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("Resource preparation began a transaction before ordered locks.");
                    reachedLock.TrySetResult();
                    return Task.CompletedTask;
                });
            var controlPlane = ActivatorUtilities.CreateInstance<EventResourceProviderControlPlane>(services, actualLock);
            operation = controlPlane.BindAsync(new BindEventResourceProviderCommand(
                new(deployment, ["https://resource-coordination.example.test"], "resource", "default"), Guid.Empty),
                cancellation.Token);
            await Task.WhenAny(reachedLock.Task, operation).WaitAsync(deadline.Token);
            if (operation.IsCompleted)
            {
                observed = true;
                await operation;
            }
            await reachedLock.Task.WaitAsync(deadline.Token);
            await Assert.That(operation.IsCompleted).IsFalse();
            cancellation.Cancel();
            observed = true;
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            await Assert.That(original.Database.CurrentTransaction).IsNull();
            await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(original)).IsFalse();
            human.RequireCurrent();
            await Assert.That(string.Equals(await ReadImageAsync(), before, StringComparison.Ordinal)).IsTrue();

            // Both the authenticated request and selected DI scope remain alive, and
            // the independent ordered lock is still held. Only preparation cleanup
            // can have released Discovery at this point.
            await using var contender = fixture.Native.CreateDatabase();
            contender.BindTenantLease();
            using var releaseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var acquired = await new EventDiscoveryEnrollmentGate(contender).AcquireAsync(releaseDeadline.Token);
            EventDiscoveryEnrollmentGate.RequireCurrent(contender);
            await Assert.That(blocker.IsCompleted).IsFalse();
        }
        finally
        {
            cancellation.Cancel();
            releaseLock.TrySetResult();
            try
            {
                if (!observed && operation is not null)
                {
                    try { await operation; }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                }
            }
            finally { await blocker; }
        }

        async Task<string> ReadImageAsync()
        {
            await using var observer = fixture.Native.CreateDatabase();
            observer.BindTenantLease();
            var bindings = await observer.SystemSettings.AsNoTracking()
                .Where(row => row.SettingKey == EventResourceProviderBindingDocument.SettingKey)
                .Select(row => new
                {
                    row.Id,
                    row.SettingKey,
                    row.Value,
                    row.IsLocked,
                    row.SettingValueTypeId,
                    row.AllowedValues,
                    row.Description,
                    row.Category,
                    row.CreatedAt,
                    row.CreatedBy,
                    row.UpdatedAt,
                    row.UpdatedBy
                }).OrderBy(row => row.Id).ToArrayAsync();
            var activations = await observer.Set<EventResourceProviderActivation>().AsNoTracking()
                .Where(row => row.Id == deployment)
                .Select(row => new { row.Id, row.State, row.Epoch, row.ConcurrencyStamp }).ToArrayAsync();
            var revisions = await observer.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
                .OrderBy(row => row.TenantId)
                .Select(row => new { row.Id, row.TenantId, row.IdentityEpoch, row.DisclosureEpoch }).ToArrayAsync();
            return JsonSerializer.Serialize(new { bindings, activations, revisions });
        }
    }
}
