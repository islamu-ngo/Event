namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationDeploymentAdoptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task IndependentAdoption_RetainsExactSourceAndTerminatesOnWithdrawal()
    {
        var source = Source();
        var adoption = ModerationDeploymentAdoption.Adopt(Guid.CreateVersion7(), source,
            Guid.CreateVersion7(), 4, Now, Now.AddMinutes(5));
        await Assert.That(adoption.SourceId).IsEqualTo(source.Id);
        await Assert.That(adoption.SourceWriterGeneration).IsEqualTo(source.WriterGeneration);
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(Source(), Now)).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, Now.AddMinutes(5))).IsFalse();
        adoption.Withdraw(1, Guid.CreateVersion7(), Now.AddMinutes(1));
        await Assert.That(adoption.MatchesCurrent(source, Now.AddMinutes(1))).IsFalse();
        await Assert.That(() => adoption.Withdraw(2, Guid.CreateVersion7(), Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(-5)]
    [Arguments(3)]
    public async Task CurrentAdoption_RejectsNonUtcObservationOfTheSameInstant(int offsetHours)
    {
        var source = Source();
        var adoption = ModerationDeploymentAdoption.Adopt(Guid.CreateVersion7(), source,
            Guid.CreateVersion7(), 4, Now, Now.AddMinutes(5));
        var observation = Now.AddMinutes(1);

        await Assert.That(adoption.MatchesCurrent(source, observation)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, observation.ToOffset(TimeSpan.FromHours(offsetHours))))
            .IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task Adoption_RejectsSelfApprovalStaleGenerationAndUnboundedLifetime(int change)
    {
        var source = Source();
        await Assert.That(() => ModerationDeploymentAdoption.Adopt(Guid.CreateVersion7(), source,
            change == 0 ? source.RecordedByHumanId : Guid.CreateVersion7(),
            change == 1 ? source.WriterGeneration : 4, Now,
            change switch { 2 => source.ValidUntil.AddTicks(1), 3 => Now, _ => Now.AddMinutes(5) }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task DeploymentCoverage_RejectsDuplicateOrMissingNativeConsumers()
    {
        var source = Source();
        source.Consumers.Add(source.Consumers[0]);
        await Assert.That(source.ValidatePersistedState).Throws<ArgumentException>();
        source.Consumers.Clear();
        await Assert.That(source.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    public async Task DeploymentCoverage_RejectsDefinitionOnlyAndUnknownMode()
    {
        await Assert.That(() => Source(mode: 0)).Throws<ArgumentException>();
        await Assert.That(() => Source(productDigest: "")).Throws<ArgumentException>();
        await Assert.That(() => Source(databaseDigest: "")).Throws<ArgumentException>();
    }

    private static ModerationDeploymentSource Source(
        int mode = 1, string? productDigest = null, string? databaseDigest = null)
    {
        var id = Guid.CreateVersion7();
        return ModerationDeploymentSource.Record(id, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), mode, new string('a', 64), new string('b', 64),
            databaseDigest ?? new string('c', 64), "Sqlite", "3.50",
            [ModerationDeploymentConsumer.Capture(id, 0, "resource.payload", new string('d', 64),
                new string('e', 64), Guid.CreateVersion7(), 1, 1, new string('f', 64),
                productDigest ?? new string('a', 64), Now.AddMinutes(10))],
            Guid.CreateVersion7(), 3, Now, Now.AddMinutes(10));
    }
}
