using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationProviderSubmissionWriteEffectTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 16, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task HeadlessProviderSubmissionPinsProviderLineageAndCreatesIdentifiersOnlyEffect()
    {
        RegistrationAttempt attempt = Attempt();
        RegistrationSubmission submission = attempt.SubmitHeadlessProvider(
            Evidence("answers"), Now.AddSeconds(1), Transport("idempotency"));

        RegistrationProviderSubmissionWriteEffect effect =
            RegistrationProviderSubmissionWriteEffect.Create(attempt, submission, Now.AddSeconds(1));

        await Assert.That(submission.RegistrationProviderBindingId).IsEqualTo(attempt.RegistrationProviderBindingId);
        await Assert.That(submission.ProviderMappingRevisionHash).IsEqualTo(attempt.ProviderMappingRevisionHash);
        await Assert.That(submission.ProviderSubmissionId).IsNull();
        await Assert.That(effect.RegistrationSubmissionId).IsEqualTo(submission.Id);
        await Assert.That(effect.RegistrationAttemptId).IsEqualTo(attempt.Id);
        await Assert.That(typeof(RegistrationProviderSubmissionWriteEffect).GetProperties()
            .Any(property => property.Name.Contains("Answer", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task AmbiguousProviderWriteParksWithoutRetryingOrChangingSubmission()
    {
        RegistrationAttempt attempt = Attempt();
        RegistrationSubmission submission = attempt.SubmitHeadlessProvider(
            Evidence("answers"), Now.AddSeconds(1), Transport("idempotency"));
        RegistrationProviderSubmissionWriteEffect effect =
            RegistrationProviderSubmissionWriteEffect.Create(attempt, submission, Now.AddSeconds(1));
        Guid firstLease = Guid.CreateVersion7();
        effect.Claim("worker", firstLease, Now.AddMinutes(1), Now.AddSeconds(2));
        effect.ScheduleRetry(firstLease, effect.ProcessingFence, "provider_rate_limited", Now.AddMinutes(2), Now.AddSeconds(3));
        Guid secondLease = Guid.CreateVersion7();
        effect.Claim("worker", secondLease, Now.AddMinutes(4), Now.AddMinutes(2));

        effect.ParkAmbiguous(secondLease, effect.ProcessingFence, "provider_write_outcome_unknown", Now.AddMinutes(3));

        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(effect.ParkedAt).IsEqualTo(Now.AddMinutes(3));
        await Assert.That(effect.NextAttemptAt).IsNull();
        await Assert.That(submission.StatusId).IsEqualTo((int)RegistrationSubmissionStatusEnum.Received);
        await Assert.That(submission.FinalizedAt).IsNull();
    }

    private static RegistrationAttempt Attempt() => RegistrationAttempt.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        CapabilityTokenHash.Create(Hash("capability")), Guid.CreateVersion7(), Evidence("mapping"),
        Now, Now.AddHours(1));

    private static RegistrationEvidenceHash Evidence(string value) =>
        RegistrationEvidenceHash.Create(Hash(value));

    private static RegistrationTransportIdempotencyHash Transport(string value) =>
        RegistrationTransportIdempotencyHash.Create(Hash(value));

    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
