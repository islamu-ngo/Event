// ABOUTME: Exercises ATProto transient storage through independent file-backed SQLite contexts.
// ABOUTME: Specifies insert-only creation, tenant binding, expiry, and exactly-one-winner consumption.

using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteAtprotoTransientStore")]
public sealed class AtprotoTransientStoreRepositorySqliteTests
{
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task CreateReadAndConsume_UsesTenantBoundSingleWinnerContract()
    {
        string path = Path.Combine(Path.GetTempPath(), $"atproto-transient-{Guid.CreateVersion7():N}.db");
        try
        {
            await using (ExploreDbContext setup = CreateContext(path))
            {
                await setup.Database.MigrateAsync();
                await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            }
            Guid tenantId = Guid.CreateVersion7();
            var time = new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(2_000_000_000_000));
            AtprotoTransientRecord record = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest, tenantId, "protected", 2_000_000_060_000);
            await using (ExploreDbContext writer = CreateContext(path))
                await Assert.That(await new AtprotoTransientStoreRepository(writer, time).TryCreateAsync(record)).IsTrue();

            ExploreDbContext[] contexts = Enumerable.Range(0, 8).Select(_ => CreateContext(path)).ToArray();
            try
            {
                foreach (ExploreDbContext context in contexts)
                {
                    await context.Database.OpenConnectionAsync();
                    await Assert.That(await context.AtprotoTransientRecords
                        .AnyAsync(row => row.Id == record.Id)).IsTrue();
                }

                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<AtprotoTransientRecord?>[] attempts = contexts.Select(async context => { await start.Task; return await new AtprotoTransientStoreRepository(context, time).ConsumeAsync(record.Id, record.Purpose, record.TokenDigest, tenantId); }).ToArray();
                start.SetResult();
                AtprotoTransientRecord?[] results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));
                await Assert.That(results.Count(result => result is not null)).IsEqualTo(1);
                await Assert.That(results.Single(result => result is not null)!.ProtectedPayload).IsEqualTo("protected");
            }
            finally { foreach (ExploreDbContext context in contexts) await context.DisposeAsync(); }
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); File.Delete(path + "-shm"); File.Delete(path + "-wal"); }
    }

    private static ExploreDbContext CreateContext(string path)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = path,
            TlsMode = PrimaryDatabaseTlsMode.Prefer,
        });
        return new ExploreDbContext(builder.Options);
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
