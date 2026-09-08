using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class OutboxRepositoryClaimTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ReplacementClaim_FencesStaleCompletionAndFailure()
    {
        await fixture.ResetAsync();
        var message = CreateMessage(maxRetries: 3);
        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.OutboxMessages.Add(message);
            await seedContext.SaveChangesAsync();
        }

        var claimedAt = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);
        await using var contextA = fixture.CreateDbContext();
        await using var contextB = fixture.CreateDbContext();
        var repositoryA = new OutboxRepository(contextA);
        var repositoryB = new OutboxRepository(contextB);

        var leaseA = await repositoryA.TryClaimForProcessing(message.Id, claimedAt);
        var earlyClaim = await repositoryB.TryClaimForProcessing(message.Id, leaseA!.Value.AddTicks(-10));
        var leaseB = await repositoryB.TryClaimForProcessing(message.Id, leaseA.Value);

        await Assert.That(earlyClaim).IsNull();
        await Assert.That(leaseB).IsNotNull();
        await Assert.That(leaseB).IsNotEqualTo(leaseA);
        await Assert.That(await repositoryA.MarkAsCompleted(message.Id, leaseA.Value)).IsFalse();
        await Assert.That(await repositoryA.MarkAsFailed(
            message.Id,
            leaseA.Value,
            "stale_failure",
            true,
            1,
            claimedAt.AddMinutes(6))).IsEqualTo(OutboxFailureTransition.NotOwned);
        await Assert.That(await repositoryB.MarkAsCompleted(message.Id, leaseB!.Value)).IsTrue();
        await Assert.That(await repositoryB.MarkAsFailed(
            message.Id,
            leaseB.Value,
            "must_not_overwrite_completed",
            true,
            1,
            claimedAt.AddMinutes(7))).IsEqualTo(OutboxFailureTransition.NotOwned);

        await using var verifyContext = fixture.CreateDbContext();
        var row = await verifyContext.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.Completed);
        await Assert.That(row.RetryCount).IsEqualTo(0);
        await Assert.That(row.LastError).IsNull();
    }

    [Test]
    public async Task MarkAsFailed_UsesPersistedMaxRetries()
    {
        await fixture.ResetAsync();
        var message = CreateMessage(maxRetries: 2);
        await using var context = fixture.CreateDbContext();
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var repository = new OutboxRepository(context);
        var firstAttemptAt = new DateTime(2026, 7, 12, 13, 0, 0, DateTimeKind.Utc);

        var firstLease = await repository.TryClaimForProcessing(message.Id, firstAttemptAt);
        var firstFailure = await repository.MarkAsFailed(
            message.Id,
            firstLease!.Value,
            "dispatch_failed",
            true,
            10,
            firstAttemptAt.AddSeconds(1));
        var secondLease = await repository.TryClaimForProcessing(message.Id, firstAttemptAt.AddSeconds(11));
        var secondFailure = await repository.MarkAsFailed(
            message.Id,
            secondLease!.Value,
            "dispatch_failed",
            true,
            10,
            firstAttemptAt.AddSeconds(12));
        var earlyReconciliationClaim = await repository.TryClaimDeadLetterReconciliation(
            message.Id,
            secondLease.Value.AddTicks(-TimeSpan.TicksPerMicrosecond));
        var recoveryLease = await repository.TryClaimDeadLetterReconciliation(
            message.Id,
            secondLease.Value);
        var staleReconciliation = await repository.MarkDeadLetterReconciled(
            message.Id,
            secondLease.Value);
        var reconciled = await repository.MarkDeadLetterReconciled(
            message.Id,
            recoveryLease!.Value);

        await Assert.That(firstFailure).IsEqualTo(OutboxFailureTransition.RetryScheduled);
        await Assert.That(secondFailure).IsEqualTo(OutboxFailureTransition.DeadLettered);
        await Assert.That(earlyReconciliationClaim).IsNull();
        await Assert.That(recoveryLease).IsNotNull();
        await Assert.That(staleReconciliation).IsFalse();
        await Assert.That(reconciled).IsTrue();
        var row = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        await Assert.That(row.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(row.RetryCount).IsEqualTo(2);
        await Assert.That(row.DeadLetteredAt).IsNotNull();
        await Assert.That(row.NextRetryAt).IsNull();
    }

    private static OutboxMessage CreateMessage(int maxRetries) => new()
    {
        Id = Guid.CreateVersion7(),
        AggregateType = "claim-test",
        AggregateId = Guid.CreateVersion7(),
        EventType = "claim-test",
        Status = OutboxMessageStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        MaxRetries = maxRetries
    };
}
