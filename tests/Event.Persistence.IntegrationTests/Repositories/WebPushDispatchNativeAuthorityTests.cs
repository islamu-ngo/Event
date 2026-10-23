using Explore.Persistence.Database.ProviderPrimitives;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Explore.Infrastructure.WebPush;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebPushDispatchNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GlobalDiscoveryAndClaimReachBothTenantsWithoutPrivateTableAuthority()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var first = await SeedAsync(seed, "push-native-a");
        var second = await SeedAsync(seed, "push-native-b");
        await using var authority = await CreateAuthorityAsync(seed);
        await using var global = await authority.Global.CreateDbContextAsync();
        var repository = authority.Admission;
        DateTime now = DateTime.UtcNow;
        var pending = await repository.GetPendingBatch(2, now);
        await Assert.That(pending.Select(row => row.Id)).IsEquivalentTo([first.Id, second.Id]);
        Guid lease = Guid.CreateVersion7();
        await Assert.That(await repository.TryMarkAsProcessing(new WebPushDispatchOwner(second.TenantId, first.Id), lease, now)).IsFalse();
        await Assert.That(await repository.TryMarkAsProcessing(new WebPushDispatchOwner(first.TenantId, first.Id), lease, now)).IsTrue();
        await Assert.That(await repository.TryMarkAsProcessing(new WebPushDispatchOwner(first.TenantId, first.Id), Guid.CreateVersion7(), now)).IsFalse();
        var persisted = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == first.Id);
        await Assert.That(persisted.ProcessingLeaseToken).IsEqualTo(lease);
        await Assert.That(persisted.AttemptCount).IsEqualTo(1);
        await RequireDenialAsync(global, Table<WebPushDispatchOutbox>(seed));
        await RequireDenialAsync(global, Table<WebPushSubscription>(seed));
        await RequireDenialAsync(global, Table<UserPii>(seed));
    }

    [Test]
    public async Task GlobalRecoveryReleasesBothTenantsAndRejectsTheExpiredLease()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var first = await SeedAsync(seed, "push-recover-a");
        var second = await SeedAsync(seed, "push-recover-b");
        DateTime now = DateTime.UtcNow;
        Guid expiredLease = Guid.CreateVersion7();
        await seed.WebPushDispatchOutbox.Where(row => row.Id == first.Id || row.Id == second.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, WebPushDispatchStatus.Processing)
                .SetProperty(row => row.ProcessingStartedAt, now.AddMinutes(-30))
                .SetProperty(row => row.ProcessingLeaseToken, expiredLease)
                .SetProperty(row => row.AttemptCount, 1));
        await using var authority = await CreateAuthorityAsync(seed);
        await using var global = await authority.Global.CreateDbContextAsync();
        var admission = authority.Admission;
        await Assert.That(await admission.RecoverStaleProcessing(
            now.AddMinutes(-10), now, "lease_timeout", "Lease expired.", 2)).IsEqualTo(2);
        await using var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId);
        var repository = new WebPushDispatchOutboxRepository(runtime);
        await Assert.That(await repository.MarkAsDelivered(first.Id, expiredLease, now)).IsFalse();
        var persisted = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == first.Id);
        await Assert.That(persisted.Status).IsEqualTo(WebPushDispatchStatus.RetryScheduled);
        await Assert.That(persisted.ProcessingLeaseToken).IsNull();
        await Assert.That(persisted.AttemptCount).IsEqualTo(1);
    }

    [Test]
    public async Task OrdinaryAcquisitionSealsExactTenantAndFencesCrossTenantSettlement()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var first = await SeedAsync(seed, "push-runtime-a");
        var second = await SeedAsync(seed, "push-runtime-b");
        Guid lease = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;
        await seed.WebPushDispatchOutbox.Where(row => row.Id == first.Id || row.Id == second.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, WebPushDispatchStatus.Processing)
                .SetProperty(row => row.ProcessingStartedAt, now)
                .SetProperty(row => row.ProcessingLeaseToken, lease));
        await using var authority = await CreateAuthorityAsync(seed);
        await using (var runtime = await authority.Runtime.CreateDbContextAsync(first.TenantId))
        {
            await Assert.That(runtime.TenantContext!.TenantId).IsEqualTo(first.TenantId);
            var repository = new WebPushDispatchOutboxRepository(runtime);
            await Assert.That(await repository.GetSenderEntryClaimAsync(
                second.TenantId, second.Id, lease, now.AddMinutes(-15), now)).IsNull();
            await Assert.That(await repository.GetSenderEntryClaimAsync(
                first.TenantId, first.Id, Guid.CreateVersion7(), now.AddMinutes(-15), now)).IsNull();
            await Assert.That(await repository.GetSenderEntryClaimAsync(
                first.TenantId, first.Id, lease, now.AddMinutes(-15), now)).IsNotNull();
            await Assert.That(await repository.MarkAsDelivered(second.Id, lease, now)).IsFalse();
            await Assert.That(await repository.MarkAsDelivered(first.Id, Guid.CreateVersion7(), now)).IsFalse();
            await Assert.That(await repository.MarkAsDelivered(first.Id, lease, now)).IsTrue();
            await Assert.That(await runtime.WebPushDispatchOutbox.IgnoreQueryFilters().CountAsync()).IsEqualTo(1);
        }
        await using var next = await authority.Runtime.CreateDbContextAsync(second.TenantId);
        await Assert.That(next.TenantContext!.TenantId).IsEqualTo(second.TenantId);
        await Assert.That(await next.WebPushDispatchOutbox.IgnoreQueryFilters()
            .Select(row => row.Id).SingleAsync()).IsEqualTo(second.Id);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(() => authority.Runtime.CreateDbContextAsync(first.TenantId, cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task GlobalAcquisitionCannotReadPrivateWebPushDataOrElevate()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await SeedAsync(seed, "push-denied");
        await using var authority = await CreateAuthorityAsync(seed);
        await using var global = await authority.Global.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await using (var command = global.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user";
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetBoolean(0)).IsFalse();
            await Assert.That(reader.GetBoolean(1)).IsFalse();
        }
        await Assert.That(global.TenantContext).IsNull();
        await RequireDenialAsync(global, Table<WebPushDispatchOutbox>(seed));
        await RequireDenialAsync(global, Table<WebPushSubscription>(seed));
        await RequireStatementDenialAsync(global, "SET ROLE postgres");
        await RequireStatementDenialAsync(global, $"SET ROLE {PostgresWebPushDispatchAdmissionContract.OwnerRole}");
        await RequireStatementDenialAsync(global, $"SET ROLE {PostgresWebPushDispatchAdmissionContract.MigratorRole}");
        await using var runtime = await authority.Runtime.CreateDbContextAsync(
            await seed.WebPushDispatchOutbox.Select(row => row.TenantId).SingleAsync());
        await RequireStatementDenialAsync(runtime,
            $"SELECT * FROM {PostgresWebPushDispatchAdmissionContract.DiscoveryFunctionSql(runtime)}(now(), 1)");
        await Assert.That(() => authority.Admission.GetPendingBatch(
            IWebPushDispatchAdmission.MaximumBatchSize + 1, DateTime.UtcNow)).Throws<PostgresException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NativeDrainUsesFreshTenantAuthorityAndPersistsProviderOutcomes(int outcome)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var dispatch = await SeedAsync(seed, "push-drain");
        var expired = await SeedAsync(seed, "push-expired");
        await seed.WebPushDispatchOutbox.Where(row => row.Id == expired.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, DateTime.UtcNow.AddHours(-7)));
        await using var authority = await CreateAuthorityAsync(seed);
        var sender = new RecordingSender((_, _) => Task.FromResult(outcome switch
        {
            0 => WebPushSendResult.Succeeded(201),
            1 => WebPushSendResult.StaleSubscription(410, "Endpoint retired."),
            2 => WebPushSendResult.Retryable(429, "Provider throttled."),
            _ => WebPushSendResult.PermanentNonRetryable(400, "Invalid provider request.")
        }));
        await using var services = SenderServices(sender);
        var drain = CreateDrain(services, authority.Admission, authority.Execution);

        var result = await drain.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.Pending).IsEqualTo(2);
        await Assert.That(result.Skipped).IsEqualTo(1);
        await Assert.That(result.StaleLease).IsEqualTo(0);
        await Assert.That(sender.Envelopes.Count).IsEqualTo(1);
        await Assert.That(sender.Envelopes[0].CorrelationId).IsEqualTo(dispatch.Id.ToString());
        await Assert.That(sender.Envelopes[0].PayloadJson).DoesNotContain(dispatch.UserId.ToString());
        var persisted = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        await Assert.That(persisted.AttemptCount).IsEqualTo(1);
        await Assert.That(persisted.ProcessingLeaseToken).IsNull();
        WebPushDispatchStatus expected = outcome switch
        {
            0 => WebPushDispatchStatus.Delivered,
            1 => WebPushDispatchStatus.PermanentFailed,
            2 => WebPushDispatchStatus.RetryScheduled,
            _ => WebPushDispatchStatus.DeadLettered
        };
        await Assert.That(persisted.Status).IsEqualTo(expected);
        var subscription = await seed.WebPushSubscriptions.AsNoTracking().SingleAsync(row => row.Id == dispatch.SubscriptionId);
        await Assert.That(subscription.IsActive).IsEqualTo(outcome != 1);
        if (outcome == 2)
            await Assert.That(persisted.NextAttemptAt).IsNotNull();
        await using var runtime = await authority.Runtime.CreateDbContextAsync(dispatch.TenantId);
        await RequireDenialAsync(runtime, Table<UserPii>(seed));
    }

    [Test]
    public async Task LateProviderSuccessCannotSettleTheSuccessorLease()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var dispatch = await SeedAsync(seed, "push-successor");
        await using var authority = await CreateAuthorityAsync(seed);
        Guid successor = Guid.CreateVersion7();
        var sender = new RecordingSender(async (_, token) =>
        {
            await seed.WebPushDispatchOutbox.Where(row => row.Id == dispatch.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProcessingLeaseToken, successor), token);
            return WebPushSendResult.Succeeded(201);
        });
        await using var services = SenderServices(sender);
        var result = await CreateDrain(services, authority.Admission, authority.Execution)
            .ProcessBatchAsync(CancellationToken.None);
        await Assert.That(result.StaleLease).IsEqualTo(1);
        await Assert.That(result.Delivered).IsEqualTo(0);
        var persisted = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        await Assert.That(persisted.Status).IsEqualTo(WebPushDispatchStatus.Processing);
        await Assert.That(persisted.ProcessingLeaseToken).IsEqualTo(successor);
        await Assert.That(persisted.AttemptCount).IsEqualTo(1);
        await Assert.That(persisted.DeliveredAt).IsNull();
    }

    [Test]
    public async Task CancellationBeforeExecutionPreservesCommittedClaimForNativeRecovery()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var dispatch = await SeedAsync(seed, "push-cancel");
        await using var authority = await CreateAuthorityAsync(seed);
        using var cancellation = new CancellationTokenSource();
        var execution = new BeforeExecutionFactory(authority.Execution, () =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        var sender = new RecordingSender((_, _) => Task.FromResult(WebPushSendResult.Succeeded(201)));
        await using var services = SenderServices(sender);
        var drain = CreateDrain(services, authority.Admission, execution);
        await Assert.That(() => drain.ProcessBatchAsync(cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(sender.Envelopes).IsEmpty();
        var persisted = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        await Assert.That(persisted.Status).IsEqualTo(WebPushDispatchStatus.Processing);
        await Assert.That(persisted.ProcessingLeaseToken).IsNotNull();
        await Assert.That(persisted.AttemptCount).IsEqualTo(1);
        await Assert.That(await authority.Admission.RecoverStaleProcessing(
            persisted.ProcessingStartedAt!.Value, DateTime.UtcNow, "cancelled", "Cancelled before execution.", 1))
            .IsEqualTo(1);
        var recovered = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        await Assert.That(recovered.Status).IsEqualTo(WebPushDispatchStatus.RetryScheduled);
        await Assert.That(recovered.ProcessingLeaseToken).IsNull();
        await Assert.That(recovered.AttemptCount).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentGlobalClaimersCreateOneDurableAttempt()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var dispatch = await SeedAsync(seed, "push-concurrent");
        await using var authority = await CreateAuthorityAsync(seed);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> ClaimAsync()
        {
            await start.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return await authority.Admission.TryMarkAsProcessing(
                new WebPushDispatchOwner(dispatch.TenantId, dispatch.Id), Guid.CreateVersion7(), DateTime.UtcNow);
        }
        Task<bool> first = ClaimAsync();
        Task<bool> second = ClaimAsync();
        start.SetResult();
        bool[] claims = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(claims.Count(claimed => claimed)).IsEqualTo(1);
        var persisted = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        await Assert.That(persisted.AttemptCount).IsEqualTo(1);
        await Assert.That(persisted.Status).IsEqualTo(WebPushDispatchStatus.Processing);
        await Assert.That(persisted.ProcessingLeaseToken).IsNotNull();
    }

    [Test]
    public async Task GlobalHealthAggregatesPreserveDueStatusAndStaleCutoffPredicatesAcrossTenants()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var first = await SeedAsync(seed, "push-count-a");
        var second = await SeedAsync(seed, "push-count-b");
        DateTime now = DateTime.UtcNow;
        DateTime cutoff = now.AddHours(-1);
        second.Status = WebPushDispatchStatus.RetryScheduled;
        second.NextAttemptAt = now;
        WebPushDispatchOutbox Row(WebPushDispatchOutbox owner, WebPushDispatchStatus status) => new()
        {
            TenantId = owner.TenantId,
            Tenant = null!,
            NotificationId = Guid.CreateVersion7(),
            CategoryId = owner.CategoryId,
            Category = null!,
            SubscriptionId = owner.SubscriptionId,
            Subscription = null!,
            UserId = owner.UserId,
            User = null!,
            PayloadJson = "{}",
            Status = status
        };
        var futureRetry = Row(first, WebPushDispatchStatus.RetryScheduled);
        futureRetry.NextAttemptAt = now.AddDays(1);
        var futurePending = Row(second, WebPushDispatchStatus.Pending);
        futurePending.NextAttemptAt = now.AddDays(1);
        var boundary = Row(first, WebPushDispatchStatus.Processing);
        boundary.ProcessingStartedAt = cutoff;
        var before = Row(second, WebPushDispatchStatus.Processing);
        before.ProcessingStartedAt = cutoff.AddSeconds(-1);
        var after = Row(first, WebPushDispatchStatus.Processing);
        after.ProcessingStartedAt = cutoff.AddSeconds(1);
        var missingStart = Row(second, WebPushDispatchStatus.Processing);
        var deleted = Row(second, WebPushDispatchStatus.RetryScheduled);
        deleted.IsDeleted = true;
        seed.AddRange(futureRetry, futurePending, boundary, before, after, missingStart, deleted,
            Row(first, WebPushDispatchStatus.DeadLettered), Row(second, WebPushDispatchStatus.PermanentFailed),
            Row(first, WebPushDispatchStatus.Delivered));
        await seed.SaveChangesAsync();
        await using var authority = await CreateAuthorityAsync(seed);

        await Assert.That(await authority.Admission.CountDueDispatchAsync(now)).IsEqualTo(2);
        await Assert.That(await authority.Admission.CountRetryScheduledAsync()).IsEqualTo(2);
        await Assert.That(await authority.Admission.CountStaleProcessingAsync(cutoff)).IsEqualTo(2);
        await Assert.That(await authority.Admission.CountTerminalFailureAsync()).IsEqualTo(2);
        var pending = await authority.Admission.GetPendingBatch(10, now);
        await Assert.That(pending.Select(row => row.Id)).IsEquivalentTo([first.Id, second.Id]);
        await using var global = await authority.Global.CreateDbContextAsync();
        await RequireDenialAsync(global, Table<WebPushDispatchOutbox>(seed));
        await RequireDenialAsync(global, Table<WebPushSubscription>(seed));
        await RequireDenialAsync(global, Table<NotificationChannelPreference>(seed));
        await RequireDenialAsync(global, Table<PrivacyErasureSaga>(seed));
    }

    [Test]
    public async Task GlobalAggregateAuthorityRejectsPrivateProjectionInvalidModesAndOrdinaryExecution()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var dispatch = await SeedAsync(seed, "push-count-denial");
        await using var authority = await CreateAuthorityAsync(seed);
        await using var global = await authority.Global.CreateDbContextAsync();
        string counts = PostgresWebPushDispatchAdmissionContract.CountFunctionSql(global);
        await Assert.That(await authority.Admission.CountDueDispatchAsync(DateTime.UtcNow)).IsEqualTo(1);
        await RequireStatementDenialAsync(global,
            $"SELECT {Column<WebPushDispatchOutbox>(seed, nameof(WebPushDispatchOutbox.PayloadJson))} FROM {Table<WebPushDispatchOutbox>(seed)}");
        await RequireStatementDenialAsync(global, $"SELECT {counts}(5, NULL)", "22023");
        await RequireStatementDenialAsync(global, $"SELECT {counts}(1, NULL)", "22023");
        await RequireStatementDenialAsync(global, $"SELECT {counts}(2, now())", "22023");
        await using var runtime = await authority.Runtime.CreateDbContextAsync(dispatch.TenantId);
        await RequireStatementDenialAsync(runtime, $"SELECT {counts}(2, NULL)");
        await Assert.That(await seed.WebPushDispatchOutbox.AsNoTracking()
            .Where(row => row.Id == dispatch.Id).Select(row => row.Status).SingleAsync())
            .IsEqualTo(WebPushDispatchStatus.Pending);
    }

    private static ServiceProvider SenderServices(RecordingSender sender) =>
        new ServiceCollection().AddSingleton<IWebPushNotificationSender>(sender).BuildServiceProvider();

    [Test]
    [Arguments("successor")]
    [Arguments("erasure")]
    [Arguments("retention")]
    [Arguments("unsubscribe")]
    [Arguments("deleted-user")]
    [Arguments("lease-expired")]
    [Arguments("ttl-expired")]
    [Arguments("ttl-subsecond")]
    [Arguments("subscription-expired")]
    [Arguments("other-recipient")]
    [Arguments("cancelled")]
    [Arguments("eligible")]
    public async Task PersistedInvalidationAfterSubscriptionReadPreventsSenderEntry(string invalidation)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var dispatch = await SeedAsync(seed, "push-entry");
        DateTime createdAt = await seed.WebPushDispatchOutbox.AsNoTracking()
            .Where(row => row.Id == dispatch.Id).Select(row => row.CreatedAt).SingleAsync();
        var clock = new EntryClock(new DateTimeOffset(createdAt));
        await using var authority = await CreateAuthorityAsync(seed);
        using var cancellation = new CancellationTokenSource();
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new SubscriptionReadFactory(authority.Execution, async () =>
        {
            read.SetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var sender = new RecordingSender((_, _) => Task.FromResult(WebPushSendResult.Succeeded(201)));
        await using var services = SenderServices(sender);
        var drain = new WebPushDispatchDrainService(
            services.GetRequiredService<IServiceScopeFactory>(), authority.Admission, execution,
            Options.Create(new WebPushSettings
            {
                Enabled = true,
                BatchSize = 10,
                MaxAttemptCount = 5,
                ProcessingLeaseTimeoutSeconds = 30000
            }), clock, NullLogger<WebPushDispatchDrainService>.Instance);
        Task<WebPushDispatchDrainResult> processing = drain.ProcessBatchAsync(cancellation.Token);
        Guid successor = Guid.CreateVersion7();
        try
        {
            await read.Task.WaitAsync(TimeSpan.FromSeconds(30));
            switch (invalidation)
            {
                case "successor":
                    await seed.WebPushDispatchOutbox.Where(row => row.Id == dispatch.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProcessingLeaseToken, successor));
                    break;
                case "erasure":
                    DateTime now = clock.GetUtcNow().UtcDateTime;
                    var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
                        PrivacyErasureSubjectKind.User, dispatch.UserId,
                        PrivacyErasureReasonCode.AccountDeletion, 1, now, now);
                    seed.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(intent, 1,
                        RandomNumberGenerator.GetBytes(32), now.AddMinutes(5), now));
                    await seed.SaveChangesAsync();
                    break;
                case "retention":
                    await seed.WebPushDispatchOutbox.Where(row => row.Id == dispatch.Id).ExecuteDeleteAsync();
                    break;
                case "unsubscribe":
                    await new WebPushSubscriptionRepository(seed).UnsubscribeAsync(
                        dispatch.TenantId, dispatch.UserId, dispatch.SubscriptionId, clock.GetUtcNow().UtcDateTime);
                    break;
                case "deleted-user":
                    await seed.Users.Where(row => row.Id == dispatch.UserId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
                    break;
                case "lease-expired":
                    await seed.WebPushDispatchOutbox.Where(row => row.Id == dispatch.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProcessingStartedAt,
                            clock.GetUtcNow().UtcDateTime.AddSeconds(-30000)));
                    break;
                case "ttl-expired":
                    clock.Advance(TimeSpan.FromSeconds(21600));
                    break;
                case "ttl-subsecond":
                    clock.Advance(TimeSpan.FromMilliseconds(21599500));
                    break;
                case "subscription-expired":
                    await seed.WebPushSubscriptions.Where(row => row.Id == dispatch.SubscriptionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpirationTime,
                            clock.GetUtcNow().UtcDateTime));
                    break;
                case "other-recipient":
                    var other = await SeedAsync(seed, "push-other-recipient");
                    await seed.WebPushSubscriptions.Where(row => row.Id == dispatch.SubscriptionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UserId, other.UserId));
                    break;
                case "cancelled":
                    cancellation.Cancel();
                    break;
                case "eligible":
                    clock.Advance(TimeSpan.FromSeconds(17));
                    break;
            }
        }
        finally
        {
            resume.TrySetResult();
        }

        if (invalidation == "cancelled")
        {
            await Assert.That(async () => await processing.WaitAsync(TimeSpan.FromSeconds(30)))
                .Throws<OperationCanceledException>();
            await Assert.That(sender.Envelopes).IsEmpty();
            return;
        }

        var result = await processing.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(sender.Envelopes.Count).IsEqualTo(invalidation == "eligible" ? 1 : 0);
        if (invalidation == "eligible")
        {
            await Assert.That(result.Delivered).IsEqualTo(1);
            await Assert.That(sender.Envelopes[0].TimeToLiveSeconds).IsEqualTo(21583);
        }
        else if (invalidation is "ttl-expired" or "ttl-subsecond")
        {
            await Assert.That(result.Skipped).IsEqualTo(1);
            var expired = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
            await Assert.That(expired.LastFailureCategory).IsEqualTo("web_push_ttl_expired");
            await Assert.That(expired.ProcessingLeaseToken).IsNull();
        }
        else
        {
            await Assert.That(result.StaleLease).IsEqualTo(1);
        }
        if (invalidation == "successor")
        {
            var persisted = await seed.WebPushDispatchOutbox.AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
            await Assert.That(persisted.ProcessingLeaseToken).IsEqualTo(successor);
            await Assert.That(persisted.Status).IsEqualTo(WebPushDispatchStatus.Processing);
            await Assert.That(persisted.DeliveredAt).IsNull();
        }
    }

    private sealed class EntryClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan elapsed) => now += elapsed;
    }

    private sealed class SubscriptionReadFactory(
        IWebPushDispatchExecutionScopeFactory inner, Func<Task> afterRead) : IWebPushDispatchExecutionScopeFactory
    {
        private bool _wrapped;

        public async Task<IWebPushDispatchExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            var scope = await inner.CreateAsync(tenantId, cancellationToken);
            if (_wrapped) return scope;
            _wrapped = true;
            return new ReadScope(scope, afterRead);
        }

        private sealed class ReadScope(IWebPushDispatchExecutionScope inner, Func<Task> afterRead)
            : IWebPushDispatchExecutionScope
        {
            public IWebPushDispatchOutboxRepository DispatchRepository => inner.DispatchRepository;
            public IWebPushSubscriptionRepository SubscriptionRepository { get; } =
                new ReadSubscriptionRepository(inner.SubscriptionRepository, afterRead);
            public IPrivacyErasureStateRepository PrivacyErasureStateRepository => inner.PrivacyErasureStateRepository;
            public Explore.Application.Contracts.Services.INotificationPreferenceResolver PreferenceResolver => inner.PreferenceResolver;
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }

        private sealed class ReadSubscriptionRepository(IWebPushSubscriptionRepository inner, Func<Task> afterRead)
            : IWebPushSubscriptionRepository
        {
            public async Task<WebPushSubscription?> GetActiveByIdAsync(
                Guid tenantId, Guid subscriptionId, CancellationToken cancellationToken = default)
            {
                var subscription = await inner.GetActiveByIdAsync(tenantId, subscriptionId, cancellationToken);
                await afterRead();
                return subscription;
            }

            public Task<WebPushSubscription?> GetById(Guid id) => inner.GetById(id);
            public Task<IReadOnlyList<WebPushSubscription>> GetAll() => inner.GetAll();
            public Task<(IReadOnlyList<WebPushSubscription> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) =>
                inner.GetAllPaged(pageNumber, pageSize);
            public Task<bool> Exists(Guid id) => inner.Exists(id);
            public Task<WebPushSubscription> Create(WebPushSubscription entity) => inner.Create(entity);
            public Task Update(WebPushSubscription entity) => inner.Update(entity);
            public Task Delete(WebPushSubscription entity) => inner.Delete(entity);
            public Task<WebPushSubscription> UpsertAsync(Guid tenantId, Guid userId, string deviceIdentifier,
                string endpoint, string p256Dh, string authSecret, DateTime? expirationTime, DateTime now,
                CancellationToken cancellationToken = default) =>
                inner.UpsertAsync(tenantId, userId, deviceIdentifier, endpoint, p256Dh, authSecret, expirationTime, now, cancellationToken);
            public Task<WebPushSubscription?> GetActiveForDeviceAsync(Guid tenantId, Guid userId,
                string deviceIdentifier, CancellationToken cancellationToken = default) =>
                inner.GetActiveForDeviceAsync(tenantId, userId, deviceIdentifier, cancellationToken);
            public Task<WebPushSubscription?> GetActiveByEndpointAsync(string endpoint, CancellationToken cancellationToken = default) =>
                inner.GetActiveByEndpointAsync(endpoint, cancellationToken);
            public Task<IReadOnlyList<WebPushSubscription>> ListActiveForUserAsync(Guid tenantId, Guid userId,
                CancellationToken cancellationToken = default) => inner.ListActiveForUserAsync(tenantId, userId, cancellationToken);
            public Task<bool> UnsubscribeAsync(Guid tenantId, Guid userId, Guid subscriptionId, DateTime now,
                CancellationToken cancellationToken = default) => inner.UnsubscribeAsync(tenantId, userId, subscriptionId, now, cancellationToken);
            public Task<bool> UnsubscribeDeviceAsync(Guid tenantId, Guid userId, string deviceIdentifier, DateTime now,
                CancellationToken cancellationToken = default) => inner.UnsubscribeDeviceAsync(tenantId, userId, deviceIdentifier, now, cancellationToken);
        }
    }

    private static WebPushDispatchDrainService CreateDrain(
        ServiceProvider services, IWebPushDispatchAdmission admission, IWebPushDispatchExecutionScopeFactory execution) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), admission, execution,
            Options.Create(new WebPushSettings { Enabled = true, BatchSize = 10, MaxAttemptCount = 5 }),
            TimeProvider.System,
            NullLogger<WebPushDispatchDrainService>.Instance);

    private sealed class RecordingSender(
        Func<WebPushSendEnvelope, CancellationToken, Task<WebPushSendResult>> send) : IWebPushNotificationSender
    {
        internal List<WebPushSendEnvelope> Envelopes { get; } = [];

        public Task<WebPushSendResult> SendAsync(WebPushSendEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return send(envelope, cancellationToken);
        }
    }

    private sealed class BeforeExecutionFactory(
        IWebPushDispatchExecutionScopeFactory inner, Func<Task> before) : IWebPushDispatchExecutionScopeFactory
    {
        public async Task<IWebPushDispatchExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            await before();
            return await inner.CreateAsync(tenantId, cancellationToken);
        }
    }

    private async Task<Authority> CreateAuthorityAsync(ExploreDbContext seed)
    {
        await PostgresWebPushDispatchAdmissionContract.ApplyAsync(seed);
        string schema = seed.Model.FindEntityType(typeof(WebPushDispatchOutbox))!.GetSchema()!;
        string suffix = Guid.CreateVersion7().ToString("N");
        string globalRole = $"push_global_{suffix}";
        string runtimeRole = $"push_runtime_{suffix}";
        string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string tables = $"{Table<WebPushDispatchOutbox>(seed)}, {Table<WebPushSubscription>(seed)}";
        string reads = string.Join(", ", new[]
        {
            Table<PrivacyErasureSaga>(seed),
            Table<NotificationPreferenceCategory>(seed), Table<NotificationPreferenceChannel>(seed),
            Table<NotificationChannelPreference>(seed), Table<NotificationPreferenceProfile>(seed)
        }.Distinct(StringComparer.Ordinal));
        string userRead = $"SELECT ({Column<User>(seed, nameof(User.Id))}, {Column<User>(seed, nameof(User.IsDeleted))}) ON {Table<User>(seed)}";
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        command.CommandText = $"""
            CREATE ROLE "{globalRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{globalPassword}';
            CREATE ROLE "{runtimeRole}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{runtimePassword}';
            GRANT USAGE ON SCHEMA "{schema}" TO "{globalRole}", "{runtimeRole}";
            GRANT {PostgresWebPushDispatchAdmissionContract.GlobalRole} TO "{globalRole}";
            GRANT SELECT, UPDATE ON {tables} TO "{runtimeRole}";
            GRANT SELECT ON {reads} TO "{runtimeRole}";
            GRANT {userRead} TO "{runtimeRole}";
            """;
        await command.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        PrimaryDatabaseConnectionOptions Options(PrimaryDatabaseRole role, string username, string password) => new()
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = schema,
            Username = username,
            Password = password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
        return new Authority(fixture.ConnectionString, schema, tables, reads, userRead, globalRole, runtimeRole,
            Options(PrimaryDatabaseRole.Global, globalRole, globalPassword),
            Options(PrimaryDatabaseRole.Runtime, runtimeRole, runtimePassword));
    }

    private sealed class Authority(
        string administratorConnection, string schema, string tables, string reads, string userRead, string globalRole, string runtimeRole,
        PrimaryDatabaseConnectionOptions globalOptions, PrimaryDatabaseConnectionOptions runtimeOptions) : IAsyncDisposable
    {
        internal GlobalExploreDbContextFactory Global { get; } = new(globalOptions, poolSize: 1);
        internal ITenantExploreDbContextFactory Runtime { get; } = new TenantExploreDbContextFactory(runtimeOptions, poolSize: 1);
        internal IWebPushDispatchAdmission Admission => new WebPushDispatchAdmission(new GlobalFactory(Global));
        internal IWebPushDispatchExecutionScopeFactory Execution => new WebPushDispatchExecutionScopeFactory(Runtime);

        public async ValueTask DisposeAsync()
        {
            foreach (var options in new[] { globalOptions, runtimeOptions })
            {
                using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
                NpgsqlConnection.ClearPool(pool);
            }
            await using var administrator = new NpgsqlConnection(administratorConnection);
            await administrator.OpenAsync();
            await using var command = administrator.CreateCommand();
            command.CommandText = $"""
                REVOKE SELECT, UPDATE ON {tables} FROM "{runtimeRole}";
                REVOKE SELECT ON {reads} FROM "{runtimeRole}";
                REVOKE {userRead} FROM "{runtimeRole}";
                REVOKE {PostgresWebPushDispatchAdmissionContract.GlobalRole} FROM "{globalRole}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{globalRole}", "{runtimeRole}";
                DROP ROLE "{globalRole}";
                DROP ROLE "{runtimeRole}";
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class GlobalFactory(GlobalExploreDbContextFactory factory) : IWebPushDispatchAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }

    private static string Table<T>(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    private static string Column<T>(ExploreDbContext context, string property)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        var mapping = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return $"\"{entity.FindProperty(property)!.GetColumnName(mapping)}\"";
    }

    private static Task RequireDenialAsync(ExploreDbContext context, string table) =>
        RequireStatementDenialAsync(context, $"SELECT * FROM {table}");

    private static async Task RequireStatementDenialAsync(
        ExploreDbContext context, string statement, string expectedSqlState = "42501")
    {
        string? state = null;
        try { await context.Database.ExecuteSqlRawAsync(statement); }
        catch (PostgresException exception) { state = exception.SqlState; }
        await Assert.That(state).IsEqualTo(expectedSqlState);
    }

    private static async Task<WebPushDispatchOutbox> SeedAsync(ExploreDbContext context, string slug)
    {
        var tenant = new Tenant
        {
            FullName = slug,
            Slug = $"{slug}-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii { Email = $"{Guid.CreateVersion7():N}@example.test", FirstName = "Push", LastName = "Recipient" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var subscription = WebPushSubscription.Create(tenant.Id, user.Id, "native-device",
            $"https://push.example.test/{Guid.CreateVersion7():N}",
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), null, DateTime.UtcNow);
        var dispatch = new WebPushDispatchOutbox
        {
            TenantId = tenant.Id,
            Tenant = null!,
            NotificationId = Guid.CreateVersion7(),
            CategoryId = (int)NotificationPreferenceCategoryEnum.EventUpdates,
            Category = null!,
            SubscriptionId = subscription.Id,
            Subscription = null!,
            UserId = user.Id,
            User = null!,
            PayloadJson = "{\"type\":\"notification_refresh\"}"
        };
        context.AddRange(subscription, dispatch);
        await context.SaveChangesAsync();
        return dispatch;
    }
}
