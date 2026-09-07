// ABOUTME: Proves committed delivery disablement prevents SMTP handoff and consumes no attempt budget.
// ABOUTME: Uses real SQLite settings locks and explicit barriers to test disable-versus-admission ordering.

using System.Data.Common;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryFenceTests
{
    public enum TenantPolicyMutation { Set, Remove, Lock, Unlock, CreateBatch, UpsertBatch }

    [Test]
    [Arguments(TenantPolicyMutation.Set)]
    [Arguments(TenantPolicyMutation.Remove)]
    [Arguments(TenantPolicyMutation.Lock)]
    [Arguments(TenantPolicyMutation.Unlock)]
    [Arguments(TenantPolicyMutation.CreateBatch)]
    [Arguments(TenantPolicyMutation.UpsertBatch)]
    public async Task DedicatedTenantPolicyMutation_WaitsForGlobalFence(TenantPolicyMutation mutation)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-tenant-writer-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            Guid tenantId = Guid.CreateVersion7();
            await using (ExploreDbContext seed = CreateContext(databasePath))
            {
                await SetEmailSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");
                seed.Tenants.Add(new Tenant
                {
                    Id = tenantId, FullName = "Tenant SMTP writer", Slug = $"smtp-{tenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
                if (mutation != TenantPolicyMutation.CreateBatch)
                    await ApplyEmailSettingsAsync(seed,
                        [new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.SmtpHost,
                            Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"smtp.initial.test\"",
                            IsLocked: mutation == TenantPolicyMutation.Unlock)]);
            }

            using var cancellation = new CancellationTokenSource();
            await using ExploreDbContext holderContext = CreateContext(databasePath);
            var transaction = new TransactionStartedObserver();
            await using ExploreDbContext writerContext = CreateContext(databasePath, transaction);
            var holderOwnsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var holderLock = new RelationalSettingMutationLock(holderContext, new EfCoreUnitOfWork(holderContext));
            var writerLock = new RelationalSettingMutationLock(writerContext, new EfCoreUnitOfWork(writerContext),
                (key, _) =>
                {
                    if (key == GovernanceSettingKeys.Email.DeliveryEnabled) reachedLock.TrySetResult();
                    return Task.CompletedTask;
                });
            Task holder = holderLock.ExecuteOrderedGroupsAsync([[GovernanceSettingKeys.Email.DeliveryEnabled]],
                async token =>
                {
                    holderOwnsLock.SetResult();
                    await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                    return true;
                }, cancellation.Token);
            Task? writer = null;
            Task? observed = null;
            bool transactionStartedBeforeRelease = false;
            try
            {
                await holderOwnsLock.Task.WaitAsync(TimeSpan.FromSeconds(15));
                writer = ApplyTenantMutationAsync(writerContext, writerLock, tenantId, mutation, cancellation.Token);
                observed = await Task.WhenAny(reachedLock.Task, transaction.Started.Task, writer)
                    .WaitAsync(TimeSpan.FromSeconds(15));
                transactionStartedBeforeRelease = transaction.Started.Task.IsCompleted;
            }
            finally
            {
                release.TrySetResult();
                cancellation.CancelAfter(TimeSpan.FromSeconds(15));
                Task[] pending = writer is null ? [holder] : [holder, writer];
                await Task.WhenAll(pending);
            }

            await Assert.That(observed).IsSameReferenceAs(reachedLock.Task);
            await Assert.That(transactionStartedBeforeRelease).IsFalse();
            await using ExploreDbContext verification = CreateContext(databasePath);
            var persisted = await verification.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleOrDefaultAsync(setting => setting.TenantId == tenantId
                    && setting.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
            if (mutation == TenantPolicyMutation.Remove)
                await Assert.That(persisted).IsNull();
            else if (mutation is TenantPolicyMutation.Lock or TenantPolicyMutation.Unlock)
                await Assert.That(persisted!.IsLocked).IsEqualTo(mutation == TenantPolicyMutation.Lock);
            else
                await Assert.That(persisted!.Value).IsEqualTo("\"smtp.changed.test\"");
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static Task ApplyTenantMutationAsync(ExploreDbContext context, ISettingMutationLock mutationLock, Guid tenantId,
        TenantPolicyMutation mutation, CancellationToken cancellationToken)
    {
        const string key = GovernanceSettingKeys.Email.SmtpHost;
        const string value = "\"smtp.changed.test\"";
        EmailDeliverySettingMutation change = mutation switch
        {
            TenantPolicyMutation.Remove => new(TenantId: tenantId, Key: key, Kind: EmailDeliverySettingMutationKind.Remove),
            TenantPolicyMutation.Lock or TenantPolicyMutation.Unlock => new(TenantId: tenantId, Key: key,
                Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: mutation == TenantPolicyMutation.Lock),
            TenantPolicyMutation.Set or TenantPolicyMutation.CreateBatch or TenantPolicyMutation.UpsertBatch =>
                new(TenantId: tenantId, Key: key, Kind: EmailDeliverySettingMutationKind.SetValue, Value: value),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        return ApplyEmailSettingsAsync(context,
            mutation is TenantPolicyMutation.CreateBatch or TenantPolicyMutation.UpsertBatch
                ? [change, new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.SmtpPort,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "2525")]
                : [change], mutationLock: mutationLock, cancellationToken: cancellationToken);
    }

    [Test]
    [Arguments(false, GovernanceSettingKeys.Email.SmtpHost)]
    [Arguments(true, GovernanceSettingKeys.Email.SmtpHost)]
    [Arguments(true, "EMAIL.SMTP_HOST")]
    [Arguments(true, " email.smtp_host ")]
    public async Task GenericCallerOwnedSmtpWrite_RejectsProtectedPolicyKeys(
        bool useExplicitTransactionWrite, string settingKey)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-late-lock-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using ExploreDbContext context = CreateContext(databasePath);
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var repository = new SystemSettingRepository(context, mutationLock);

            await Assert.That(async () => await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var setting = new SystemSetting
                {
                    Id = Guid.CreateVersion7(), SettingKey = settingKey,
                    Value = "\"smtp.changed.test\"", ValueType = SettingValueType.String,
                    CreatedAt = DateTime.UtcNow
                };
                if (useExplicitTransactionWrite)
                    await repository.UpsertInCurrentTransactionAsync(setting, token);
                else
                    await repository.UpsertAsync(setting, token);
            })).Throws<InvalidOperationException>();

            await using ExploreDbContext verification = CreateContext(databasePath);
            string host = await verification.SystemSettings
                .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpHost)
                .Select(setting => setting.Value).SingleAsync();
            await Assert.That(host).IsEqualTo("\"smtp.fixture.test\"");
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    [Arguments(GovernanceSettingKeys.Email.DeliveryEnabled, "false")]
    [Arguments(GovernanceSettingKeys.Email.SmtpHost, "\"smtp.changed.test\"")]
    [Arguments(GovernanceSettingKeys.TenantDelegation.LockSmtp, "false")]
    public async Task DedicatedPolicyMutation_AcquiresGlobalFenceBeforeStartingTransaction(
        string settingKey, string value)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-policy-writer-order-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            using var operationCancellation = new CancellationTokenSource();
            await using ExploreDbContext holderContext = CreateContext(databasePath);
            var transaction = new TransactionStartedObserver();
            await using ExploreDbContext writerContext = CreateContext(databasePath, transaction);
            var holderOwnsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writerReachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var holderLock = new RelationalSettingMutationLock(holderContext, new EfCoreUnitOfWork(holderContext));
            var writerLock = new RelationalSettingMutationLock(writerContext, new EfCoreUnitOfWork(writerContext),
                (key, _) =>
                {
                    if (key == GovernanceSettingKeys.Email.DeliveryEnabled)
                        writerReachedLock.TrySetResult();
                    return Task.CompletedTask;
                });
            Task holder = holderLock.ExecuteOrderedGroupsAsync(
                [[GovernanceSettingKeys.Email.DeliveryEnabled]], async token =>
                {
                    holderOwnsLock.SetResult();
                    await releaseLock.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                    return true;
                }, operationCancellation.Token);
            Task? writer = null;
            Task? observed = null;
            bool transactionStartedBeforeRelease = false;
            bool writerCompletedBeforeRelease = false;
            try
            {
                await holderOwnsLock.Task.WaitAsync(TimeSpan.FromSeconds(15));
                writer = SetEmailSettingAsync(writerContext, settingKey, value,
                    mutationLock: writerLock, cancellationToken: operationCancellation.Token);
                observed = await Task.WhenAny(writerReachedLock.Task, transaction.Started.Task, writer)
                    .WaitAsync(TimeSpan.FromSeconds(15));
                transactionStartedBeforeRelease = transaction.Started.Task.IsCompleted;
                writerCompletedBeforeRelease = writer.IsCompleted;
            }
            finally
            {
                releaseLock.TrySetResult();
                operationCancellation.CancelAfter(TimeSpan.FromSeconds(15));
                Task[] pending = writer is null ? [holder] : [holder, writer];
                await Task.WhenAll(pending);
            }

            await Assert.That(observed).IsSameReferenceAs(writerReachedLock.Task);
            await Assert.That(transactionStartedBeforeRelease).IsFalse();
            await Assert.That(writerCompletedBeforeRelease).IsFalse();
            await using ExploreDbContext verificationContext = CreateContext(databasePath);
            var repository = new SystemSettingRepository(verificationContext,
                new RelationalSettingMutationLock(verificationContext, new EfCoreUnitOfWork(verificationContext)));
            var persisted = await repository.GetByKey(settingKey);
            await Assert.That(persisted!.Value).IsEqualTo(value);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    public async Task CommittedInstanceDisable_SkipsOptionalDispatchWithoutAttemptOrReceipt()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-delivery-fence-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch dispatch;
            await using (ExploreDbContext context = CreateContext(databasePath))
            {
                dispatch = await SeedProcessingDispatchAsync(context, "committed-disable");
                var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
                await DisableAsync(context, mutationLock);
            }

            await using (ExploreDbContext context = CreateContext(databasePath))
            {
                var result = await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(Request(dispatch));
                await AssertSuppressedResultAsync(result);
            }
            await AssertNoHandoffAsync(databasePath, dispatch);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    public async Task DisableOwnsPolicyLock_HandoffWaitsAndObservesCommittedDisable()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-delivery-fence-race-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch dispatch;
            await using (ExploreDbContext context = CreateContext(databasePath))
                dispatch = await SeedProcessingDispatchAsync(context, "racing-disable");

            await using ExploreDbContext disableContext = CreateContext(databasePath);
            await using ExploreDbContext handoffContext = CreateContext(databasePath);
            using var operationCancellation = new CancellationTokenSource();
            var disableOwnsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var commitDisable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handoffReachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disableLock = new RelationalSettingMutationLock(disableContext, new EfCoreUnitOfWork(disableContext));
            var handoffLock = new RelationalSettingMutationLock(handoffContext, new EfCoreUnitOfWork(handoffContext),
                (key, _) =>
                {
                    if (key == GovernanceSettingKeys.Email.DeliveryEnabled)
                        handoffReachedLock.TrySetResult();
                    return Task.CompletedTask;
                });

            Task disable = disableLock.ExecuteOrderedGroupsAsync(
                [EmailDeliverySettingKeys.All], async token =>
                {
                    disableOwnsLock.SetResult();
                    await commitDisable.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                    return await new EfCoreUnitOfWork(disableContext).ExecuteSerializableAsync(async transactionToken =>
                    {
                        await DisableAsync(disableContext, disableLock, transactionToken);
                        return true;
                    }, token);
                }, operationCancellation.Token);
            Task<EmailDispatchEligibilityResult>? handoff = null;
            try
            {
                await disableOwnsLock.Task.WaitAsync(TimeSpan.FromSeconds(15));
                handoff = CreateEvaluator(handoffContext, handoffLock)
                    .EvaluateAndBeginProviderHandoffAsync(Request(dispatch), operationCancellation.Token);
                Task observed = await Task.WhenAny(handoffReachedLock.Task, handoff)
                    .WaitAsync(TimeSpan.FromSeconds(15));

                await Assert.That(observed).IsSameReferenceAs(handoffReachedLock.Task);
                await Assert.That(handoff.IsCompleted).IsFalse();
            }
            finally
            {
                commitDisable.TrySetResult();
                operationCancellation.CancelAfter(TimeSpan.FromSeconds(15));
                Task[] pending = handoff is null ? [disable] : [disable, handoff];
                await Task.WhenAll(pending);
            }

            await AssertSuppressedResultAsync(await handoff!);
            await AssertNoHandoffAsync(databasePath, dispatch);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static Task DisableAsync(ExploreDbContext context, ISettingMutationLock mutationLock,
        CancellationToken cancellationToken = default) =>
        ConfirmEmailDisableAsync(context, mutationLock: mutationLock, cancellationToken: cancellationToken);

    [Test]
    public async Task RequiredDeliveryWithoutHost_ParksWithoutAttemptOrAutomaticClaim()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-delivery-fence-unconfigured-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch dispatch;
            await using (ExploreDbContext context = CreateContext(databasePath))
            {
                dispatch = await SeedProcessingDispatchAsync(context, "unconfigured");
                var delivery = await context.NotificationDeliveries
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
                delivery.DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired;
                delivery.IsRequired = true;
                await context.SaveChangesAsync();
                await SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpHost, "\"\"");
            }

            await using (ExploreDbContext context = CreateContext(databasePath))
            {
                var result = await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(Request(dispatch));
                await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Parked);
                await Assert.That(result.SkipReason).IsEqualTo("email_capability_unavailable");
                await Assert.That(result.RecipientEmail).IsNull();
                await Assert.That(result.ReceiptId).IsNull();
                await Assert.That(result.AttemptNumber).IsNull();
            }
            await AssertNoHandoffAsync(databasePath, dispatch, expectedStatus: EmailDispatchStatus.Parked,
                expectedDeliveryStatus: NotificationDeliveryStatusEnum.Parked);

            await using ExploreDbContext claimContext = CreateContext(databasePath);
            var repository = new EmailDispatchOutboxRepository(claimContext);
            var persisted = await repository.GetByTenantAndId(dispatch.TenantId, dispatch.OutboxId, CancellationToken.None);
            await Assert.That(persisted!.ParkedAt).IsNotNull();
            await Assert.That(persisted.NextAttemptAt).IsNull();
            var claimed = await repository.TryClaimSpecificAsync(new EmailDispatchSpecificClaimRequest(
                TenantId: dispatch.TenantId, PublishEventId: persisted.PublishEventId,
                LeaseToken: Guid.CreateVersion7(), GlobalProcessingLimit: 10, TenantProcessingLimit: 5,
                OptionalReminderBacklogHighWatermark: 100, OptionalReminderBacklogLowWatermark: 50,
                ClaimedAt: DateTime.UtcNow), CancellationToken.None);
            await Assert.That(claimed).IsNull();
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static EmailDispatchEligibilityRequest Request(SeededDispatch dispatch) =>
        new(TenantId: dispatch.TenantId, OutboxId: dispatch.OutboxId,
            ProcessingLeaseToken: dispatch.LeaseToken, AttemptNumber: 0,
            GlobalSmtpRateLimitPerMinute: 60, TenantSmtpRateLimitPerMinute: 60,
            ConsumerId: "email-policy-fence-test", EvaluatedAt: DateTime.UtcNow);

    private static async Task AssertSuppressedResultAsync(EmailDispatchEligibilityResult result)
    {
        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("email_delivery_disabled");
        await Assert.That(result.RecipientEmail).IsNull();
        await Assert.That(result.ReceiptId).IsNull();
        await Assert.That(result.AttemptNumber).IsNull();
    }

    private static async Task AssertNoHandoffAsync(string databasePath, SeededDispatch dispatch,
        EmailDispatchStatus expectedStatus = EmailDispatchStatus.Skipped,
        NotificationDeliveryStatusEnum expectedDeliveryStatus = NotificationDeliveryStatusEnum.Skipped)
    {
        await using ExploreDbContext context = CreateContext(databasePath);
        var persisted = await new EmailDispatchOutboxRepository(context)
            .GetByTenantAndId(dispatch.TenantId, dispatch.OutboxId, CancellationToken.None);
        var delivery = await context.NotificationDeliveries
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue).AsNoTracking()
            .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);

        await Assert.That(persisted!.Status).IsEqualTo(expectedStatus);
        await Assert.That(persisted.AttemptCount).IsEqualTo(0);
        await Assert.That(persisted.ProcessingLeaseToken).IsNull();
        await Assert.That(delivery.StatusId).IsEqualTo((int)expectedDeliveryStatus);
        await Assert.That(await context.EmailDispatchAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .CountAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId))
            .IsEqualTo(0);
        await Assert.That(await context.EmailDispatchReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .CountAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId))
            .IsEqualTo(0);
    }

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-shm");
        File.Delete(databasePath + "-wal");
    }

    private sealed class TransactionStartedObserver : DbTransactionInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override DbTransaction TransactionStarted(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result)
        {
            Started.TrySetResult();
            return result;
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
