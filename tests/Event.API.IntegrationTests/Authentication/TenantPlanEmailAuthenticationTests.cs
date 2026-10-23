using System.Data.Common;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class TenantPlanEmailAuthenticationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ApplyingAssignment_LoadsAndAppliesPersistedVersionContent(bool mixed)
    {
        var notifications = new Notifications();
        await using var fixture = await CreateFixtureAsync(notifications);
        var scenario = await SeedPlanAsync(fixture, mixed);
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var result = await PlanHandler(services).ExecuteAsync(Request(scenario), CancellationToken.None);
            await Assert.That(result.IsSuccess).IsTrue();
        }, planAssignment: true);
        await AssertAppliedAsync(fixture, notifications, scenario, mixed);
    }

    [Test]
    public async Task SmtpPlanWaitsForGlobalFence_BeforeStartingTransaction()
    {
        var notifications = new Notifications();
        var writerReachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await CreateFixtureAsync(notifications, (key, _) =>
        {
            if (key == GovernanceSettingKeys.Email.DeliveryEnabled)
                writerReachedLock.TrySetResult();
            return Task.CompletedTask;
        });
        var scenario = await SeedPlanAsync(fixture, mixed: true);
        var transaction = new TransactionObserver();
        using var cancellation = new CancellationTokenSource();
        await using var holderContext = fixture.CreateIndependentContext();
        var holderReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holderLock = new RelationalSettingMutationLock(holderContext, new EfCoreUnitOfWork(holderContext));
        Task holder = holderLock.ExecuteOrderedGroupsAsync([[GovernanceSettingKeys.Email.DeliveryEnabled]], async token =>
        {
            holderReady.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            return true;
        }, cancellation.Token);
        Task? write = null;
        Task? observed = null;
        bool transactionStartedBeforeRelease = false;
        BaseCommandResponse<Guid>? response = null;
        try
        {
            await holderReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
            write = fixture.ExecuteAsync(tenantOwner: true, async services =>
            {
                await Assert.That(ReferenceEquals(services.GetRequiredService<ExploreDbContext>(), holderContext)).IsFalse();
                response = await PlanHandler(services).ExecuteAsync(Request(scenario), cancellation.Token);
            }, transaction, planAssignment: true);
            observed = await Task.WhenAny(writerReachedLock.Task, transaction.Started.Task, write)
                .WaitAsync(TimeSpan.FromSeconds(15));
            transactionStartedBeforeRelease = transaction.Started.Task.IsCompleted;
        }
        finally
        {
            release.TrySetResult();
            cancellation.CancelAfter(TimeSpan.FromSeconds(15));
            await Task.WhenAll(write is null ? [holder] : [holder, write]);
        }

        await Assert.That(observed).IsSameReferenceAs(writerReachedLock.Task);
        await Assert.That(transactionStartedBeforeRelease).IsFalse();
        await Assert.That(transaction.Started.Task.IsCompleted).IsTrue();
        await Assert.That(response!.IsSuccess).IsTrue();
        await AssertAppliedAsync(fixture, notifications, scenario, mixed: true);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WrongTenantOrLockedInstanceSetting_RejectsWithoutPartialPlan(bool locked)
    {
        var notifications = new Notifications();
        await using var fixture = await CreateFixtureAsync(notifications);
        var scenario = await SeedPlanAsync(fixture, mixed: true);
        if (locked)
            await fixture.ExecuteAsync(tenantOwner: false, async services =>
            {
                var result = await services.GetRequiredService<IEmailDeliverySettingsWriter>().ApplyAsync(
                    [new(null, GovernanceSettingKeys.Email.SmtpHost,
                        EmailDeliverySettingMutationKind.SetLock, IsLocked: true)],
                    fixture.Subject.PrincipalId);
                await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            });
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var request = locked ? Request(scenario) : Request(scenario) with { TenantId = Guid.CreateVersion7() };
            if (!locked)
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PlanHandler(services).ExecuteAsync(request, CancellationToken.None));
                return;
            }
            var result = await PlanHandler(services).ExecuteAsync(request, CancellationToken.None);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("tenant_plan_setting_locked");
        }, planAssignment: true);
        await AssertUnchangedAsync(fixture, notifications, scenario);
    }

    [Test]
    public async Task PersistedQuotaAboveInstanceCeiling_RejectsWithoutPartialPlan()
    {
        var notifications = new Notifications();
        await using var fixture = await CreateFixtureAsync(notifications);
        var scenario = await SeedPlanAsync(fixture, mixed: true, storageQuota: 2048);
        await using (var seed = fixture.CreateIndependentContext())
        {
            var ceiling = await seed.SystemSettings.SingleOrDefaultAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes);
            if (ceiling is null)
            {
                ceiling = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
                    Value = "1024",
                    ValueType = SettingValueType.Long,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                };
                seed.SystemSettings.Add(ceiling);
            }
            else ceiling.Value = "1024";
            await seed.SaveChangesAsync();
        }
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var result = await PlanHandler(services).ExecuteAsync(Request(scenario), CancellationToken.None);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("tenant_plan_quota_ceiling_exceeded");
        }, planAssignment: true);
        await AssertUnchangedAsync(fixture, notifications, scenario);
    }

    [Test]
    public async Task UnsafePublicationPolicy_RollsBackAlreadyWrittenSmtpAndAssignment()
    {
        var notifications = new Notifications();
        var smtpWrite = new SmtpWriteObserver(PlatformDefaults.DefaultTenantId);
        await using var fixture = await CreateFixtureAsync(notifications, writeObserver: smtpWrite);
        var scenario = await SeedPlanAsync(fixture, mixed: true, unsafePublication: true);
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var result = await services.GetRequiredService<IEmailDeliverySettingsWriter>().ApplyAsync(
                [new(scenario.TenantId, GovernanceSettingKeys.Email.SmtpHost,
                    EmailDeliverySettingMutationKind.SetValue, "\"smtp.original.test\""),
                 new(scenario.TenantId, GovernanceSettingKeys.Email.FromAddress,
                    EmailDeliverySettingMutationKind.SetValue, "\"events@original.test\"")],
                scenario.ActorId);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
        });
        await using var beforeContext = fixture.CreateIndependentContext();
        var before = await beforeContext.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
            .SingleAsync(row => row.TenantId == scenario.TenantId);
        var settingsBefore = await SettingsAsync(beforeContext, scenario.TenantId);
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var result = await PlanHandler(services).ExecuteAsync(Request(scenario), CancellationToken.None);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
            await Assert.That(smtpWrite.SawProposedHostInsideTransaction).IsTrue();
        }, planAssignment: true);

        await using var observer = fixture.CreateIndependentContext();
        var settings = await SettingsAsync(observer, scenario.TenantId);
        await Assert.That(settings.Count).IsEqualTo(2);
        await Assert.That(settings.Single(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost).Value)
            .IsEqualTo("\"smtp.original.test\"");
        await Assert.That(settings.Single(row => row.SettingKey == GovernanceSettingKeys.Email.FromAddress).Value)
            .IsEqualTo("\"events@original.test\"");
        await Assert.That(settings.Select(SettingSnapshot).OrderBy(row => row.Key))
            .IsEquivalentTo(settingsBefore.Select(SettingSnapshot).OrderBy(row => row.Key));
        var after = await observer.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
            .SingleAsync(row => row.TenantId == scenario.TenantId);
        await Assert.That(after.DeliveryPolicyRevision).IsEqualTo(before.DeliveryPolicyRevision);
        await Assert.That(after.OptionalSuppressedThroughRevision).IsEqualTo(before.OptionalSuppressedThroughRevision);
        await Assert.That(after.OptionalSuppressedThroughUtc).IsEqualTo(before.OptionalSuppressedThroughUtc);
        await AssertAssignmentUnchangedAsync(observer, scenario);
        await Assert.That(notifications.Published).IsEmpty();
    }

    // The fixture has already selected the original combined owner. Resolve the production concrete
    // native handler here: the outer decorator's disposable standard reader cannot replace it.
    private static ICommandHandler<ApplyInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>>
        PlanHandler(IServiceProvider services) =>
        services.GetRequiredService<ApplyInstanceTenantPlanAssignmentCommandHandler>();

    private static ApplyInstanceTenantPlanAssignmentCommand Request(PlanScenario scenario) =>
        new(scenario.TenantId, scenario.AssignmentId, scenario.ActorId);

    private sealed class SmtpWriteObserver(Guid tenantId)
    {
        internal bool SawProposedHostInsideTransaction { get; private set; }

        internal async Task ObserveAsync(ExploreDbContext context, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                SawProposedHostInsideTransaction |= await context.TenantSettingOverrides
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                    .AnyAsync(row => row.TenantId == tenantId && row.SettingKey == GovernanceSettingKeys.Email.SmtpHost
                        && row.Value == "\"smtp.tenant-plan.test\"", cancellationToken);
        }
    }

    private sealed class ObservedPlanSourceFactory(IHumanMixedSettingsSourceFactory inner,
        ExploreDbContext context, SmtpWriteObserver observer) : IHumanMixedSettingsSourceFactory
    {
        public Task<IHumanMixedSettingsSourceProtocol> CreateAsync(
            Explore.Application.Features.Settings.Requests.Commands.UpdateSettingBatchCommand command, CancellationToken token) =>
            inner.CreateAsync(command, token);
        public async Task<IHumanMixedSettingsSourceProtocol> CreateAsync(TypedSettingsOriginal original, CancellationToken token) =>
            new ObservedPlanSource(await inner.CreateAsync(original, token), context, observer);
    }

    private sealed class ObservedPlanSource(IHumanMixedSettingsSourceProtocol inner,
        ExploreDbContext context, SmtpWriteObserver observer) : IHumanMixedSettingsSourceProtocol
    {
        public HumanCommitSubject Subject => inner.Subject;
        public Explore.Application.Authentication.HumanAuthenticationLifetime Lifetime => inner.Lifetime;
        public HumanSupportForwardingObservation Forwarding => inner.Forwarding;
        public Guid? TenantId => inner.TenantId;
        public string OriginalData => inner.OriginalData;
        public Task<HumanEmailSourceFacts> ObserveAsync(CancellationToken token) => inner.ObserveAsync(token);
        public Task<HumanEmailSourceFacts> AcquireAsync(CancellationToken token) => inner.AcquireAsync(token);
        public async Task<HumanEmailSourceFacts> RequireOwnedAsync(CancellationToken token)
        {
            var facts = await inner.RequireOwnedAsync(token);
            await observer.ObserveAsync(context, token);
            return facts;
        }
    }

    private sealed class TransactionObserver : DbTransactionInterceptor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
