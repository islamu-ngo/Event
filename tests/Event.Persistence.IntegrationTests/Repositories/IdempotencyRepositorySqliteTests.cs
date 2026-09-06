// ABOUTME: File-backed SQLite contention contracts for EF-native idempotency key claims.
// ABOUTME: Proves one owner, durable winner lookup, and atomic replacement of expired records.

using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteIdempotencyClaims")]
public sealed class IdempotencyRepositorySqliteTests
{
    private static readonly DateTime Now =
        DateTime.SpecifyKind(new DateTime(2026, 8, 27, 12, 0, 0), DateTimeKind.Utc);

    [Test]
    public async Task ConcurrentSameKeyClaims_ProduceOneOwnerAndOneDurableRecord()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-race-{Guid.CreateVersion7():N}.db");
        try
        {
            await using (ExploreDbContext setup = CreateContext(databasePath))
            {
                await setup.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(setup, CancellationToken.None);
            }

            Guid tenantId = Guid.CreateVersion7();
            await using ExploreDbContext winnerContext = CreateContext(databasePath);
            var contenderGate = new IdempotencyInsertGate();
            await using ExploreDbContext contenderContext = CreateContext(databasePath, contenderGate);
            var winnerRepository = new IdempotencyRepository(winnerContext);
            var contenderRepository = new IdempotencyRepository(contenderContext);
            await using var winnerTransaction = await winnerContext.Database.BeginTransactionAsync();
            IdempotencyRecord winnerRecord = NewClaim("contended-key", tenantId);
            IdempotencyRecord contenderRecord = NewClaim("contended-key", tenantId);

            IdempotencyClaim winner = await winnerRepository.TryClaimAsync(winnerRecord);
            Task<IdempotencyClaim> contenderTask = contenderRepository.TryClaimAsync(contenderRecord);
            await contenderGate.WaitUntilCommandReachedAsync();
            await Assert.That(contenderTask.IsCompleted).IsFalse();

            contenderGate.Release();
            await winnerTransaction.CommitAsync();
            IdempotencyClaim contender = await contenderTask;

            await Assert.That(winner.IsOwner).IsTrue();
            await Assert.That(contender.IsOwner).IsFalse();
            await Assert.That(contender.Record.Id).IsEqualTo(winnerRecord.Id);
            await using ExploreDbContext verification = CreateContext(databasePath);
            await Assert.That(await verification.IdempotencyRecords.CountAsync(record =>
                record.Key == winnerRecord.Key &&
                record.TenantId == tenantId)).IsEqualTo(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task ExpiredClaim_IsReplacedWhileActiveClaimRemainsOwned()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"idempotency-expiry-{Guid.CreateVersion7():N}.db");
        try
        {
            Guid tenantId = Guid.CreateVersion7();
            await using ExploreDbContext context = CreateContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
            var repository = new IdempotencyRepository(context);
            IdempotencyRecord expired = NewClaim("replace-key", tenantId);
            expired.CreatedAt = Now.AddDays(-2);
            expired.ExpiresAt = Now.AddMinutes(-1);
            await repository.SaveAsync(expired);
            IdempotencyRecord replacement = NewClaim("replace-key", tenantId);

            IdempotencyClaim replaced = await repository.TryClaimAsync(replacement);
            IdempotencyClaim replay = await repository.TryClaimAsync(NewClaim("replace-key", tenantId));

            await Assert.That(replaced.IsOwner).IsTrue();
            await Assert.That(replaced.Record.Id).IsEqualTo(replacement.Id);
            await Assert.That(replay.IsOwner).IsFalse();
            await Assert.That(replay.Record.Id).IsEqualTo(replacement.Id);
            await Assert.That(await context.IdempotencyRecords.CountAsync(record =>
                record.Key == replacement.Key &&
                record.TenantId == tenantId)).IsEqualTo(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private static ExploreDbContext CreateContext(
        string databasePath,
        params IInterceptor[] interceptors)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        var context = new ExploreDbContext(builder.Options);
        context.EnableTenantFilterBypass("Idempotency SQLite contention contract.");
        return context;
    }

    private static IdempotencyRecord NewClaim(string key, Guid tenantId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            TenantId = tenantId,
            UserId = "test-user",
            RequestMethod = "POST",
            RequestTarget = "/api/test",
            RequestBodyHash = Guid.NewGuid().ToString("N"),
            PrincipalFingerprint = Guid.NewGuid().ToString("N"),
            StatusCode = IdempotencyRecord.InProgressStatusCode,
            CreatedAt = Now,
            ExpiresAt = Now.AddHours(24)
        };

    private sealed class IdempotencyInsertGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _commandReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed = 1;

        public Task WaitUntilCommandReachedAsync() =>
            _commandReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await WaitAtInsertAsync(command, cancellationToken);

            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await WaitAtInsertAsync(command, cancellationToken);
            return result;
        }

        private async Task WaitAtInsertAsync(
            DbCommand command,
            CancellationToken cancellationToken)
        {
            if (command.CommandText.Contains("idempotency_records", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _armed, 0) == 1)
            {
                _commandReached.TrySetResult();
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
    }
}
