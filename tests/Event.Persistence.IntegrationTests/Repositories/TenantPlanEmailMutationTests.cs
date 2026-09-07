// ABOUTME: Verifies tenant-plan SMTP application through real version loading and relational mutation fences.
// ABOUTME: Preserves tenant matching, instance locks, mixed setting atomicity, and post-commit notifications.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Application.Features.ControlPlane.Handlers.Commands;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.QueryFilters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class TenantPlanEmailMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ApplyingAssignment_LoadsAndAppliesPersistedVersionContent(bool mixed)
    {
        var databasePath = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(databasePath);
            PlanScenario scenario;
            await using (var context = CreateContext(databasePath))
                scenario = await SeedPlanAsync(context, mixed);

            await using var writerContext = CreateContext(databasePath);
            using var fixture = new InstanceSettingsCommandFixture(writerContext, scenario.ActorId);
            var result = await CreateHandler(fixture).Handle(Request(scenario), CancellationToken.None);

            await Assert.That(result.IsSuccess).IsTrue();
            await AssertAppliedAsync(fixture, scenario, mixed);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task SmtpPlanWaitsForGlobalFence_BeforeStartingTransaction()
    {
        var databasePath = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(databasePath);
            PlanScenario scenario;
            await using (var context = CreateContext(databasePath))
                scenario = await SeedPlanAsync(context, mixed: true);

            using var cancellation = new CancellationTokenSource();
            await using var holderContext = CreateContext(databasePath);
            var transaction = new TransactionObserver();
            await using var writerContext = CreateContext(databasePath, transaction);
            var holderReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writerReachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var holderLock = new RelationalSettingMutationLock(holderContext, new EfCoreUnitOfWork(holderContext));
            var writerLock = new RelationalSettingMutationLock(writerContext, new EfCoreUnitOfWork(writerContext), (key, _) =>
            {
                if (key == GovernanceSettingKeys.Email.DeliveryEnabled) writerReachedLock.TrySetResult();
                return Task.CompletedTask;
            });
            using var fixture = new InstanceSettingsCommandFixture(writerContext, scenario.ActorId, writerLock);
            Task holder = holderLock.ExecuteOrderedGroupsAsync([[GovernanceSettingKeys.Email.DeliveryEnabled]], async token =>
            {
                holderReady.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                return true;
            }, cancellation.Token);
            Task<BaseCommandResponse<Guid>>? write = null;
            Task? observed = null;
            bool transactionStartedBeforeRelease = false;
            try
            {
                await holderReady.Task.WaitAsync(TimeSpan.FromSeconds(15));
                write = CreateHandler(fixture).Handle(Request(scenario), cancellation.Token);
                observed = await Task.WhenAny(writerReachedLock.Task, transaction.Started.Task, write)
                    .WaitAsync(TimeSpan.FromSeconds(15));
                transactionStartedBeforeRelease = transaction.Started.Task.IsCompleted;
            }
            finally
            {
                release.TrySetResult();
                cancellation.CancelAfter(TimeSpan.FromSeconds(15));
                Task[] pending = write is null ? [holder] : [holder, write];
                await Task.WhenAll(pending);
            }

            await Assert.That(observed).IsSameReferenceAs(writerReachedLock.Task);
            await Assert.That(transactionStartedBeforeRelease).IsFalse();
            await Assert.That((await write!).IsSuccess).IsTrue();
            await AssertAppliedAsync(fixture, scenario, mixed: true);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WrongTenantOrLockedInstanceSetting_RejectsWithoutPartialPlan(bool locked)
    {
        var databasePath = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(databasePath);
            PlanScenario scenario;
            await using (var context = CreateContext(databasePath))
            {
                scenario = await SeedPlanAsync(context, mixed: true);
                if (locked)
                {
                    var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
                    await ApplyEmailSettingsAsync(context,
                        [new(TenantId: null, Key: GovernanceSettingKeys.Email.SmtpHost,
                            Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: true)], mutationLock: mutationLock);
                }
            }
            await using var writerContext = CreateContext(databasePath);
            using var fixture = new InstanceSettingsCommandFixture(writerContext, scenario.ActorId);
            var request = locked ? Request(scenario) : Request(scenario) with { TenantId = Guid.CreateVersion7() };
            var result = await CreateHandler(fixture).Handle(request, CancellationToken.None);

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo(locked
                ? "tenant_plan_setting_locked" : "tenant_plan_assignment_tenant_mismatch");
            await Assert.That(await new TenantSettingRepository(writerContext, fixture.MutationLock).GetAllForTenant(scenario.TenantId)).IsEmpty();
            await Assert.That(fixture.Notifications.Published).IsEmpty();
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task PersistedQuotaAboveInstanceCeiling_RejectsWithoutPartialPlan()
    {
        var databasePath = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(databasePath);
            PlanScenario scenario;
            await using (var context = CreateContext(databasePath))
            {
                scenario = await SeedPlanAsync(context, mixed: true, storageQuota: 2048);
                var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
                await new SystemSettingRepository(context, mutationLock).UpsertAsync(new SystemSetting
                {
                    Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
                    Value = "1024", ValueType = SettingValueType.Long, CreatedAt = DateTime.UtcNow
                });
            }

            await using var writerContext = CreateContext(databasePath);
            using var fixture = new InstanceSettingsCommandFixture(writerContext, scenario.ActorId);
            var result = await CreateHandler(fixture).Handle(Request(scenario), CancellationToken.None);

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("tenant_plan_quota_ceiling_exceeded");
            await Assert.That(await new TenantSettingRepository(writerContext, fixture.MutationLock).GetAllForTenant(scenario.TenantId)).IsEmpty();
            await Assert.That(fixture.Notifications.Published).IsEmpty();
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task UnsafePublicationPolicy_RollsBackAlreadyWrittenSmtpAndAssignment()
    {
        string databasePath = NewDatabasePath();
        try
        {
            await CreateDatabaseAsync(databasePath);
            PlanScenario scenario;
            await using (var seed = CreateContext(databasePath))
            {
                scenario = await SeedPlanAsync(seed, mixed: true, unsafePublication: true);
                await ApplyEmailSettingsAsync(seed,
                    [new(TenantId: scenario.TenantId, Key: GovernanceSettingKeys.Email.SmtpHost,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"smtp.original.test\""),
                     new(TenantId: scenario.TenantId, Key: GovernanceSettingKeys.Email.FromAddress,
                        Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"events@original.test\"")], actorUserId: scenario.ActorId);
            }
            var smtpWrite = new SmtpWriteObserver(scenario.TenantId);
            await using var context = CreateContext(databasePath, smtpWrite);
            using var fixture = new InstanceSettingsCommandFixture(context: context, userId: scenario.ActorId);
            var before = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == scenario.TenantId);
            var assignmentBefore = (await new TenantPlanRepository(context).GetAssignmentAsync(scenario.AssignmentId))!;

            var result = await CreateHandler(fixture).Handle(Request(scenario), CancellationToken.None);

            await Assert.That(result.FailureCode).IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
            await Assert.That(smtpWrite.SawProposedHostInsideTransaction).IsTrue();
            await using var observer = CreateContext(databasePath);
            var settings = await new TenantSettingRepository(observer, new RelationalSettingMutationLock(observer, new EfCoreUnitOfWork(observer)))
                .GetAllForTenant(scenario.TenantId);
            await Assert.That(settings.Count).IsEqualTo(2);
            await Assert.That(settings.Single(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost).Value).IsEqualTo("\"smtp.original.test\"");
            await Assert.That(settings.Single(row => row.SettingKey == GovernanceSettingKeys.Email.FromAddress).Value).IsEqualTo("\"events@original.test\"");
            var after = await observer.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == scenario.TenantId);
            await Assert.That(after.DeliveryPolicyRevision).IsEqualTo(before.DeliveryPolicyRevision);
            await Assert.That(after.OptionalSuppressedThroughRevision).IsEqualTo(before.OptionalSuppressedThroughRevision);
            await Assert.That(after.OptionalSuppressedThroughUtc).IsEqualTo(before.OptionalSuppressedThroughUtc);
            var assignmentAfter = (await new TenantPlanRepository(observer).GetAssignmentAsync(scenario.AssignmentId))!;
            await Assert.That(assignmentAfter.TenantPlanVersionId).IsEqualTo(assignmentBefore.TenantPlanVersionId);
            await Assert.That(assignmentAfter.TenantPlanAssignmentStatusId).IsEqualTo(assignmentBefore.TenantPlanAssignmentStatusId);
            await Assert.That(assignmentAfter.UpdatedAt).IsEqualTo(assignmentBefore.UpdatedAt);
            await Assert.That(await observer.Set<TenantPlanApplicationLog>().AsNoTracking()
                .AnyAsync(row => row.TenantId == scenario.TenantId && row.TenantPlanAssignmentId == scenario.AssignmentId)).IsFalse();
            await Assert.That(fixture.Notifications.Published).IsEmpty();
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static ApplyControlPlaneTenantPlanAssignmentCommandHandler CreateHandler(InstanceSettingsCommandFixture fixture) =>
        new(new TenantPlanRepository(fixture.Context), new TenantSettingRepository(fixture.Context, fixture.MutationLock), fixture.SystemSettings,
            new TenantPlanStorageQuotaCeilingPolicy(fixture.SystemSettings), fixture.UnitOfWork, fixture.MutationLock,
            fixture.PublicationPolicyBoundary, fixture.Settings, fixture.Mediator, fixture.EmailDeliverySettingsWriter);

    private static ApplyControlPlaneTenantPlanAssignmentCommand Request(PlanScenario scenario) =>
        new(TenantId: scenario.TenantId, AssignmentId: scenario.AssignmentId, AppliedByUserId: scenario.ActorId);

    private static async Task AssertAppliedAsync(InstanceSettingsCommandFixture fixture, PlanScenario scenario, bool mixed)
    {
        var repository = new TenantSettingRepository(fixture.Context, fixture.MutationLock);
        var host = await repository.GetByTenantAndKey(scenario.TenantId, GovernanceSettingKeys.Email.SmtpHost);
        await Assert.That(host).IsNotNull();
        await Assert.That(host!.Value).IsEqualTo("\"smtp.tenant-plan.test\"");
        var sender = await repository.GetByTenantAndKey(scenario.TenantId, GovernanceSettingKeys.Email.FromAddress);
        await Assert.That(sender!.Value).IsEqualTo("\"events@tenant-plan.test\"");
        if (mixed)
        {
            var assistant = await repository.GetByTenantAndKey(scenario.TenantId, GovernanceSettingKeys.AiAssistant.Enabled);
            var approval = await repository.GetByTenantAndKey(scenario.TenantId, EventSettingDefinitions.RequireApproval.Key);
            await Assert.That(assistant!.Value).IsEqualTo("true");
            await Assert.That(approval!.Value).IsEqualTo("true");
        }
        await Assert.That(fixture.Notifications.Published.Select(notification => notification.Key))
            .Contains(GovernanceSettingKeys.Email.SmtpHost);
    }

    private static async Task<PlanScenario> SeedPlanAsync(ExploreDbContext context, bool mixed, long? storageQuota = null,
        bool unsafePublication = false)
    {
        await SetEmailSettingAsync(context, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");
        var actorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(), FullName = "Tenant plan SMTP", Slug = $"plan-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = now
        };
        var plan = new TenantPlan
        {
            Id = Guid.CreateVersion7(), Key = $"smtp-{Guid.CreateVersion7():N}", DisplayName = "SMTP plan", CreatedAt = now
        };
        var version = new TenantPlanVersion
        {
            Id = Guid.CreateVersion7(), TenantPlan = plan, TenantPlanId = plan.Id, VersionNumber = 1,
            TenantPlanStatusId = (int)TenantPlanStatusEnum.Published, CurrencyCode = "EUR", BillingPeriod = "monthly",
            IsActiveForProvisioning = true, CreatedAt = now,
            Settings =
            [
                new() { Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Email.SmtpHost,
                    JsonValue = "\"smtp.tenant-plan.test\"", CreatedAt = now },
                new() { Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Email.FromAddress,
                    JsonValue = "\"events@tenant-plan.test\"", CreatedAt = now }
            ]
        };
        if (mixed)
        {
            version.Settings.Add(new TenantPlanVersionSetting
            {
                Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.AiAssistant.Enabled,
                JsonValue = "true", CreatedAt = now
            });
            if (unsafePublication)
                version.Settings.Add(new TenantPlanVersionSetting
                {
                    Id = Guid.CreateVersion7(), SettingKey = EventReportingIntakeSettingDefinitions.IntakeEnabled.Key,
                    JsonValue = "false", CreatedAt = now
                });
            version.Settings.Add(new TenantPlanVersionSetting
            {
                Id = Guid.CreateVersion7(), SettingKey = EventSettingDefinitions.RequireApproval.Key,
                JsonValue = unsafePublication ? "false" : "true", CreatedAt = now
            });
        }
        if (storageQuota.HasValue)
        {
            version.Quotas.Add(new TenantPlanVersionQuota
            {
                Id = Guid.CreateVersion7(), QuotaKey = TenantPlanQuotaKeys.StorageBytes,
                Limit = storageQuota.Value, CreatedAt = now
            });
        }
        var assignment = await new TenantPlanRepository(context).CreateAssignmentAsync(new TenantPlanAssignment
        {
            Id = Guid.CreateVersion7(), Tenant = tenant, TenantId = tenant.Id, TenantPlan = plan, TenantPlanId = plan.Id,
            TenantPlanVersion = version, TenantPlanVersionId = version.Id,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedByUserId = actorId, AssignedAt = now, CreatedAt = now
        });
        return new(TenantId: tenant.Id, AssignmentId: assignment.Id, ActorId: actorId);
    }

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"plan-email-mutation-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }

    private sealed record PlanScenario(Guid TenantId, Guid AssignmentId, Guid ActorId);

    private sealed class SmtpWriteObserver(Guid tenantId) : SaveChangesInterceptor
    {
        internal bool SawProposedHostInsideTransaction { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context && context.Database.CurrentTransaction is not null)
                SawProposedHostInsideTransaction |= await context.TenantSettingOverrides
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                    .AnyAsync(row => row.TenantId == tenantId && row.SettingKey == GovernanceSettingKeys.Email.SmtpHost
                        && row.Value == "\"smtp.tenant-plan.test\"", cancellationToken);
            return result;
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
