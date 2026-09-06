// ABOUTME: File-backed SQLite regressions for ATProto bootstrap JTI replay consumption.
// ABOUTME: Proves concurrent same-tenant uses have one winner while tenant scopes remain isolated.

using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteAtprotoBootstrapReplay")]
public sealed class AtprotoBootstrapReplayRepositorySqliteTests
{
    [Test]
    public async Task TryConsumeAsync_WhenFileBackedSqliteContends_AllowsOneSameTenantWinnerAndIsolatesTenants()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"atproto-bootstrap-replay-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            Guid tenantId = Guid.CreateVersion7();
            const string jti = "sqlite-bootstrap-contention";
            ExploreDbContext[] contexts = Enumerable.Range(0, 4).Select(_ => CreateContext(databasePath)).ToArray();

            DateTimeOffset expiresAt;
            try
            {
                foreach (ExploreDbContext context in contexts)
                {
                    await context.Database.OpenConnectionAsync();
                    await Assert.That(await context.IdempotencyRecords.CountAsync())
                        .IsEqualTo(0);
                }

                expiresAt = DateTimeOffset.UtcNow.AddMinutes(1);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<bool>[] attempts = contexts.Select(async context =>
                {
                    await start.Task;
                    return await new AtprotoBootstrapReplayRepository(context)
                        .TryConsumeAsync(jti, tenantId, expiresAt);
                }).ToArray();

                start.SetResult();
                bool[] results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));

                await Assert.That(results.Count(result => result)).IsEqualTo(1);
            }
            finally
            {
                foreach (ExploreDbContext context in contexts)
                {
                    await context.DisposeAsync();
                }
            }

            await using (ExploreDbContext tenantContext = CreateContext(databasePath))
            {
                bool isolatedTenantResult = await new AtprotoBootstrapReplayRepository(tenantContext)
                    .TryConsumeAsync(jti, Guid.CreateVersion7(), expiresAt);

                await Assert.That(isolatedTenantResult).IsTrue();
                await Assert.That(await tenantContext.IdempotencyRecords
                    .CountAsync(record => record.Key == "atproto-bootstrap:" + jti)).IsEqualTo(2);
            }
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
    public async Task TryConsumeAsync_WhenExpired_DoesNotPersistAReplayRecord()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"atproto-bootstrap-replay-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using ExploreDbContext context = CreateContext(databasePath);

            bool consumed = await new AtprotoBootstrapReplayRepository(context).TryConsumeAsync(
                "expired-bootstrap",
                Guid.CreateVersion7(),
                DateTimeOffset.UtcNow.AddSeconds(-1));

            await Assert.That(consumed).IsFalse();
            await Assert.That(await context.IdempotencyRecords.CountAsync()).IsEqualTo(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private static async Task CreateDatabaseAsync(string databasePath)
    {
        await using ExploreDbContext context = CreateContext(databasePath);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    private static ExploreDbContext CreateContext(string databasePath)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            Pooling = false,
        }.ToString();
        return new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
    }
}
