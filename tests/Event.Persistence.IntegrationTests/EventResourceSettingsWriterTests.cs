using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
[NotInParallel("EventResourceSettingsWriter")]
public sealed class EventResourceSettingsWriterTests(EventResourcePersistenceTests.TestDatabase database)
{
    [Test]
    public async Task WideningOneTenantCeilingRejectsTheEntireProposedBatch()
    {
        var tenantId = await ResetAsync();
        await using var context = database.CreateContext();
        var result = await Writer(context).ApplyAsync(
        [
            new(tenantId, GovernanceSettingKeys.EventResources.AuditRetentionDays,
                EventResourceSettingMutationKind.SetValue, "5"),
            new(tenantId, GovernanceSettingKeys.EventResources.MaxUploadBytes,
                EventResourceSettingMutationKind.SetValue, "20971520")
        ], null);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_resource_policy_widening");
        await Assert.That(result.DeferredNotifications).IsEmpty();
        await Assert.That(await context.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == tenantId)).IsEqualTo(0);
    }

    [Test]
    public async Task ValidTenantRestrictionPersistsWithoutChangingInstanceDefaults()
    {
        var tenantId = await ResetAsync();
        await using var context = database.CreateContext();
        var result = await Writer(context).ApplyAsync(
            [new(tenantId, GovernanceSettingKeys.EventResources.MaxActiveResources,
                EventResourceSettingMutationKind.SetValue, "12")], null);
        await Assert.That(result.Success).IsTrue();
        await Assert.That((await context.TenantSettingOverrides.AsNoTracking()
            .SingleAsync(row => row.TenantId == tenantId)).Value).IsEqualTo("12");
        await Assert.That(await context.SystemSettings.AsNoTracking()
            .CountAsync(row => row.SettingKey == GovernanceSettingKeys.EventResources.MaxActiveResources)).IsEqualTo(0);
        await Assert.That(result.DeferredNotifications.Length).IsEqualTo(1);
    }

    [Test]
    public async Task TenantCannotOptIntoUnscannedFilesEvenWhenInstanceAlreadyAllowsThem()
    {
        var tenantId = await ResetAsync();
        await using var context = database.CreateContext();
        var writer = Writer(context);
        (await writer.ApplyAsync(
            [new(null, GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                EventResourceSettingMutationKind.SetValue, "true")], null)).EnsureAccepted();
        var result = await writer.ApplyAsync(
            [new(tenantId, GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                EventResourceSettingMutationKind.SetValue, "true")], null);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.DeferredNotifications).IsEmpty();
        await Assert.That(await context.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == tenantId)).IsEqualTo(0);
    }

    [Test]
    public async Task NativeStrictBatchRejectsInvalidResourcePolicyAtomically()
    {
        await ResetAsync();
        await using var context = database.CreateContext();
        Guid userId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
        using var fixture = new InstanceSettingsCommandFixture(context, userId);
        var handler = new UpdateSettingBatchCommandHandler(
            fixture.Settings,
            new Explore.Persistence.Repositories.UserPreferenceRepository(context),
            fixture,
            fixture.CurrentUserService,
            fixture.AdminContext,
            fixture.NotificationHandlers,
            NullLogger<UpdateSettingBatchCommandHandler>.Instance,
            fixture.PublicationPolicyBoundary,
            fixture.UnitOfWork,
            fixture.MutationLock,
            fixture.EmailDeliverySettingsWriter,
            fixture.VisitorSettings,
            fixture.EventResourceSettingsWriter);

        var response = await handler.ExecuteAsync(new UpdateSettingBatchCommand
        {
            Category = EventResourceSettingDefinitions.Category,
            Scope = Explore.Domain.Settings.SettingScope.Instance,
            Mode = BatchUpdateMode.Strict,
            Values = new Dictionary<string, string>
            {
                [GovernanceSettingKeys.EventResources.AuditRetentionDays] = "5",
                [GovernanceSettingKeys.EventResources.MaxActiveResources] = "501"
            }
        }, CancellationToken.None);

        await Assert.That(response.Success).IsFalse();
        await Assert.That(await fixture.SystemSettings.GetByKey(
            GovernanceSettingKeys.EventResources.AuditRetentionDays)).IsNull();
        await Assert.That(await fixture.SystemSettings.GetByKey(
            GovernanceSettingKeys.EventResources.MaxActiveResources)).IsNull();
        await Assert.That(fixture.Notifications.Published).IsEmpty();
    }

    [Test]
    public async Task SettingUpsertServiceUsesCoordinatedResourceWriter()
    {
        await ResetAsync();
        await using var context = database.CreateContext();
        using var fixture = new InstanceSettingsCommandFixture(context, Guid.CreateVersion7());
        string key = GovernanceSettingKeys.EventResources.MaxActiveResources;

        await fixture.UpsertService.UpsertValueAsync(key, "10");
        await fixture.UpsertService.UpsertLockAsync(key, "10", isLocked: true, actorId: null);

        SystemSetting stored = (await fixture.SystemSettings.GetByKey(key))!;
        await Assert.That(stored.Value).IsEqualTo("10");
        await Assert.That(stored.IsLocked).IsTrue();
        await Assert.That(fixture.Notifications.Published.Select(notification => notification.Key))
            .IsEquivalentTo([key]);
    }

    [Test]
    public async Task GenericRepositoriesRejectResourceGovernanceBypasses()
    {
        var tenantId = await ResetAsync();
        await using var context = database.CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var systems = new Explore.Persistence.Repositories.SystemSettingRepository(context, mutationLock);
        var tenants = new Explore.Persistence.Repositories.TenantSettingRepository(context, mutationLock);
        string key = GovernanceSettingKeys.EventResources.MaxActiveResources;

        await Assert.ThrowsAsync<InvalidOperationException>(() => systems.UpsertAsync(new SystemSetting
        {
            SettingKey = key,
            Value = "10",
            CreatedAt = DateTime.UtcNow
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenants.SetValueAsync(tenantId, key, "10"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenants.RemoveOverrideAsync(tenantId, key));
    }

    [Test]
    public async Task InstanceTighteningDoesNotRequireRewritingExistingTenantChoices()
    {
        var tenantId = await ResetAsync();
        await using var context = database.CreateContext();
        var writer = Writer(context);
        (await writer.ApplyAsync(
            [new(tenantId, GovernanceSettingKeys.EventResources.MaxActiveResources,
                EventResourceSettingMutationKind.SetValue, "100")], null)).EnsureAccepted();
        var result = await writer.ApplyAsync(
            [new(null, GovernanceSettingKeys.EventResources.MaxActiveResources,
                EventResourceSettingMutationKind.SetValue, "10")], null);
        await Assert.That(result.Success).IsTrue();
        await Assert.That((await context.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.EventResources.MaxActiveResources)).Value).IsEqualTo("10");
        await Assert.That((await context.TenantSettingOverrides.AsNoTracking()
            .SingleAsync(row => row.TenantId == tenantId)).Value).IsEqualTo("100");
    }

    private static EventResourceSettingsWriter Writer(ExploreDbContext context)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        return new(context, new RelationalSettingMutationLock(context, unitOfWork), unitOfWork);
    }

    [Test]
    public async Task CallerRollbackRemovesResourceChangesCommittedOnlyInsideItsTransaction()
    {
        var tenantId = await ResetAsync();
        await using var context = database.CreateContext();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var locks = new RelationalSettingMutationLock(context, unitOfWork);
        var writer = new EventResourceSettingsWriter(context, locks, unitOfWork);
        bool reachedLaterSection = false;
        await Assert.That(() => locks.ExecuteOrderedGroupsAsync([EventResourceSettingMutationGuard.Keys],
            token => unitOfWork.ExecuteSerializableAsync<bool>(async inner =>
            {
                (await writer.ApplyAsync(
                    [new(tenantId, GovernanceSettingKeys.EventResources.AuditRetentionDays,
                        EventResourceSettingMutationKind.SetValue, "5")], null, inner)).EnsureAccepted();
                reachedLaterSection = true;
                throw new InvalidOperationException("A later manifest section failed.");
            }, token))).Throws<InvalidOperationException>();
        await Assert.That(reachedLaterSection).IsTrue();
        await using var verification = database.CreateIndependentContext();
        await Assert.That(await verification.TenantSettingOverrides.AsNoTracking()
            .CountAsync(row => row.TenantId == tenantId)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceTighteningAndTenantMutationShareAnOuterLeaseBeforeEitherSnapshot(bool instanceFirst)
    {
        var tenantId = await ResetAsync();
        await using var instanceContext = database.CreateIndependentContext();
        await using var tenantContext = database.CreateIndependentContext();
        var instanceUnit = new EfCoreUnitOfWork(instanceContext);
        var tenantUnit = new EfCoreUnitOfWork(tenantContext);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstOwnsKeys = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenderArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string[] keys = EventResourceSettingMutationGuard.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        async Task Holder(string key, CancellationToken token)
        {
            if (key != keys[^1]) return;
            firstOwnsKeys.TrySetResult();
            await release.Task.WaitAsync(token);
        }
        Task Contender(string key, CancellationToken token)
        {
            if (key == keys[0]) contenderArrived.TrySetResult();
            return Task.CompletedTask;
        }
        var instanceWriter = new EventResourceSettingsWriter(instanceContext,
            new RelationalSettingMutationLock(instanceContext, instanceUnit, instanceFirst ? Holder : Contender), instanceUnit);
        var tenantWriter = new EventResourceSettingsWriter(tenantContext,
            new RelationalSettingMutationLock(tenantContext, tenantUnit, instanceFirst ? Contender : Holder), tenantUnit);
        Task<EventResourceSettingsWriteResult> Tighten() => instanceWriter.ApplyAsync(
            [new(null, GovernanceSettingKeys.EventResources.MaxActiveResources,
                EventResourceSettingMutationKind.SetValue, "10")], null, deadline.Token);
        Task<EventResourceSettingsWriteResult> Choose() => tenantWriter.ApplyAsync(
            [new(tenantId, GovernanceSettingKeys.EventResources.MaxActiveResources,
                EventResourceSettingMutationKind.SetValue, "100")], null, deadline.Token);
        Task<EventResourceSettingsWriteResult>? tightening = null, choosing = null;
        if (instanceFirst) tightening = Tighten(); else choosing = Choose();
        await firstOwnsKeys.Task.WaitAsync(deadline.Token);
        if (instanceFirst) choosing = Choose(); else tightening = Tighten();
        try
        {
            await contenderArrived.Task.WaitAsync(deadline.Token);
            await Assert.That(instanceContext.Database.CurrentTransaction).IsNull();
            await Assert.That(tenantContext.Database.CurrentTransaction).IsNull();
        }
        finally
        {
            release.TrySetResult();
        }
        await Assert.That((await tightening!.WaitAsync(deadline.Token)).Success).IsTrue();
        var tenantResult = await choosing!.WaitAsync(deadline.Token);
        await Assert.That(tenantResult.Success).IsEqualTo(!instanceFirst);
        if (instanceFirst)
            await Assert.That(tenantResult.FailureCode).IsEqualTo("event_resource_policy_widening");
        await using var verification = database.CreateIndependentContext();
        await Assert.That((await verification.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.EventResources.MaxActiveResources)).Value).IsEqualTo("10");
        var choice = await verification.TenantSettingOverrides.AsNoTracking()
            .SingleOrDefaultAsync(row => row.TenantId == tenantId);
        await Assert.That(choice?.Value).IsEqualTo(instanceFirst ? null : "100");
    }

    private async Task<Guid> ResetAsync()
    {
        await using var context = database.CreateContext();
        var keys = EventResourceSettingDefinitions.All.Select(definition => definition.Key).ToArray();
        await context.TenantSettingOverrides.Where(row => keys.Contains(row.SettingKey)).ExecuteDeleteAsync();
        await context.SystemSettings.Where(row => keys.Contains(row.SettingKey)).ExecuteDeleteAsync();
        var scope = await database.SeedScopeAsync();
        return scope.TenantAId;
    }
}
