using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationFinalizationEffectTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task RequestDuringProcessingInvalidatesTheStaleLeaseAndRequeuesTheEffect()
    {
        RegistrationOrder order = RegistrationOrder.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            BookingPartyTypeEnum.Individual,
            Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            null,
            null,
            "EUR",
            UtcNow,
            UtcNow.AddMinutes(15));
        RegistrationFinalizationEffect effect = RegistrationFinalizationEffect.Create(order, UtcNow);
        effect.Claim("requirements-worker", Guid.CreateVersion7(), UtcNow.AddMinutes(1), UtcNow.AddSeconds(1));

        effect.Request(UtcNow.AddSeconds(2));

        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(effect.ProcessingFence).IsEqualTo(1);
        await Assert.That(effect.ProcessingLeaseToken).IsNull();
        await Assert.That(effect.NextAttemptAt).IsEqualTo(UtcNow.AddSeconds(2));
    }
}
