using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class IncomingWebhookEffectOutboxLifecycleTests
{
    private static readonly DateTime CreatedAt = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Claim_FromPending_CreatesFencedLease()
    {
        var pointer = CreatePointer();
        var leaseToken = Guid.CreateVersion7();

        pointer.Claim("worker-a", leaseToken, CreatedAt.AddMinutes(1), CreatedAt.AddSeconds(1));

        await Assert.That(pointer.Status).IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(pointer.ProcessingLeaseToken).IsEqualTo(leaseToken);
        await Assert.That(pointer.ProcessingFence).IsEqualTo(1);
        await Assert.That(pointer.ProcessingGeneration).IsEqualTo(1);
        await Assert.That(pointer.AttemptCount).IsEqualTo(1);
    }

    [Test]
    public async Task Complete_WithStaleFence_IsRejected()
    {
        var pointer = CreatePointer();
        var firstLease = Guid.CreateVersion7();
        pointer.Claim("worker-a", firstLease, CreatedAt.AddSeconds(2), CreatedAt.AddSeconds(1));
        pointer.RecoverExpiredClaim(CreatedAt.AddSeconds(3));
        var secondLease = Guid.CreateVersion7();
        pointer.Claim("worker-b", secondLease, CreatedAt.AddMinutes(1), CreatedAt.AddSeconds(4));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            pointer.Complete(firstLease, 1, 1, CreatedAt.AddSeconds(5))));
        await Assert.That(pointer.Status).IsEqualTo(OutboxMessageStatus.Processing);
    }

    [Test]
    public async Task ScheduleRetry_ClearsLeaseAndPreservesDueTime()
    {
        var pointer = CreatePointer();
        var leaseToken = Guid.CreateVersion7();
        pointer.Claim("worker-a", leaseToken, CreatedAt.AddMinutes(1), CreatedAt.AddSeconds(1));
        var nextAttemptAt = CreatedAt.AddMinutes(2);

        pointer.ScheduleRetry(
            leaseToken,
            pointer.ProcessingFence,
            pointer.ProcessingGeneration,
            "coop_effect_transient_failure",
            "The effect will be retried.",
            nextAttemptAt,
            CreatedAt.AddSeconds(2));

        await Assert.That(pointer.Status).IsEqualTo(OutboxMessageStatus.Failed);
        await Assert.That(pointer.NextAttemptAt).IsEqualTo(nextAttemptAt);
        await Assert.That(pointer.ProcessingLeaseToken).IsNull();
    }

    [Test]
    public async Task DeadLetter_StoresOnlyBoundedSanitizedEvidence()
    {
        var pointer = CreatePointer();
        var leaseToken = Guid.CreateVersion7();
        pointer.Claim("worker-a", leaseToken, CreatedAt.AddMinutes(1), CreatedAt.AddSeconds(1));

        pointer.DeadLetter(
            leaseToken,
            pointer.ProcessingFence,
            pointer.ProcessingGeneration,
            "coop_effect_payload_invalid",
            "The retained callback is invalid.",
            CreatedAt.AddSeconds(2));

        await Assert.That(pointer.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(pointer.FailureCategory).IsEqualTo("coop_effect_payload_invalid");
        await Assert.That(pointer.SafeDetail).IsEqualTo("The retained callback is invalid.");
        await Assert.That(pointer.DeadLetteredAt).IsEqualTo(CreatedAt.AddSeconds(2));
        await Assert.That(pointer.ProcessingLeaseToken).IsNull();
    }

    [Test]
    public async Task Redrive_FromDeadLettered_IncrementsGenerationAndResetsAttempts()
    {
        var pointer = CreatePointer();
        var leaseToken = Guid.CreateVersion7();
        pointer.Claim("worker-a", leaseToken, CreatedAt.AddMinutes(1), CreatedAt.AddSeconds(1));
        pointer.DeadLetter(
            leaseToken,
            pointer.ProcessingFence,
            pointer.ProcessingGeneration,
            "coop_effect_payload_invalid",
            "The retained callback is invalid.",
            CreatedAt.AddSeconds(2));

        pointer.Redrive(expectedProcessingGeneration: 1, CreatedAt.AddSeconds(3));

        await Assert.That(pointer.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(pointer.ProcessingGeneration).IsEqualTo(2);
        await Assert.That(pointer.AttemptCount).IsEqualTo(0);
        await Assert.That(pointer.FailureCategory).IsNull();
        await Assert.That(pointer.SafeDetail).IsNull();
    }

    private static IncomingWebhookEffectOutbox CreatePointer() => IncomingWebhookEffectOutbox.CreatePending(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "coop",
        "provider-decision-1",
        "moderation.coop.decision",
        "sha256:" + new string('a', 64),
        CreatedAt);
}
