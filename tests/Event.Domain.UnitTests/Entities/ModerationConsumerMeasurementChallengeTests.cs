using System.Security.Cryptography;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationConsumerMeasurementChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Challenge_FreezesOriginalSourceAndBindingWithoutConformance()
    {
        var installed = Installation();
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var challenge = ModerationConsumerMeasurementChallenge.Prepare(Guid.CreateVersion7(), installed,
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), new string('b', 64), nonce,
            Guid.CreateVersion7(), 3, Now, Now.AddMinutes(1));
        installed.Withdraw(1, Guid.CreateVersion7(), Now.AddSeconds(1));
        challenge.ValidatePersistedState();
        await Assert.That(challenge.InstallationRevision).IsEqualTo(1L);
        await Assert.That(challenge.RuntimeGeneration).IsEqualTo(installed.RuntimeGeneration);
        await Assert.That(challenge.InstallationWriterGeneration).IsEqualTo(2L);
        await Assert.That(challenge.WriterGeneration).IsEqualTo(3L);
        await Assert.That(challenge.Nonce).IsEqualTo(nonce);
        await Assert.That(challenge.ValidUntil).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task InvalidChallenge_CannotOutliveOrRequalifyAWithdrawnInstallation(int change)
    {
        var installed = Installation();
        if (change == 0) installed.Withdraw(1, Guid.CreateVersion7(), Now);
        await Assert.That(() => ModerationConsumerMeasurementChallenge.Prepare(
            Guid.CreateVersion7(), installed, Guid.CreateVersion7(), Guid.CreateVersion7(),
            change == 1 ? "unbound" : new string('a', 64), new string('b', 64),
            change == 2 ? "" : Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
            change == 3 ? Guid.Empty : Guid.CreateVersion7(), change == 4 ? 2 : 3, Now,
            change == 5 ? Now : change == 6 ? Now.AddMinutes(6) : Now.AddMinutes(1))).Throws<ArgumentException>();
    }

    private static ModerationConsumerInstallation Installation() =>
        ModerationConsumerInstallation.Install(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), "resource.payload", 1,
            new string('c', 64), new string('d', 64), Guid.CreateVersion7(), 2, Now, Now.AddMinutes(5));

    [Test]
    public async Task OriginalAnchorRemainsExactWithoutExtendingTheChallenge()
    {
        var effect = Guid.CreateVersion7();
        var challenge = ModerationConsumerMeasurementChallenge.Prepare(Guid.CreateVersion7(), Installation(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64), new string('b', 64),
            new string('e', 64), Guid.CreateVersion7(), 3, Now, Now.AddMinutes(1),
            effect, new string('f', 64), new string('a', 64));
        await Assert.That(challenge.OriginalEffectId).IsEqualTo(effect);
        await Assert.That(challenge.OriginalSnapshotHash).IsEqualTo(new string('f', 64));
        await Assert.That(challenge.OriginalEffectBindingHash).IsEqualTo(new string('a', 64));
        await Assert.That(challenge.ValidUntil).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    [Arguments("without-effect")]
    [Arguments("missing-snapshot")]
    [Arguments("missing-binding")]
    [Arguments("invalid-effect")]
    public async Task PartialOrInvalidOriginalAnchorsAreRejected(string change)
    {
        await Assert.That(() => ModerationConsumerMeasurementChallenge.Prepare(
            Guid.CreateVersion7(), Installation(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new string('a', 64), new string('b', 64), new string('e', 64),
            Guid.CreateVersion7(), 3, Now, Now.AddMinutes(1),
            change == "without-effect" ? null : change == "invalid-effect" ? Guid.Empty : Guid.CreateVersion7(),
            change == "missing-snapshot" ? null : new string('f', 64),
            change == "missing-binding" ? null : new string('a', 64))).Throws<ArgumentException>();
    }
}
