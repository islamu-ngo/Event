// ABOUTME: SQLite integration tests for IntegrationSync stale-lease recovery and exact fenced settlement.
// ABOUTME: Proves provider-handoff ambiguity is parked while reclaimed owners reject stale completion.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("IntegrationSyncOutboxRepository")]
public sealed class IntegrationSyncOutboxRepositoryTests
{
    [Test]
    public async Task StaleClaimIsReclaimedAndOnlyTheExactNewOwnerCanSettle()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using ExploreDbContext context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
        DateTime now = DateTime.UtcNow;
        IntegrationSyncOutbox outbox = await SeedAsync(context, now.AddMinutes(-10));
        var repository = new IntegrationSyncOutboxRepository(context);
        Guid staleToken = outbox.ProcessingLeaseToken!.Value;
        DateTime staleStartedAt = outbox.ProcessingStartedAt!.Value;

        IReadOnlyList<IntegrationSyncOutbox> candidates = await repository.GetPendingBatch(
            10,
            now,
            now.AddMinutes(-5),
            CancellationToken.None);
        await Assert.That(candidates.Select(candidate => candidate.Id)).Contains(outbox.Id);

        Guid newToken = Guid.CreateVersion7();
        DateTime newStartedAt = now;
        bool reclaimed = await repository.TryClaimAsync(
            new IntegrationSyncClaimRequest(outbox.TenantId, outbox.Id, newToken, newStartedAt, now.AddMinutes(-5)),
            CancellationToken.None);
        await Assert.That(reclaimed).IsTrue();

        bool staleSettled = await repository.CompleteAsync(
            new IntegrationSyncClaimIdentity(outbox.TenantId, outbox.Id, staleToken, staleStartedAt),
            now,
            CancellationToken.None);
        bool currentSettled = await repository.CompleteAsync(
            new IntegrationSyncClaimIdentity(outbox.TenantId, outbox.Id, newToken, newStartedAt),
            now,
            CancellationToken.None);
        await Assert.That(staleSettled).IsFalse();
        await Assert.That(currentSettled).IsTrue();
    }

    [Test]
    public async Task ProviderHandoffBarrierIsSelectedOnlyForAmbiguousParking()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using ExploreDbContext context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
        DateTime now = DateTime.UtcNow;
        IntegrationSyncOutbox outbox = await SeedAsync(context, now);
        var repository = new IntegrationSyncOutboxRepository(context);
        var claim = new IntegrationSyncClaimIdentity(
            outbox.TenantId,
            outbox.Id,
            outbox.ProcessingLeaseToken!.Value,
            outbox.ProcessingStartedAt!.Value);

        await Assert.That(await repository.MarkProviderHandoffStartedAsync(claim, now, CancellationToken.None)).IsTrue();
        IReadOnlyList<IntegrationSyncOutbox> candidates = await repository.GetPendingBatch(
            10,
            now.AddMinutes(10),
            now.AddMinutes(5),
            CancellationToken.None);

        await Assert.That(candidates.Select(candidate => candidate.Id)).Contains(outbox.Id);
        await Assert.That(await repository.ParkAmbiguousAsync(claim, now.AddMinutes(10), CancellationToken.None)).IsTrue();
        context.ChangeTracker.Clear();
        IntegrationSyncOutbox persisted = await context.IntegrationSyncOutbox.IgnoreQueryFilters().SingleAsync();
        await Assert.That(persisted.Status).IsEqualTo(IntegrationSyncStatus.DeadLettered);
        await Assert.That(persisted.LastError).IsEqualTo(IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous);

        IntegrationSyncOutbox? resolved = await repository.ResolveAmbiguousAsync(
            new IntegrationSyncRecoveryRequest(
                outbox.TenantId,
                outbox.Id,
                IntegrationSyncRecoveryDecision.RetryDefinitelyNotAccepted,
                "incident-verified-not-accepted",
                Guid.CreateVersion7(),
                now.AddMinutes(11)),
            CancellationToken.None);
        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.Status).IsEqualTo(IntegrationSyncStatus.RetryScheduled);
        await Assert.That(resolved.LastError).IsEqualTo(IntegrationSyncFailureCodes.OperatorRetryDefinitelyNotAccepted);
    }

    [Test]
    public async Task MalformedProcessingEvidenceIsParkedInsteadOfReplayed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using ExploreDbContext context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
        DateTime now = DateTime.UtcNow;
        IntegrationSyncOutbox outbox = await SeedAsync(context, now.AddMinutes(-10));
        outbox.ProcessingLeaseToken = null;
        await context.SaveChangesAsync();
        var repository = new IntegrationSyncOutboxRepository(context);

        IReadOnlyList<IntegrationSyncOutbox> candidates = await repository.GetPendingBatch(
            10,
            now,
            now.AddMinutes(-5),
            CancellationToken.None);
        bool parked = await repository.ParkMalformedProcessingAsync(
            outbox.TenantId,
            outbox.Id,
            now,
            CancellationToken.None);

        await Assert.That(candidates.Select(candidate => candidate.Id)).Contains(outbox.Id);
        await Assert.That(parked).IsTrue();
    }

    [Test]
    public async Task QueueHealthSnapshotReportsOnlyBoundedCrossTenantCounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using ExploreDbContext context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
        DateTime now = DateTime.UtcNow;
        await SeedAsync(context, now.AddMinutes(-10));

        QueueDrainHealthSnapshot snapshot = await new QueueDrainHealthRepository(context).GetSnapshotAsync(
            now,
            now.AddMinutes(-5),
            CancellationToken.None);

        await Assert.That(snapshot.IntegrationStale).IsEqualTo(1);
        await Assert.That(snapshot.IntegrationDue).IsEqualTo(0);
        await Assert.That(snapshot.IncomingDue).IsEqualTo(0);
        await Assert.That(snapshot.PdsDue).IsEqualTo(0);
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection) => new(
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options);

    private static async Task<IntegrationSyncOutbox> SeedAsync(ExploreDbContext context, DateTime startedAt)
    {
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Integration sync repository test",
            Slug = $"integration-sync-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = startedAt
        };
        var outbox = new IntegrationSyncOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Kind = IntegrationKind.Listmonk,
            SourceType = "test",
            SourceId = Guid.CreateVersion7(),
            SubscriberEmail = "integration-sync@example.test",
            SubscriberPayloadJson = "{}",
            ListmonkListId = 1,
            Status = IntegrationSyncStatus.Processing,
            AttemptCount = 1,
            MaxAttempts = 5,
            ProcessingStartedAt = startedAt,
            ProcessingLeaseToken = Guid.CreateVersion7(),
            CreatedAt = startedAt
        };
        context.AddRange(tenant, outbox);
        await context.SaveChangesAsync();
        return outbox;
    }
}
