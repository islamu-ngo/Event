// ABOUTME: Exercises committed-delete ambiguity against real SQLite with an enabled retry strategy.
// ABOUTME: Ensures uncertain consumption withholds ciphertext and never repeats a destructive command.

using System.Data.Common;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class AtprotoTransientConsumeFailureTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    [Test]
    public async Task CommittedDeleteResponseLoss_DoesNotRetryOrReleaseCiphertext()
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
        await connection.OpenAsync();
        var ordinaryOptions = Options(connection).Options;
        var record = AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.OAuthState,
            Digest,
            Guid.CreateVersion7(),
            "opaque-protected-test-payload",
            Now.AddMinutes(1).ToUnixTimeMilliseconds());

        await using (var setup = new ExploreDbContext(ordinaryOptions))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(record);
            await setup.SaveChangesAsync();
        }

        var responseLoss = new CommittedDeleteResponseLossInterceptor();
        var retryingOptions = Options(connection)
            .ReplaceService<IExecutionStrategyFactory, RetryOnceExecutionStrategyFactory>()
            .AddInterceptors(responseLoss)
            .Options;

        await using (var consuming = new ExploreDbContext(retryingOptions))
        {
            await Assert.That(consuming.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
            var repository = new AtprotoTransientStoreRepository(consuming, new FixedTimeProvider());

            await Assert.That(async () => await repository.ConsumeAsync(
                    record.Id, record.Purpose, record.TokenDigest, record.TenantId!.Value))
                .Throws<TimeoutException>();
        }

        // A committed database mutation and a lost response are both real here:
        // only the response delivery is fault-injected after SQLite completes DELETE.
        await Assert.That(responseLoss.CompletedDeletes).IsEqualTo(1);
        await using var verification = new ExploreDbContext(ordinaryOptions);
        await Assert.That(await verification.AtprotoTransientRecords.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ExpiryBetweenCandidateReadAndDelete_DoesNotConsumeTheRow()
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(Options(connection).Options);
        await context.Database.EnsureCreatedAsync();
        long expiresAt = Now.AddSeconds(1).ToUnixTimeMilliseconds();
        var record = AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.OAuthState,
            Digest,
            Guid.CreateVersion7(),
            "opaque-protected-test-payload",
            expiresAt);
        context.Add(record);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var clock = new ExpiringAtDeleteTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(expiresAt));
        var repository = new AtprotoTransientStoreRepository(context, clock);
        var result = await repository.ConsumeAsync(
            record.Id, record.Purpose, record.TokenDigest, record.TenantId!.Value);

        await Assert.That(result).IsNull();
        await Assert.That(await context.AtprotoTransientRecords.AnyAsync(row => row.Id == record.Id)).IsTrue();
    }

    private static DbContextOptionsBuilder<ExploreDbContext> Options(SqliteConnection connection) =>
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention();

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExpiringAtDeleteTimeProvider(DateTimeOffset expiry) : TimeProvider
    {
        private int _observations;

        public override DateTimeOffset GetUtcNow() =>
            Interlocked.Increment(ref _observations) == 1 ? Now : expiry;
    }

    private sealed class CommittedDeleteResponseLossInterceptor : DbCommandInterceptor
    {
        public int CompletedDeletes { get; private set; }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            CompletedDeletes++;
            if (CompletedDeletes == 1)
            {
                throw new TimeoutException("The committed operation's response was lost.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class RetryOnceExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryOnceExecutionStrategy(dependencies.CurrentContext.Context);
    }

    private sealed class RetryOnceExecutionStrategy(DbContext context)
        : ExecutionStrategy(context, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
    }
}
