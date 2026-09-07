using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class NotificationFanoutRunLeaseRepositoryTests(PostgreSqlContainerFixture fixture)
{
    private const int BacklogHighWatermark = 1000;
    private const int BacklogLowWatermark = 500;

    [Test]
    public async Task EnsurePendingOccurrenceRunAsync_ConcurrentReplayThenClaim_ReusesIndependentRun()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-ensure-replay", ensureRun: false);
        Guid candidateA = Guid.CreateVersion7();
        Guid candidateB = Guid.CreateVersion7();

        await using var contextA = fixture.CreateDbContext();
        await using var contextB = fixture.CreateDbContext();
        var repositoryA = new NotificationFanoutRunRepository(contextA);
        var repositoryB = new NotificationFanoutRunRepository(contextB);
        NotificationFanoutRun?[] concurrent = await Task.WhenAll(
            repositoryA.EnsurePendingOccurrenceRunAsync(
                scenario.TenantId,
                scenario.OccurrenceId,
                candidateA,
                CancellationToken.None),
            repositoryB.EnsurePendingOccurrenceRunAsync(
                scenario.TenantId,
                scenario.OccurrenceId,
                candidateB,
                CancellationToken.None));

        Guid runId = concurrent[0]?.Id
            ?? throw new InvalidOperationException("Concurrent ensure did not return a run.");
        await using var replayContext = fixture.CreateDbContext();
        var replayRepository = new NotificationFanoutRunRepository(replayContext);
        NotificationFanoutRun? replayed = await replayRepository.EnsurePendingOccurrenceRunAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            Guid.CreateVersion7(),
            CancellationToken.None);

        await using var claimContext = fixture.CreateDbContext();
        var claimRepository = new NotificationFanoutRunRepository(claimContext);
        NotificationFanoutClaim? claim = await claimRepository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "worker-after-handoff",
            Utc(2026, 8, 1, 12),
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None);

        await Assert.That(concurrent[1]).IsNotNull();
        await Assert.That(concurrent[1]!.Id).IsEqualTo(runId);
        await Assert.That(runId).IsNotEqualTo(scenario.OccurrenceId);
        await Assert.That(runId == candidateA || runId == candidateB).IsTrue();
        await Assert.That(replayed).IsNotNull();
        await Assert.That(replayed!.Id).IsEqualTo(runId);
        await Assert.That(replayed.Status).IsEqualTo("pending");
        await Assert.That(replayed.ProcessingLeaseToken).IsNull();
        await Assert.That(claim).IsNotNull();
        await Assert.That(claim!.RunId).IsEqualTo(runId);
    }

    [Test]
    public async Task EnsurePendingOccurrenceRunAsync_HeavySupersessionCommitsWhileWaiting_DoesNotCreatePendingRun()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync(
            "fanout-ensure-heavy-supersession-race",
            ensureRun: false);
        Guid replacementId = await SeedAdditionalOccurrenceAsync(
            scenario,
            "heavy-replacement",
            priority: 100,
            occurredAt: Utc(2026, 8, 1, 12),
            ensureRun: false);

        await using var heavyContext = fixture.CreateDbContext();
        await heavyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var heavyTransaction = await heavyContext.Database.BeginTransactionAsync();
            var occurrenceRepository = new NotificationFanoutOccurrenceRepository(heavyContext);
            await occurrenceRepository.AcquireEventPrecedenceLockAndHasHeavyAuthorityAsync(
                scenario.TenantId,
                scenario.EventId,
                CancellationToken.None);
            NotificationFanoutOccurrence occurrence = await heavyContext.NotificationFanoutOccurrences
                .SingleAsync(item => item.TenantId == scenario.TenantId
                    && item.Id == scenario.OccurrenceId);
            occurrence.Supersede(
                replacementId,
                "heavy_precedence",
                Utc(2026, 8, 1, 12));
            await heavyContext.SaveChangesAsync();

            await using var ensureContext = fixture.CreateDbContext();
            await ensureContext.Database.OpenConnectionAsync();
            int ensureBackendPid = await ensureContext.Database
                .SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
                .SingleAsync();
            var runRepository = new NotificationFanoutRunRepository(ensureContext);
            Task<NotificationFanoutRun?> ensureTask = runRepository.EnsurePendingOccurrenceRunAsync(
                scenario.TenantId,
                scenario.OccurrenceId,
                Guid.CreateVersion7(),
                CancellationToken.None);

            bool waitedOnEventLock;
            try
            {
                waitedOnEventLock = await WaitForAdvisoryLockWaiterAsync(ensureBackendPid);
            }
            finally
            {
                await heavyTransaction.CommitAsync();
            }

            NotificationFanoutRun? ensured = await ensureTask.WaitAsync(TimeSpan.FromSeconds(5));
            await using var verificationContext = fixture.CreateDbContext();
            int pendingRunCount = await verificationContext.NotificationFanoutRuns
                .CountAsync(run => run.TenantId == scenario.TenantId
                    && run.FanoutOccurrenceId == scenario.OccurrenceId
                    && run.Status == "pending");

            await Assert.That(waitedOnEventLock).IsTrue();
            await Assert.That(ensured).IsNull();
            await Assert.That(pendingRunCount).IsEqualTo(0);
        });
    }

    [Test]
    public async Task ClaimDueRoundAsync_SelectsOnePerTenantInPriorityThenTimeOrder()
    {
        await fixture.ResetAsync();
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        FanoutScenario tenantA = await SeedOccurrenceAsync(
            "fanout-fair-a",
            priority: 10,
            occurredAt: Utc(2026, 8, 1, 8));
        Guid tenantAHigh = await SeedAdditionalOccurrenceAsync(
            tenantA,
            "high",
            priority: 70,
            occurredAt: Utc(2026, 8, 1, 10));
        FanoutScenario tenantB = await SeedOccurrenceAsync(
            "fanout-fair-b",
            priority: 50,
            occurredAt: Utc(2026, 8, 1, 9));
        FanoutScenario tenantC = await SeedOccurrenceAsync(
            "fanout-fair-c",
            priority: 50,
            occurredAt: Utc(2026, 8, 1, 8));

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaimRoundResult round = await repository.ClaimDueRoundAsync(
            new NotificationFanoutClaimRoundRequest(
                "fair-worker",
                claimedAt,
                TimeSpan.FromMinutes(1),
                MaxTenants: 10,
                MaxActiveClaims: 10,
                MaxActiveClaimsPerTenant: 1,
                OptionalReminderBacklogHighWatermark: 1000,
                OptionalReminderBacklogLowWatermark: 500),
            CancellationToken.None);
        IReadOnlyList<NotificationFanoutClaim> claims = round.Claims;

        await Assert.That(claims).Count().IsEqualTo(3);
        await Assert.That(claims[0].OccurrenceId).IsEqualTo(tenantAHigh);
        await Assert.That(claims[1].OccurrenceId).IsEqualTo(tenantC.OccurrenceId);
        await Assert.That(claims[2].OccurrenceId).IsEqualTo(tenantB.OccurrenceId);
        await Assert.That(claims.Count(claim => claim.TenantId == tenantA.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task TryClaimOccurrenceAsync_DifferentRunsAcrossReplicas_EnforcesTenantCeiling()
    {
        await fixture.ResetAsync();
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-tenant-ceiling");
        Guid secondOccurrenceId = await SeedAdditionalOccurrenceAsync(
            scenario,
            "second",
            priority: 30,
            occurredAt: Utc(2026, 8, 1, 11).AddMinutes(1));

        await using var contextA = fixture.CreateDbContext();
        await using var contextB = fixture.CreateDbContext();
        var repositoryA = new NotificationFanoutRunRepository(contextA);
        var repositoryB = new NotificationFanoutRunRepository(contextB);
        NotificationFanoutClaim?[] claims = await Task.WhenAll(
            repositoryA.TryClaimOccurrenceAsync(
                scenario.TenantId,
                scenario.OccurrenceId,
                "ceiling-worker-a",
                claimedAt,
                TimeSpan.FromMinutes(1),
                8,
                1,
                BacklogHighWatermark,
                BacklogLowWatermark,
                CancellationToken.None),
            repositoryB.TryClaimOccurrenceAsync(
                scenario.TenantId,
                secondOccurrenceId,
                "ceiling-worker-b",
                claimedAt,
                TimeSpan.FromMinutes(1),
                8,
                1,
                BacklogHighWatermark,
                BacklogLowWatermark,
                CancellationToken.None));

        await Assert.That(claims.Count(claim => claim is not null)).IsEqualTo(1);
    }

    [Test]
    public async Task TryClaimOccurrenceAsync_DifferentTenantsAcrossReplicas_EnforcesGlobalCeiling()
    {
        await fixture.ResetAsync();
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        FanoutScenario first = await SeedOccurrenceAsync("fanout-global-ceiling-a");
        FanoutScenario second = await SeedOccurrenceAsync("fanout-global-ceiling-b");

        await using var contextA = fixture.CreateDbContext();
        await using var contextB = fixture.CreateDbContext();
        var repositoryA = new NotificationFanoutRunRepository(contextA);
        var repositoryB = new NotificationFanoutRunRepository(contextB);
        NotificationFanoutClaim?[] claims = await Task.WhenAll(
            repositoryA.TryClaimOccurrenceAsync(
                first.TenantId,
                first.OccurrenceId,
                "global-worker-a",
                claimedAt,
                TimeSpan.FromMinutes(1),
                maxActiveClaims: 1,
                maxActiveClaimsPerTenant: 1,
                optionalReminderBacklogHighWatermark: BacklogHighWatermark,
                optionalReminderBacklogLowWatermark: BacklogLowWatermark,
                cancellationToken: CancellationToken.None),
            repositoryB.TryClaimOccurrenceAsync(
                second.TenantId,
                second.OccurrenceId,
                "global-worker-b",
                claimedAt,
                TimeSpan.FromMinutes(1),
                maxActiveClaims: 1,
                maxActiveClaimsPerTenant: 1,
                optionalReminderBacklogHighWatermark: BacklogHighWatermark,
                optionalReminderBacklogLowWatermark: BacklogLowWatermark,
                cancellationToken: CancellationToken.None));

        await Assert.That(claims.Count(claim => claim is not null)).IsEqualTo(1);
    }

    [Test]
    public async Task ClaimDueRoundAsync_MaxActiveTwo_AllowsTwoRoundsThenStopsTenant()
    {
        await fixture.ResetAsync();
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-max-two", priority: 50);
        await SeedAdditionalOccurrenceAsync(
            scenario,
            "second",
            priority: 30,
            occurredAt: Utc(2026, 8, 1, 11).AddMinutes(1));
        await SeedAdditionalOccurrenceAsync(
            scenario,
            "third",
            priority: 10,
            occurredAt: Utc(2026, 8, 1, 11).AddMinutes(2));

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        var request = new NotificationFanoutClaimRoundRequest(
            "max-two-worker",
            claimedAt,
            TimeSpan.FromMinutes(1),
            MaxTenants: 10,
            MaxActiveClaims: 10,
            MaxActiveClaimsPerTenant: 2,
            OptionalReminderBacklogHighWatermark: 1000,
            OptionalReminderBacklogLowWatermark: 500);
        IReadOnlyList<NotificationFanoutClaim> first = (await repository.ClaimDueRoundAsync(
            request,
            CancellationToken.None)).Claims;
        IReadOnlyList<NotificationFanoutClaim> second = (await repository.ClaimDueRoundAsync(
            request,
            CancellationToken.None)).Claims;
        IReadOnlyList<NotificationFanoutClaim> blocked = (await repository.ClaimDueRoundAsync(
            request,
            CancellationToken.None)).Claims;

        await Assert.That(first).Count().IsEqualTo(1);
        await Assert.That(second).Count().IsEqualTo(1);
        await Assert.That(blocked).IsEmpty();
        await Assert.That(first[0].OccurrenceId).IsNotEqualTo(second[0].OccurrenceId);
    }

    [Test]
    public async Task ClaimDueRoundAsync_CoreBacklogDefersThenResumesOptionalReminder()
    {
        await fixture.ResetAsync();
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-backpressure-core", priority: 30);
        Guid reminderOccurrenceId = await SeedAdditionalOccurrenceAsync(
            scenario,
            "reminder",
            priority: 10,
            occurredAt: claimedAt.AddMinutes(-1),
            deliveryPolicy: NotificationDeliveryPolicyEnum.ReminderOptional);
        var request = new NotificationFanoutClaimRoundRequest(
            "backpressure-worker",
            claimedAt,
            TimeSpan.FromMinutes(1),
            MaxTenants: 10,
            MaxActiveClaims: 10,
            MaxActiveClaimsPerTenant: 2,
            OptionalReminderBacklogHighWatermark: 1,
            OptionalReminderBacklogLowWatermark: 0);

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaimRoundResult pressured = await repository.ClaimDueRoundAsync(
            request,
            CancellationToken.None);
        NotificationFanoutClaim coreClaim = pressured.Claims.Single();
        bool completed = await repository.TryCompleteAsync(
            coreClaim,
            claimedAt.AddSeconds(1),
            CancellationToken.None);
        NotificationFanoutClaimRoundResult resumed = await repository.ClaimDueRoundAsync(
            request with { ClaimedAt = claimedAt.AddSeconds(2) },
            CancellationToken.None);
        NotificationFanoutProcessorSnapshot snapshot = await repository.GetProcessorSnapshotAsync(
            claimedAt.AddSeconds(2),
            CancellationToken.None);

        await Assert.That(coreClaim.OccurrenceId).IsEqualTo(scenario.OccurrenceId);
        await Assert.That(completed).IsTrue();
        await Assert.That(resumed.Claims).Count().IsEqualTo(1);
        await Assert.That(resumed.Claims[0].OccurrenceId).IsEqualTo(reminderOccurrenceId);
        await Assert.That(snapshot.OptionalRemindersDeferred).IsFalse();
    }

    [Test]
    public async Task ClaimDueRoundAsync_ExcludesFutureSupersededFailedCompletedAndActiveRuns()
    {
        await fixture.ResetAsync();
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        FanoutScenario valid = await SeedOccurrenceAsync("fanout-state-valid");
        await SeedOccurrenceAsync(
            "fanout-state-future",
            notBefore: claimedAt.AddMinutes(1));
        FanoutScenario superseded = await SeedOccurrenceAsync("fanout-state-superseded");
        Guid replacementId = await SeedAdditionalOccurrenceAsync(
            superseded,
            "replacement",
            priority: 30,
            occurredAt: claimedAt,
            ensureRun: false);
        await SupersedeAsync(superseded, replacementId, claimedAt);
        FanoutScenario failed = await SeedOccurrenceAsync("fanout-state-failed");
        await SetRunStatusAsync(failed, "failed");
        FanoutScenario completed = await SeedOccurrenceAsync("fanout-state-completed");
        await SetRunStatusAsync(completed, "completed");
        FanoutScenario active = await SeedOccurrenceAsync("fanout-state-active");
        await using (var activeContext = fixture.CreateDbContext())
        {
            var activeRepository = new NotificationFanoutRunRepository(activeContext);
            NotificationFanoutClaim? activeClaim = await activeRepository.TryClaimOccurrenceAsync(
                active.TenantId,
                active.OccurrenceId,
                "active-worker",
                claimedAt,
                TimeSpan.FromMinutes(1),
                8,
                1,
                BacklogHighWatermark,
                BacklogLowWatermark,
                CancellationToken.None);
            await Assert.That(activeClaim).IsNotNull();
        }

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaimRoundResult round = await repository.ClaimDueRoundAsync(
            new NotificationFanoutClaimRoundRequest(
                "state-worker",
                claimedAt,
                TimeSpan.FromMinutes(1),
                MaxTenants: 10,
                MaxActiveClaims: 10,
                MaxActiveClaimsPerTenant: 1,
                OptionalReminderBacklogHighWatermark: 1000,
                OptionalReminderBacklogLowWatermark: 500),
            CancellationToken.None);
        IReadOnlyList<NotificationFanoutClaim> claims = round.Claims;

        await Assert.That(claims).Count().IsEqualTo(1);
        await Assert.That(claims[0].OccurrenceId).IsEqualTo(valid.OccurrenceId);
    }

    [Test]
    public async Task TryClaimOccurrenceAsync_MissingRun_DoesNotInventWorkerState()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-missing-run", ensureRun: false);

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaim? claim = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "missing-run-worker",
            Utc(2026, 8, 1, 12),
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None);
        NotificationFanoutRun? run = await repository.GetByOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            trackChanges: false,
            CancellationToken.None);

        await Assert.That(claim).IsNull();
        await Assert.That(run).IsNull();
    }

    [Test]
    public async Task TryClaimOccurrenceAsync_AlreadySuperseded_ReturnsNull()
    {
        await fixture.ResetAsync();
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-exact-superseded");
        Guid replacementId = await SeedAdditionalOccurrenceAsync(
            scenario,
            "replacement",
            priority: 30,
            occurredAt: claimedAt,
            ensureRun: false);
        await SupersedeAsync(scenario, replacementId, claimedAt);

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaim? claim = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "superseded-worker",
            claimedAt,
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None);

        await Assert.That(claim).IsNull();
    }

    [Test]
    public async Task TryClaimOccurrenceAsync_SupersessionCommitsWhileWaitingOnEventLock_ReturnsNull()
    {
        await fixture.ResetAsync();
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-claim-supersession-race");
        Guid replacementId = await SeedAdditionalOccurrenceAsync(
            scenario,
            "replacement",
            priority: 30,
            occurredAt: claimedAt,
            ensureRun: false);

        await using var blockingContext = fixture.CreateDbContext();
        await blockingContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var blockingTransaction = await blockingContext.Database.BeginTransactionAsync();
            string lockKey = $"notification-fanout-precedence:{scenario.TenantId:N}:{scenario.EventId:N}";
            await using IAsyncDisposable blockingLease = await RelationalNamedLock.AcquireTransactionAsync(
                blockingContext,
                lockKey,
                CancellationToken.None);
            NotificationFanoutOccurrence occurrence = await blockingContext.NotificationFanoutOccurrences
                .SingleAsync(item => item.TenantId == scenario.TenantId
                    && item.Id == scenario.OccurrenceId);
            occurrence.Supersede(replacementId, "newer_occurrence", claimedAt);
            await blockingContext.SaveChangesAsync();

            await using var claimContext = fixture.CreateDbContext();
            await claimContext.Database.OpenConnectionAsync();
            int claimBackendPid = await claimContext.Database
                .SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
                .SingleAsync();
            var claimRepository = new NotificationFanoutRunRepository(claimContext);
            Task<NotificationFanoutClaim?> claimTask = claimRepository.TryClaimOccurrenceAsync(
                scenario.TenantId,
                scenario.OccurrenceId,
                "supersession-race-worker",
                claimedAt,
                TimeSpan.FromMinutes(1),
                8,
                1,
                BacklogHighWatermark,
                BacklogLowWatermark,
                CancellationToken.None);

            bool waitedOnEventLock;
            try
            {
                waitedOnEventLock = await WaitForAdvisoryLockWaiterAsync(claimBackendPid);
            }
            finally
            {
                await blockingTransaction.CommitAsync();
            }

            NotificationFanoutClaim? claim = await claimTask.WaitAsync(TimeSpan.FromSeconds(5));
            NotificationFanoutRun? run = await claimRepository.GetByOccurrenceAsync(
                scenario.TenantId,
                scenario.OccurrenceId,
                trackChanges: false,
                CancellationToken.None);

            await Assert.That(waitedOnEventLock).IsTrue();
            await Assert.That(claim).IsNull();
            await Assert.That(run).IsNotNull();
            await Assert.That(run!.Status).IsEqualTo("pending");
            await Assert.That(run.ProcessingLeaseToken).IsNull();
        });
    }

    [Test]
    public async Task TryClaimOccurrenceAsync_TwoWorkers_ProducesOneActiveFence()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-claim-contention");
        DateTime claimedAt = Utc(2026, 8, 1, 12);

        await using var contextA = fixture.CreateDbContext();
        await using var contextB = fixture.CreateDbContext();
        var repositoryA = new NotificationFanoutRunRepository(contextA);
        var repositoryB = new NotificationFanoutRunRepository(contextB);

        NotificationFanoutClaim?[] claims = await Task.WhenAll(
            repositoryA.TryClaimOccurrenceAsync(
                scenario.TenantId,
                scenario.OccurrenceId,
                "worker-a",
                claimedAt,
                TimeSpan.FromMinutes(1),
                8,
                1,
                BacklogHighWatermark,
                BacklogLowWatermark,
                CancellationToken.None),
            repositoryB.TryClaimOccurrenceAsync(
                scenario.TenantId,
                scenario.OccurrenceId,
                "worker-b",
                claimedAt,
                TimeSpan.FromMinutes(1),
                8,
                1,
                BacklogHighWatermark,
                BacklogLowWatermark,
                CancellationToken.None));

        await Assert.That(claims.Count(claim => claim is not null)).IsEqualTo(1);
        NotificationFanoutClaim winner = claims.Single(claim => claim is not null)!;
        await Assert.That(winner.TenantId).IsEqualTo(scenario.TenantId);
        await Assert.That(winner.OccurrenceId).IsEqualTo(scenario.OccurrenceId);
        await Assert.That(winner.LeaseToken).IsNotEqualTo(Guid.Empty);
        await Assert.That(winner.Fence).IsGreaterThan(0);
    }

    [Test]
    public async Task LeaseRenewalAndExpiry_PreventEarlyTakeoverThenIssueNewFence()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-lease-expiry");
        DateTime claimedAt = Utc(2026, 8, 1, 12);

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaim first = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "worker-a",
            claimedAt,
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None) ?? throw new InvalidOperationException("Initial claim was not acquired.");

        bool renewed = await repository.TryRenewClaimAsync(
            first,
            claimedAt.AddSeconds(30),
            claimedAt.AddMinutes(2),
            CancellationToken.None);
        NotificationFanoutClaim? earlyTakeover = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "worker-b",
            claimedAt.AddSeconds(90),
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None);
        NotificationFanoutClaim? recovered = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "worker-b",
            claimedAt.AddMinutes(2).AddTicks(1),
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None);

        await Assert.That(renewed).IsTrue();
        await Assert.That(earlyTakeover).IsNull();
        await Assert.That(recovered).IsNotNull();
        await Assert.That(recovered!.LeaseToken).IsNotEqualTo(first.LeaseToken);
        await Assert.That(recovered.Fence).IsGreaterThan(first.Fence);
    }

    [Test]
    public async Task TryRenewClaimAsync_SameLeaseHorizon_IsIdempotentForExactFence()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-same-horizon-renewal");
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        DateTime leaseHorizon = claimedAt.AddMinutes(1);

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaim claim = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "same-horizon-worker",
            claimedAt,
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None) ?? throw new InvalidOperationException("Initial claim was not acquired.");

        bool renewed = await repository.TryRenewClaimAsync(
            claim,
            claimedAt.AddSeconds(10),
            leaseHorizon,
            CancellationToken.None);
        bool staleRenewed = await repository.TryRenewClaimAsync(
            claim with { LeaseToken = Guid.CreateVersion7() },
            claimedAt.AddSeconds(11),
            leaseHorizon,
            CancellationToken.None);
        NotificationFanoutRun? run = await repository.GetByOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            trackChanges: false,
            CancellationToken.None);

        await Assert.That(renewed).IsTrue();
        await Assert.That(staleRenewed).IsFalse();
        await Assert.That(run).IsNotNull();
        await Assert.That(run!.ProcessingLeaseExpiresAt).IsEqualTo(leaseHorizon);
        await Assert.That(run.HeartbeatAt).IsEqualTo(claimedAt.AddSeconds(10));
    }

    [Test]
    public async Task TryCheckpointAsync_RejectsStaleExpectedCursorAndPreservesCounts()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-checkpoint-stale");
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        var cursor1 = new NotificationFanoutAudienceCursor(claimedAt.AddHours(-2), Guid.CreateVersion7());
        var cursor2 = new NotificationFanoutAudienceCursor(claimedAt.AddHours(-1), Guid.CreateVersion7());

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaim claim = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "checkpoint-worker",
            claimedAt,
            TimeSpan.FromMinutes(5),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None) ?? throw new InvalidOperationException("Initial claim was not acquired.");

        bool firstCheckpoint = await repository.TryCheckpointAsync(
            claim,
            expectedCursor: null,
            cursor1,
            processedDelta: 2,
            createdDelta: 2,
            claimedAt.AddSeconds(1),
            CancellationToken.None);
        bool staleCheckpoint = await repository.TryCheckpointAsync(
            claim,
            expectedCursor: null,
            cursor2,
            processedDelta: 100,
            createdDelta: 100,
            claimedAt.AddSeconds(2),
            CancellationToken.None);
        bool nextCheckpoint = await repository.TryCheckpointAsync(
            claim,
            cursor1,
            cursor2,
            processedDelta: 1,
            createdDelta: 1,
            claimedAt.AddSeconds(3),
            CancellationToken.None);
        bool completed = await repository.TryCompleteAsync(
            claim,
            claimedAt.AddSeconds(4),
            CancellationToken.None);
        NotificationFanoutRun? run = await repository.GetByOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            trackChanges: false,
            CancellationToken.None);

        await Assert.That(firstCheckpoint).IsTrue();
        await Assert.That(staleCheckpoint).IsFalse();
        await Assert.That(nextCheckpoint).IsTrue();
        await Assert.That(completed).IsTrue();
        await Assert.That(run).IsNotNull();
        await Assert.That(run!.ProcessedCount).IsEqualTo(3);
        await Assert.That(run.CreatedNotificationCount).IsEqualTo(3);
    }

    [Test]
    public async Task OccurrenceLease_IsTenantBoundAndStaleWorkerCannotCompleteAfterTakeover()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-tenant-fence");
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        Guid wrongTenantId = Guid.CreateVersion7();

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaim? wrongTenantClaim = await repository.TryClaimOccurrenceAsync(
            wrongTenantId,
            scenario.OccurrenceId,
            "wrong-tenant",
            claimedAt,
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None);
        NotificationFanoutRun? wrongTenantRun = await repository.GetByOccurrenceAsync(
            wrongTenantId,
            scenario.OccurrenceId,
            trackChanges: false,
            CancellationToken.None);
        NotificationFanoutClaim first = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "worker-a",
            claimedAt,
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None) ?? throw new InvalidOperationException("Initial claim was not acquired.");
        NotificationFanoutClaim second = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "worker-b",
            claimedAt.AddMinutes(1).AddTicks(1),
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None) ?? throw new InvalidOperationException("Expired claim was not recovered.");

        bool staleCompletion = await repository.TryCompleteAsync(
            first,
            claimedAt.AddMinutes(1).AddSeconds(1),
            CancellationToken.None);
        bool winnerCompletion = await repository.TryCompleteAsync(
            second,
            claimedAt.AddMinutes(1).AddSeconds(2),
            CancellationToken.None);

        await Assert.That(wrongTenantClaim).IsNull();
        await Assert.That(wrongTenantRun).IsNull();
        await Assert.That(staleCompletion).IsFalse();
        await Assert.That(winnerCompletion).IsTrue();
    }

    [Test]
    public async Task CrashBeforeCheckpoint_ReplaysPreviousCursorWithoutSkippingReadPage()
    {
        await fixture.ResetAsync();
        FanoutScenario scenario = await SeedOccurrenceAsync("fanout-crash-replay");
        DateTime claimedAt = Utc(2026, 8, 1, 12);
        var committedCursor = new NotificationFanoutAudienceCursor(
            claimedAt.AddHours(-2),
            Guid.CreateVersion7());
        var uncommittedPageCursor = new NotificationFanoutAudienceCursor(
            claimedAt.AddHours(-1),
            Guid.CreateVersion7());

        await using var context = fixture.CreateDbContext();
        var repository = new NotificationFanoutRunRepository(context);
        NotificationFanoutClaim first = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "worker-a",
            claimedAt,
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None) ?? throw new InvalidOperationException("Initial claim was not acquired.");
        bool checkpointed = await repository.TryCheckpointAsync(
            first,
            expectedCursor: null,
            committedCursor,
            processedDelta: 2,
            createdDelta: 2,
            claimedAt.AddSeconds(1),
            CancellationToken.None);

        NotificationFanoutClaim replay = await repository.TryClaimOccurrenceAsync(
            scenario.TenantId,
            scenario.OccurrenceId,
            "worker-b",
            claimedAt.AddMinutes(1).AddTicks(1),
            TimeSpan.FromMinutes(1),
            8,
            1,
            BacklogHighWatermark,
            BacklogLowWatermark,
            CancellationToken.None) ?? throw new InvalidOperationException("Expired claim was not recovered.");
        bool replayCheckpointed = await repository.TryCheckpointAsync(
            replay,
            committedCursor,
            uncommittedPageCursor,
            processedDelta: 2,
            createdDelta: 2,
            claimedAt.AddMinutes(1).AddSeconds(1),
            CancellationToken.None);

        await Assert.That(checkpointed).IsTrue();
        await Assert.That(replay.Cursor).IsEqualTo(committedCursor);
        await Assert.That(replay.Fence).IsGreaterThan(first.Fence);
        await Assert.That(replay.Generation).IsGreaterThan(first.Generation);
        await Assert.That(replay.LeaseToken).IsNotEqualTo(first.LeaseToken);
        await Assert.That(replayCheckpointed).IsTrue();
    }

    private async Task<FanoutScenario> SeedOccurrenceAsync(
        string slugPrefix,
        bool ensureRun = true,
        int priority = 30,
        DateTime? occurredAt = null,
        DateTime? notBefore = null,
        NotificationDeliveryPolicyEnum deliveryPolicy = NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional)
    {
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Fanout {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var servicePrincipal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = $"fanout-source-{Guid.CreateVersion7():N}",
            DisplayName = "Fanout source",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Bot,
            ActorType = null!,
            ServicePrincipalId = servicePrincipal.Id,
            ServicePrincipal = servicePrincipal,
            Pii = new ActorPii { DisplayName = "Fanout source" },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Fanout lease event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        DateTime occurrenceTime = occurredAt ?? Utc(2026, 8, 1, 11);
        NotificationFanoutOccurrence occurrence = NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(),
            tenant.Id,
            @event.Id,
            sessionId: null,
            occurrenceTime,
            audienceCutoffAt: occurrenceTime,
            Guid.CreateVersion7(),
            "{\"fields\":[\"startTime\"]}",
            "{\"startTime\":\"2026-08-01T09:00:00Z\"}",
            "{\"startTime\":\"2026-08-01T10:00:00Z\"}",
            "event.updated",
            templateVersion: 1,
            (int)deliveryPolicy,
            policyVersion: 1,
            priority,
            notBefore: notBefore ?? occurrenceTime,
            sourceType: "event",
            sourceId: @event.Id,
            coalescingKey: $"event:{@event.Id:N}:schedule",
            coalescingWindowEndsAt: occurrenceTime);
        context.NotificationFanoutOccurrences.Add(occurrence);
        await context.SaveChangesAsync();
        if (ensureRun)
        {
            var repository = new NotificationFanoutRunRepository(context);
            await repository.EnsurePendingOccurrenceRunAsync(
                tenant.Id,
                occurrence.Id,
                Guid.CreateVersion7(),
                CancellationToken.None);
        }

        return new FanoutScenario(tenant.Id, @event.Id, occurrence.Id);
    }

    private async Task<Guid> SeedAdditionalOccurrenceAsync(
        FanoutScenario scenario,
        string suffix,
        int priority,
        DateTime occurredAt,
        DateTime? notBefore = null,
        bool ensureRun = true,
        NotificationDeliveryPolicyEnum deliveryPolicy = NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional)
    {
        await using var context = fixture.CreateDbContext();
        NotificationFanoutOccurrence occurrence = NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(),
            scenario.TenantId,
            scenario.EventId,
            sessionId: null,
            occurredAt,
            audienceCutoffAt: occurredAt,
            Guid.CreateVersion7(),
            "{\"fields\":[\"startTime\"]}",
            "{\"startTime\":\"2026-08-01T09:00:00Z\"}",
            "{\"startTime\":\"2026-08-01T10:00:00Z\"}",
            "event.updated",
            templateVersion: 1,
            (int)deliveryPolicy,
            policyVersion: 1,
            priority,
            notBefore: notBefore ?? occurredAt,
            sourceType: "event",
            sourceId: scenario.EventId,
            coalescingKey: $"event:{scenario.EventId:N}:schedule:{suffix}",
            coalescingWindowEndsAt: occurredAt);
        context.NotificationFanoutOccurrences.Add(occurrence);
        await context.SaveChangesAsync();
        if (ensureRun)
        {
            var repository = new NotificationFanoutRunRepository(context);
            await repository.EnsurePendingOccurrenceRunAsync(
                scenario.TenantId,
                occurrence.Id,
                Guid.CreateVersion7(),
                CancellationToken.None);
        }

        return occurrence.Id;
    }

    private async Task SupersedeAsync(
        FanoutScenario scenario,
        Guid replacementId,
        DateTime supersededAt)
    {
        await using var context = fixture.CreateDbContext();
        NotificationFanoutOccurrence occurrence = await context.NotificationFanoutOccurrences
            .SingleAsync(item => item.TenantId == scenario.TenantId
                && item.Id == scenario.OccurrenceId);
        occurrence.Supersede(replacementId, "newer_occurrence", supersededAt);
        await context.SaveChangesAsync();
    }

    private async Task SetRunStatusAsync(FanoutScenario scenario, string status)
    {
        await using var context = fixture.CreateDbContext();
        NotificationFanoutRun run = await context.NotificationFanoutRuns
            .SingleAsync(item => item.TenantId == scenario.TenantId
                && item.FanoutOccurrenceId == scenario.OccurrenceId);
        run.Status = status;
        run.ProcessingLeaseOwner = null;
        run.ProcessingLeaseToken = null;
        run.ProcessingLeaseExpiresAt = null;
        await context.SaveChangesAsync();
    }

    private async Task<bool> WaitForAdvisoryLockWaiterAsync(int backendPid)
    {
        await using var observerContext = fixture.CreateDbContext();
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int waiting = await observerContext.Database.SqlQuery<int>($$"""
                SELECT COUNT(*)::integer AS "Value"
                FROM pg_locks
                WHERE pid = {{backendPid}}
                  AND locktype = 'advisory'
                  AND NOT granted
                """).SingleAsync();
            if (waiting > 0)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        return false;
    }

    private static DateTime Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private sealed record FanoutScenario(Guid TenantId, Guid EventId, Guid OccurrenceId);
}
