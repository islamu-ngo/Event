using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationRequirementFulfillmentTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task OptionalRequirementCanBeSkippedButRequiredRequirementCannot()
    {
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder();
        RegistrationRequirement optional = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, UtcNow);
        RegistrationRequirement required = RegistrationRequirement.Create(
            workflow, 2, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, UtcNow);

        RegistrationRequirementFulfillment skipped = RegistrationRequirementFulfillment.CreateSkipped(
            order, optional, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, UtcNow);

        await Assert.That(skipped.IsSkipped).IsTrue();
        await Assert.That(skipped.SourceRegistrationSubmissionId).IsNull();
        await Assert.That(() => RegistrationRequirementFulfillment.CreateSkipped(
            order, required, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, UtcNow))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EffectClaimUsesMonotonicFenceWhenExpiredLeaseIsRecovered()
    {
        (RegistrationOrder order, _) = CreateOrder();
        RegistrationFinalizationEffect effect = RegistrationFinalizationEffect.Create(order, UtcNow);
        Guid firstLease = Guid.CreateVersion7();
        effect.Claim("worker-a", firstLease, UtcNow.AddSeconds(10), UtcNow);
        effect.RecoverExpiredClaim(UtcNow.AddSeconds(10));
        effect.Claim("worker-b", Guid.CreateVersion7(), UtcNow.AddMinutes(1), UtcNow.AddSeconds(10));

        await Assert.That(effect.AttemptCount).IsEqualTo(2);
        await Assert.That(effect.ProcessingFence).IsEqualTo(2);
        await Assert.That(effect.ProcessingLeaseToken).IsNotEqualTo(firstLease);
    }

    [Test]
    public async Task ProviderSubmissionWriteEffect_ParksAmbiguousDeliveryWithoutRetry()
    {
        RegistrationAttempt attempt = CreateProviderAttempt();
        RegistrationSubmission submission = attempt.SubmitHeadlessProvider(
            RegistrationEvidenceHash.Create(Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())),
            UtcNow.AddMinutes(1),
            null);
        RegistrationProviderSubmissionWriteEffect effect = RegistrationProviderSubmissionWriteEffect.Create(
            attempt, submission, UtcNow.AddMinutes(1));
        Guid lease = Guid.CreateVersion7();
        effect.Claim("worker", lease, UtcNow.AddMinutes(3), UtcNow.AddMinutes(2));

        effect.ParkAmbiguous(lease, effect.ProcessingFence, "provider_response_lost", UtcNow.AddMinutes(2).AddSeconds(1));

        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(effect.ParkedAt).IsNotNull();
        await Assert.That(effect.NextAttemptAt).IsNull();
        await Assert.That(effect.ProcessingLeaseToken).IsNull();
    }

    [Test]
    public async Task ProviderSubmissionWriteEffect_RetryAndDeadLetterClearActiveLease()
    {
        RegistrationAttempt attempt = CreateProviderAttempt();
        RegistrationSubmission submission = attempt.SubmitHeadlessProvider(
            RegistrationEvidenceHash.Create(Convert.ToBase64String(Enumerable.Repeat((byte)8, 32).ToArray())),
            UtcNow.AddMinutes(1),
            null);
        RegistrationProviderSubmissionWriteEffect effect = RegistrationProviderSubmissionWriteEffect.Create(
            attempt, submission, UtcNow.AddMinutes(1));
        Guid firstLease = Guid.CreateVersion7();
        effect.Claim("worker", firstLease, UtcNow.AddMinutes(3), UtcNow.AddMinutes(2));
        effect.ScheduleRetry(firstLease, effect.ProcessingFence, "before_handoff_timeout", UtcNow.AddMinutes(4), UtcNow.AddMinutes(2).AddSeconds(1));

        Guid secondLease = Guid.CreateVersion7();
        effect.Claim("worker", secondLease, UtcNow.AddMinutes(6), UtcNow.AddMinutes(4));
        effect.DeadLetter(secondLease, effect.ProcessingFence, "validation_failed", UtcNow.AddMinutes(4).AddSeconds(1));

        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(effect.DeadLetteredAt).IsNotNull();
        await Assert.That(effect.NextAttemptAt).IsNull();
        await Assert.That(effect.ProcessingLeaseToken).IsNull();
    }

    private static (RegistrationOrder Order, RegistrationWorkflow Workflow) CreateOrder()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, "REGISTRATION", UtcNow);
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId, eventId, Guid.CreateVersion7(), null, BookingPartyTypeEnum.Individual,
            Guid.CreateVersion7(), RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            workflow.Id, null, "EUR", UtcNow, UtcNow.AddMinutes(15));
        return (order, workflow);
    }

    private static RegistrationAttempt CreateProviderAttempt() => RegistrationAttempt.Create(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        CapabilityTokenHash.Create(Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray())),
        Guid.CreateVersion7(),
        RegistrationEvidenceHash.Create(Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray())),
        UtcNow,
        UtcNow.AddMinutes(15));
}
