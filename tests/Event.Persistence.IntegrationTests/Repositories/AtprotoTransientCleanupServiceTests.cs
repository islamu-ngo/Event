// ABOUTME: Exercises bounded ATProto cleanup against migrated PostgreSQL and native repositories.
// ABOUTME: Protects live authentication state, replay acceptance retention, and concurrent sweep safety.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoTransientCleanupServiceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetryEnabledProvider_LostDeleteAcknowledgement_StopsSweepAfterOneBatchAttempt(bool replayFault)
    {
        await fixture.ResetAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await SeedExpiredAsync(2601, clock.GetUtcNow());
        var fault = new LostDeleteAcknowledgement(replayFault
            ? "atproto_transient_assertion_replays" : "atproto_transient_records");
        await using ExploreDbContext context = fixture.CreateDbContext(fault);

        await Assert.That(async () => await CreateService(context, clock).CleanupExpiredAsync()).Throws<NpgsqlException>();

        await Assert.That(fault.DeleteExecutions).IsEqualTo(1);
        await using ExploreDbContext verifier = fixture.CreateDbContext();
        await Assert.That(await verifier.AtprotoTransientRecords.CountAsync()).IsEqualTo(replayFault ? 101 : 2101);
        await Assert.That(await verifier.AtprotoTransientAssertionReplays.CountAsync()).IsEqualTo(replayFault ? 2101 : 2601);
    }

    [Test]
    public async Task Sweep_BoundsBothStoresToFiveBatches_AndNextSweepDrainsRemainder()
    {
        await fixture.ResetAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await SeedExpiredAsync(2601, clock.GetUtcNow());
        var observer = new DeleteBudgetObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(observer);
        AtprotoTransientCleanupService service = CreateService(context, clock);

        var first = await service.CleanupExpiredAsync();

        await Assert.That(first).IsEqualTo((2500, 2500));
        await Assert.That(observer.Executions).IsEqualTo(10);
        await Assert.That(observer.LargestBatch).IsEqualTo(500);
        // Expired rows have no public read operation; count persisted remnants, not filtered live reads.
        await Assert.That(await context.AtprotoTransientRecords.CountAsync()).IsEqualTo(101);
        await Assert.That(await context.AtprotoTransientAssertionReplays.CountAsync()).IsEqualTo(101);

        var second = await service.CleanupExpiredAsync();

        await Assert.That(second).IsEqualTo((101, 101));
        await Assert.That(observer.Executions).IsEqualTo(12);
        await Assert.That(await context.AtprotoTransientRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AtprotoTransientAssertionReplays.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Sweep_DeletesCredentialsAtExpiry_ButRetainsReplayThroughMaximumClockDrift()
    {
        await fixture.ResetAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        Guid tenant = Guid.CreateVersion7();
        string assertionId = Guid.CreateVersion7().ToString();
        // The validator supplies final acceptance expiry, including its permitted clock skew.
        long acceptanceExpiry = now + 10_000;
        var state = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest(), tenant, "opaque-state", now + 60_000);
        var handoff = AtprotoTransientRecord.Create(AtprotoTransientPurpose.TenantHandoff, Digest(), tenant, "opaque-handoff", now + 60_000);
        var probe = AtprotoTransientRecord.CreateHealthProbe(Digest(), "opaque-probe", now + 60_000);
        await using ExploreDbContext context = fixture.CreateDbContext();
        var transients = new AtprotoTransientStoreRepository(context, clock);
        var replays = new AtprotoTransientAssertionReplayRepository(context, clock);
        await Assert.That(await transients.TryCreateAsync(state)).IsTrue();
        await Assert.That(await transients.TryCreateAsync(handoff)).IsTrue();
        await Assert.That(await transients.TryCreateHealthProbeAsync(probe)).IsTrue();
        await Assert.That(await replays.TryClaimAsync(AtprotoTransientAssertionReplay.CreateFromAssertionId(assertionId, acceptanceExpiry))).IsTrue();
        context.AtprotoTransientRecords.Add(AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest(), tenant, "expired", now));
        context.AtprotoTransientAssertionReplays.Add(AtprotoTransientAssertionReplay.CreateFromAssertionId(Guid.CreateVersion7().ToString(), now));
        await context.SaveChangesAsync();
        AtprotoTransientCleanupService service = CreateService(context, clock);

        await Assert.That(await service.CleanupExpiredAsync()).IsEqualTo((1, 0));
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(acceptanceExpiry - 1);
        await Assert.That(await service.CleanupExpiredAsync()).IsEqualTo((0, 0));
        await Assert.That(await replays.TryClaimAsync(AtprotoTransientAssertionReplay.CreateFromAssertionId(assertionId, acceptanceExpiry))).IsFalse();
        await Assert.That((await transients.ReadAsync(state.Purpose, state.TokenDigest, tenant))?.Id).IsEqualTo(state.Id);
        await Assert.That((await transients.ReadAsync(handoff.Purpose, handoff.TokenDigest, tenant))?.Id).IsEqualTo(handoff.Id);
        await Assert.That(await transients.ConsumeHealthProbeAsync(probe.Id, probe.TokenDigest)).IsTrue();

        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(acceptanceExpiry);
        await Assert.That(await service.CleanupExpiredAsync()).IsEqualTo((0, 1));
        await Assert.That(await context.AtprotoTransientAssertionReplays.CountAsync()).IsEqualTo(1);
        await Assert.That(await replays.TryClaimAsync(AtprotoTransientAssertionReplay.CreateFromAssertionId(assertionId, acceptanceExpiry))).IsFalse();

        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(acceptanceExpiry + 9_999);
        await Assert.That(await service.CleanupExpiredAsync()).IsEqualTo((0, 0));
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(acceptanceExpiry + 10_000);
        await Assert.That(await service.CleanupExpiredAsync()).IsEqualTo((0, 1));
        await Assert.That(await context.AtprotoTransientAssertionReplays.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AtprotoTransientRecords.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task IndependentConcurrentSweeps_DeleteEachExpiredRowOnce_AndLeaveLiveStateUsable()
    {
        await fixture.ResetAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await SeedExpiredAsync(1601, clock.GetUtcNow());
        var live = AtprotoTransientRecord.Create(AtprotoTransientPurpose.TenantHandoff, Digest(), Guid.CreateVersion7(), "live", clock.GetUtcNow().AddMinutes(1).ToUnixTimeMilliseconds());
        await using (ExploreDbContext writer = fixture.CreateDbContext())
            await new AtprotoTransientStoreRepository(writer, clock).TryCreateAsync(live);
        await using ExploreDbContext firstContext = fixture.CreateDbContext();
        await using ExploreDbContext secondContext = fixture.CreateDbContext();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(int TransientRows, int ReplayRows)> Sweep(ExploreDbContext context)
        {
            await start.Task;
            return await CreateService(context, clock).CleanupExpiredAsync(deadline.Token);
        }
        var first = Sweep(firstContext);
        var second = Sweep(secondContext);
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        await using ExploreDbContext verifier = fixture.CreateDbContext();
        var final = await CreateService(verifier, clock).CleanupExpiredAsync(deadline.Token);

        await Assert.That(results.Sum(result => result.TransientRows) + final.TransientRows).IsEqualTo(1601);
        await Assert.That(results.Sum(result => result.ReplayRows) + final.ReplayRows).IsEqualTo(1601);
        await Assert.That(await verifier.AtprotoTransientRecords.CountAsync()).IsEqualTo(1);
        await Assert.That(await verifier.AtprotoTransientAssertionReplays.CountAsync()).IsEqualTo(0);
        await Assert.That((await new AtprotoTransientStoreRepository(verifier, clock)
            .ConsumeAsync(live.Id, live.Purpose, live.TokenDigest, live.TenantId!.Value))?.ProtectedPayload).IsEqualTo("live");
    }

    private AtprotoTransientCleanupService CreateService(ExploreDbContext context, TimeProvider clock) =>
        new(new AtprotoTransientStoreRepository(context, clock), new AtprotoTransientAssertionReplayRepository(context, clock), clock);

    private async Task SeedExpiredAsync(int count, DateTimeOffset now)
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        Guid tenant = Guid.CreateVersion7();
        long expiry = now.AddMilliseconds(-10_001).ToUnixTimeMilliseconds();
        context.AtprotoTransientRecords.AddRange(Enumerable.Range(0, count).Select(_ =>
            AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest(), tenant, "expired-opaque", expiry)));
        context.AtprotoTransientAssertionReplays.AddRange(Enumerable.Range(0, count).Select(_ =>
            AtprotoTransientAssertionReplay.CreateFromAssertionId(Guid.CreateVersion7().ToString(), expiry)));
        await context.SaveChangesAsync();
    }

    private static string Digest() => Guid.CreateVersion7().ToString("N") + Guid.CreateVersion7().ToString("N");

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DeleteBudgetObserver : DbCommandInterceptor
    {
        public int Executions { get; private set; }
        public int LargestBatch { get; private set; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                Executions++;
                LargestBatch = Math.Max(LargestBatch, result);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LostDeleteAcknowledgement(string tableName) : DbCommandInterceptor
    {
        public int DeleteExecutions { get; private set; }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(tableName, StringComparison.OrdinalIgnoreCase))
            {
                DeleteExecutions++;
                if (DeleteExecutions == 1)
                    throw new NpgsqlException("Injected lost cleanup acknowledgement after committed deletion.", new TimeoutException());
            }
            return ValueTask.FromResult(result);
        }
    }
}
