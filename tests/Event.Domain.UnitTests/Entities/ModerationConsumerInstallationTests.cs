namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationConsumerInstallationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Installation_RetainsObservedArtifactsAndAccountableTerminalWithdrawal()
    {
        var source = Install();
        var revoker = Guid.CreateVersion7();
        source.Withdraw(1, revoker, Now.AddMinutes(1));
        source.ValidatePersistedState();
        await Assert.That(source.RuntimeGeneration).IsEqualTo(new string('a', 64));
        await Assert.That(source.InventoryFingerprint).IsEqualTo(new string('b', 64));
        await Assert.That(source.Revision).IsEqualTo(2L);
        await Assert.That(source.WithdrawnByHumanId).IsEqualTo(revoker);
        await Assert.That(() => source.Withdraw(2, revoker, Now.AddMinutes(2))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task InvalidInstallation_RejectsMalformedConsumerModeArtifactsAndWindow(int change)
    {
        await Assert.That(() => ModerationConsumerInstallation.Install(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            change == 0 ? "unknown consumer" : "resource.payload", change == 1 ? 2 : 1,
            change == 2 ? "unobserved-generation" : new string('a', 64),
            change == 3 ? new string('B', 64) : new string('b', 64),
            change == 4 ? Guid.Empty : Guid.CreateVersion7(), 2, Now,
            change == 5 ? Now : Now.AddMinutes(5))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("native.operations")]
    [Arguments("resource.redirect")]
    [Arguments("queued.webhook")]
    public async Task InstallationAndChallengeRetainTheirOwnConsumerIdentity(string consumerId)
    {
        var installed = ModerationConsumerInstallation.Install(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), consumerId, 1, new string('a', 64), new string('b', 64),
            Guid.CreateVersion7(), 2, Now, Now.AddMinutes(5));
        var challenge = ModerationConsumerMeasurementChallenge.Prepare(Guid.CreateVersion7(), installed,
            Guid.CreateVersion7(), Guid.CreateVersion7(), new string('c', 64), new string('d', 64), new string('e', 64),
            Guid.CreateVersion7(), 3, Now, Now.AddMinutes(1));
        installed.Withdraw(1, Guid.CreateVersion7(), Now.AddSeconds(1));
        challenge.ValidatePersistedState();
        await Assert.That(challenge.ConsumerId).IsEqualTo(consumerId);
        await Assert.That(challenge.InstallationId).IsEqualTo(installed.Id);
        await Assert.That(challenge.InstallationRevision).IsEqualTo(1L);
    }
    private static ModerationConsumerInstallation Install() =>
        ModerationConsumerInstallation.Install(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), "resource.payload", 1,
            new string('a', 64), new string('b', 64), Guid.CreateVersion7(), 2, Now, Now.AddMinutes(5));
}
