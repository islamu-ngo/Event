// ABOUTME: Proves SQLite webhook event and object-transition races resolve as duplicates.
// ABOUTME: Exercises the same portable unique-conflict classifier used by all five persistence providers.

using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Event.Persistence.IntegrationTests;

public sealed class IncomingWebhookPortableDedupeTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly DateTime UtcNow = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentCapture_EventOrObjectTransitionConflict_ReturnsOneDuplicateWithoutThrowing(bool sameEventId)
    {
        string path = Path.Combine(Path.GetTempPath(), $"incoming-webhook-dedupe-{Guid.NewGuid():N}.db");
        try
        {
            await using (ExploreDbContext setup = await CreateContext(path, null))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            using var barrier = new Barrier(2);
            await using ExploreDbContext firstContext = await CreateContext(path, new IdentityReadBarrierInterceptor(barrier));
            await using ExploreDbContext secondContext = await CreateContext(path, new IdentityReadBarrierInterceptor(barrier));
            var first = new IncomingWebhookMessageRepository(firstContext);
            var second = new IncomingWebhookMessageRepository(secondContext);
            string transitionKey = "checkout.session.completed:cs_race";
            IncomingWebhookMessage firstMessage = Message("evt_race_1", transitionKey, 'a');
            IncomingWebhookMessage secondMessage = Message(sameEventId ? "evt_race_1" : "evt_race_2", transitionKey, 'b');

            bool[] outcomes = await Task.WhenAll(
                Task.Run(() => first.TryCreateAsync(firstMessage, CancellationToken.None)),
                Task.Run(() => second.TryCreateAsync(secondMessage, CancellationToken.None)));

            await Assert.That(outcomes.Count(value => value)).IsEqualTo(1);
            await Assert.That(outcomes.Count(value => !value)).IsEqualTo(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task UniqueViolationWithoutEventOrObjectIdentityMatch_IsRethrown()
    {
        string path = Path.Combine(Path.GetTempPath(), $"incoming-webhook-unresolved-{Guid.NewGuid():N}.db");
        try
        {
            await using ExploreDbContext context = await CreateContext(path, null);
            await context.Database.EnsureCreatedAsync();
            var repository = new IncomingWebhookMessageRepository(context);
            IncomingWebhookMessage first = Message("evt_primary", "checkout.session.completed:cs_primary", 'a');
            IncomingWebhookMessage unrelated = Message("evt_unrelated", "checkout.session.completed:cs_unrelated", 'b');
            typeof(IncomingWebhookMessage).GetProperty(nameof(IncomingWebhookMessage.Id))!.SetValue(unrelated, first.Id);
            await repository.TryCreateAsync(first, CancellationToken.None);
            context.ChangeTracker.Clear();

            await Assert.That(() => repository.TryCreateAsync(unrelated, CancellationToken.None))
                .Throws<DbUpdateException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IncomingWebhookMessage Message(string eventId, string idempotencyKey, char hashCharacter) =>
        IncomingWebhookMessage.CreateVerified(
            TenantId,
            "stripe-connect",
            eventId,
            idempotencyKey,
            "checkout.session.completed",
            "{}"u8,
            "sha256:" + new string(hashCharacter, 64),
            "application/json",
            "utf-8",
            null,
            UtcNow,
            UtcNow.AddSeconds(1),
            UtcNow.AddDays(14),
            "test-v1",
            UtcNow.AddDays(30),
            UtcNow.AddDays(90),
            UtcNow.AddDays(14),
            UtcNow.AddDays(30));

    private static async Task<ExploreDbContext> CreateContext(string path, IInterceptor? interceptor)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            DefaultTimeout = 30,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = OFF; PRAGMA journal_mode = WAL;";
            await command.ExecuteNonQueryAsync();
        }

        DbContextOptionsBuilder<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention();
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        var context = new ExploreDbContext(options.Options);
        context.EnableTenantFilterBypass("Webhook portable dedupe test.");
        return context;
    }

    private sealed class IdentityReadBarrierInterceptor(Barrier barrier) : DbCommandInterceptor
    {
        private int synchronized;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref synchronized, 1) == 0 &&
                command.CommandText.Contains("incoming_webhook_messages", StringComparison.OrdinalIgnoreCase))
            {
                barrier.SignalAndWait(cancellationToken);
            }

            return ValueTask.FromResult(result);
        }
    }
}
