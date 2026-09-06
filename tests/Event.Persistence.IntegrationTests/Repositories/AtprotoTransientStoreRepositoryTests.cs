// ABOUTME: Exercises ATProto transient storage against migrated PostgreSQL with independent contexts.
// ABOUTME: Proves single-winner deletion, ABA safety, and no retry after an ambiguous destructive commit.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoTransientStoreRepositoryTests(PostgreSqlContainerFixture fixture)
{
    private const string Digest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Test]
    public async Task ConcurrentIndependentContexts_ReturnExactlyOnePayloadWinner()
    {
        await fixture.ResetAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.CreateVersion7();
        AtprotoTransientRecord record = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest, tenant, "winner", now.AddMinutes(1).ToUnixTimeMilliseconds());
        await using (ExploreDbContext writer = fixture.CreateDbContext())
            await Assert.That(await new AtprotoTransientStoreRepository(writer, new FixedTimeProvider(now)).TryCreateAsync(record)).IsTrue();
        ExploreDbContext[] contexts = Enumerable.Range(0, 16).Select(_ => fixture.CreateDbContext()).ToArray();
        try
        {
            foreach (ExploreDbContext context in contexts)
            {
                await context.Database.OpenConnectionAsync();
                await Assert.That(await context.AtprotoTransientRecords.AnyAsync(row => row.Id == record.Id)).IsTrue();
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<AtprotoTransientRecord?>[] attempts = contexts.Select(async context =>
            {
                await start.Task;
                return await new AtprotoTransientStoreRepository(context, new FixedTimeProvider(now))
                    .ConsumeAsync(record.Id, record.Purpose, record.TokenDigest, tenant, deadline.Token);
            }).ToArray();
            start.SetResult();
            AtprotoTransientRecord?[] results = await Task.WhenAll(attempts);
            await Assert.That(results.Count(result => result is not null)).IsEqualTo(1);
            await Assert.That(results.Single(result => result is not null)!.ProtectedPayload).IsEqualTo("winner");
        }
        finally
        {
            foreach (ExploreDbContext context in contexts) await context.DisposeAsync();
        }
    }

    [Test]
    public async Task CandidateIdPredicate_PreventsDeleteRecreateAba()
    {
        await fixture.ResetAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.CreateVersion7();
        AtprotoTransientRecord original = AtprotoTransientRecord.Create(AtprotoTransientPurpose.TenantHandoff, Digest, tenant, "old", now.AddMinutes(1).ToUnixTimeMilliseconds());
        await using (ExploreDbContext writer = fixture.CreateDbContext())
            await new AtprotoTransientStoreRepository(writer, new FixedTimeProvider(now)).TryCreateAsync(original);
        var interceptor = new ReplaceBeforeDeleteInterceptor(fixture, original, now);
        await using ExploreDbContext consumer = fixture.CreateDbContext(interceptor);
        AtprotoTransientRecord? result = await new AtprotoTransientStoreRepository(consumer, new FixedTimeProvider(now)).ConsumeAsync(original.Id, original.Purpose, original.TokenDigest, tenant);
        await Assert.That(result).IsNull();
        await using ExploreDbContext verifier = fixture.CreateDbContext();
        AtprotoTransientRecord replacement = await verifier.AtprotoTransientRecords.AsNoTracking().SingleAsync();
        await Assert.That(replacement.Id).IsNotEqualTo(original.Id);
        await Assert.That(replacement.ProtectedPayload).IsEqualTo("replacement");
    }

    [Test]
    public async Task RetryEnabledProvider_AmbiguousPostDeleteFaultExecutesDeleteOnceAndReturnsNoPayload()
    {
        await fixture.ResetAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.CreateVersion7();
        AtprotoTransientRecord record = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest, tenant, "must-not-escape", now.AddMinutes(1).ToUnixTimeMilliseconds());
        await using (ExploreDbContext writer = fixture.CreateDbContext())
            await new AtprotoTransientStoreRepository(writer, new FixedTimeProvider(now)).TryCreateAsync(record);
        var interceptor = new AmbiguousDeleteFaultInterceptor();
        await using ExploreDbContext context = fixture.CreateDbContext(interceptor);
        var repository = new AtprotoTransientStoreRepository(context, new FixedTimeProvider(now));
        await Assert.That(async () => await repository.ConsumeAsync(record.Id, record.Purpose, record.TokenDigest, tenant)).Throws<NpgsqlException>();
        await Assert.That(interceptor.DeleteExecutions).IsEqualTo(1);
        await using ExploreDbContext verifier = fixture.CreateDbContext();
        await Assert.That(await verifier.AtprotoTransientRecords.CountAsync()).IsEqualTo(0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class AmbiguousDeleteFaultInterceptor : DbCommandInterceptor
    {
        private int executions;
        public int DeleteExecutions => executions;
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("atproto_transient_records", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                int count = Interlocked.Increment(ref executions);
                if (count == 1) throw new NpgsqlException("Injected ambiguous post-delete transport failure.", new TimeoutException());
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ReplaceBeforeDeleteInterceptor(PostgreSqlContainerFixture fixture, AtprotoTransientRecord original, DateTimeOffset now) : DbCommandInterceptor
    {
        private int replaced;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("atproto_transient_records", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref replaced, 1) == 0)
            {
                await using ExploreDbContext replacementContext = fixture.CreateDbContext();
                await replacementContext.AtprotoTransientRecords.Where(record => record.Id == original.Id).ExecuteDeleteAsync(cancellationToken);
                AtprotoTransientRecord replacement = AtprotoTransientRecord.Create(original.Purpose, original.TokenDigest, original.TenantId!.Value, "replacement", now.AddMinutes(1).ToUnixTimeMilliseconds());
                await new AtprotoTransientStoreRepository(replacementContext, new FixedTimeProvider(now)).TryCreateAsync(replacement, cancellationToken);
            }
            return result;
        }
    }
}
