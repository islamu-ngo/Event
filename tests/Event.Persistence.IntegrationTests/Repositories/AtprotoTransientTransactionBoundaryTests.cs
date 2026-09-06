// ABOUTME: Exercises ambient-transaction rejection and cleanup contention on real PostgreSQL.
// ABOUTME: Verifies live authentication payload ownership remains separate from expired-row cleanup.

using System.Transactions;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AtprotoTransientTransactionBoundary")]
public sealed class AtprotoTransientTransactionBoundaryTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    [Before(Test)]
    public Task ResetAsync() => fixture.ResetAsync();

    [Test]
    public async Task AmbientTransaction_CannotReceivePayloadBeforeDurableCommit()
    {
        var record = Record('a', Now.AddMinutes(1));
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            setup.Add(record);
            await setup.SaveChangesAsync();
        }

        // PostgreSQL supports ambient transactions. Without the repository guard,
        // the caller would receive ciphertext while this scope can still roll back.
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var consuming = new ExploreDbContext(options);
            await Assert.That(consuming.Database.CreateExecutionStrategy().RetriesOnFailure).IsFalse();
            await Assert.That(await consuming.AtprotoTransientRecords.AnyAsync(row => row.Id == record.Id)).IsTrue();
            var repository = new AtprotoTransientStoreRepository(consuming, new FixedTimeProvider());

            await Assert.That(async () => await repository.ConsumeAsync(
                    record.Id, record.Purpose, record.TokenDigest, record.TenantId!.Value))
                .Throws<InvalidOperationException>();
        }

        await using ExploreDbContext verification = fixture.CreateDbContext();
        await Assert.That(await verification.AtprotoTransientRecords.AnyAsync(row => row.Id == record.Id)).IsTrue();
    }

    [Test]
    public async Task ExplicitEnlistment_CannotReceivePayloadBeforeDurableCommit()
    {
        var record = Record('a', Now.AddMinutes(1));
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            setup.Add(record);
            await setup.SaveChangesAsync();
        }

        using (var transaction = new CommittableTransaction())
        {
            await using var consuming = CreateNonRetryingContext();
            await consuming.Database.OpenConnectionAsync();
            consuming.Database.EnlistTransaction(transaction);
            await Assert.That(Transaction.Current).IsNull();
            await Assert.That(consuming.Database.CurrentTransaction).IsNull();
            await Assert.That(await consuming.AtprotoTransientRecords.AnyAsync(row => row.Id == record.Id)).IsTrue();
            var repository = new AtprotoTransientStoreRepository(consuming, new FixedTimeProvider());

            await Assert.That(async () => await repository.ConsumeAsync(
                    record.Id, record.Purpose, record.TokenDigest, record.TenantId!.Value))
                .Throws<InvalidOperationException>();
            transaction.Rollback();
        }

        await using ExploreDbContext verification = fixture.CreateDbContext();
        var verifier = new AtprotoTransientStoreRepository(verification, new FixedTimeProvider());
        await Assert.That((await verifier.ConsumeAsync(
            record.Id, record.Purpose, record.TokenDigest, record.TenantId!.Value))!.Id).IsEqualTo(record.Id);
    }

    [Test]
    public async Task OuterEfTransaction_CannotReportADurableReplayClaim()
    {
        string assertionId = Guid.CreateVersion7().ToString();
        await using (ExploreDbContext claiming = CreateNonRetryingContext())
        {
            await using var transaction = await claiming.Database.BeginTransactionAsync();
            var repository = new AtprotoTransientAssertionReplayRepository(claiming, new FixedTimeProvider());

            await Assert.That(async () => await repository.TryClaimAsync(Replay(assertionId)))
                .Throws<InvalidOperationException>();
            await transaction.RollbackAsync();
        }

        await AssertDurableReplayClaimAsync(assertionId);
    }

    [Test]
    public async Task AmbientTransaction_CannotReportADurableReplayClaim()
    {
        string assertionId = Guid.CreateVersion7().ToString();
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using ExploreDbContext claiming = CreateNonRetryingContext();
            await claiming.Database.OpenConnectionAsync();
            await Assert.That(await claiming.AtprotoTransientAssertionReplays.CountAsync()).IsEqualTo(0);
            var repository = new AtprotoTransientAssertionReplayRepository(claiming, new FixedTimeProvider());

            await Assert.That(async () => await repository.TryClaimAsync(Replay(assertionId)))
                .Throws<InvalidOperationException>();
        }

        await AssertDurableReplayClaimAsync(assertionId);
    }

    [Test]
    public async Task ExplicitEnlistment_CannotReportADurableReplayClaim()
    {
        string assertionId = Guid.CreateVersion7().ToString();
        using (var transaction = new CommittableTransaction())
        {
            await using ExploreDbContext claiming = CreateNonRetryingContext();
            await claiming.Database.OpenConnectionAsync();
            claiming.Database.EnlistTransaction(transaction);
            await Assert.That(Transaction.Current).IsNull();
            await Assert.That(claiming.Database.CurrentTransaction).IsNull();
            await Assert.That(await claiming.AtprotoTransientAssertionReplays.CountAsync()).IsEqualTo(0);
            var repository = new AtprotoTransientAssertionReplayRepository(claiming, new FixedTimeProvider());

            await Assert.That(async () => await repository.TryClaimAsync(Replay(assertionId)))
                .Throws<InvalidOperationException>();
            transaction.Rollback();
        }

        await AssertDurableReplayClaimAsync(assertionId);
    }

    private ExploreDbContext CreateNonRetryingContext() =>
        new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    private static AtprotoTransientAssertionReplay Replay(string assertionId) =>
        AtprotoTransientAssertionReplay.CreateFromAssertionId(
            assertionId, Now.AddMinutes(1).ToUnixTimeMilliseconds());

    private async Task AssertDurableReplayClaimAsync(string assertionId)
    {
        await using (ExploreDbContext writer = fixture.CreateDbContext())
        {
            var repository = new AtprotoTransientAssertionReplayRepository(writer, new FixedTimeProvider());
            await Assert.That(await repository.TryClaimAsync(Replay(assertionId))).IsTrue();
        }

        await using ExploreDbContext reader = fixture.CreateDbContext();
        var verifier = new AtprotoTransientAssertionReplayRepository(reader, new FixedTimeProvider());
        await Assert.That(await verifier.TryClaimAsync(Replay(assertionId))).IsFalse();
    }

    [Test]
    public async Task CleanupRacingLiveConsumers_DeletesOnlyItsBoundedExpiredBatch()
    {
        var live = Record('a', Now.AddMinutes(1));
        var expiredFirst = Record('b', Now.AddMinutes(-1));
        var expiredSecond = Record('c', Now.AddMinutes(-1));
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            setup.AddRange(live, expiredFirst, expiredSecond);
            await setup.SaveChangesAsync();
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> cleanup = CleanupAsync();
        Task<AtprotoTransientRecord?> firstConsume = ConsumeAsync();
        Task<AtprotoTransientRecord?> secondConsume = ConsumeAsync();
        start.SetResult();
        await Task.WhenAll(cleanup, firstConsume, secondConsume).WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(await cleanup).IsEqualTo(1);
        AtprotoTransientRecord?[] results = [await firstConsume, await secondConsume];
        await Assert.That(results.Count(result => result is not null)).IsEqualTo(1);
        await Assert.That(results.Single(result => result is not null)!.Id).IsEqualTo(live.Id);

        await using ExploreDbContext verification = fixture.CreateDbContext();
        var survivingRows = await verification.AtprotoTransientRecords.AsNoTracking().ToListAsync();
        await Assert.That(survivingRows.Count).IsEqualTo(1);
        await Assert.That(survivingRows[0].ExpiresAtUnixMilliseconds <= Now.ToUnixTimeMilliseconds()).IsTrue();

        async Task<int> CleanupAsync()
        {
            await using ExploreDbContext context = fixture.CreateDbContext();
            await start.Task;
            return await new AtprotoTransientStoreRepository(context, new FixedTimeProvider())
                .DeleteExpiredAsync(Now.ToUnixTimeMilliseconds(), batchSize: 1);
        }

        async Task<AtprotoTransientRecord?> ConsumeAsync()
        {
            await using ExploreDbContext context = fixture.CreateDbContext();
            await start.Task;
            return await new AtprotoTransientStoreRepository(context, new FixedTimeProvider())
                .ConsumeAsync(live.Id, live.Purpose, live.TokenDigest, live.TenantId!.Value);
        }
    }

    private static AtprotoTransientRecord Record(char digestCharacter, DateTimeOffset expiry) =>
        AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.OAuthState,
            new string(digestCharacter, AtprotoTransientRecord.Sha256DigestLength),
            Guid.CreateVersion7(),
            "opaque-protected-test-payload",
            expiry.ToUnixTimeMilliseconds());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
