namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationMeasuredOperationsSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Independent_acceptance_preserves_finite_source_and_terminal_withdrawal()
    {
        var source = Source();
        await Assert.That(() => ModerationMeasuredOperationsAcceptance.Accept(Guid.CreateVersion7(), source,
            source.RecordedByHumanId, 5, Now, Now.AddMinutes(2))).Throws<ArgumentException>();
        await Assert.That(() => ModerationMeasuredOperationsAcceptance.Accept(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 4, Now, Now.AddMinutes(2))).Throws<ArgumentException>();
        await Assert.That(() => ModerationMeasuredOperationsAcceptance.Accept(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, source.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
        var accepted = ModerationMeasuredOperationsAcceptance.Accept(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, Now.AddMinutes(2));
        await Assert.That(accepted.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(accepted.MatchesCurrent(Source(), Now)).IsFalse();
        await Assert.That(accepted.MatchesCurrent(source, accepted.ValidUntil)).IsFalse();
        accepted.Withdraw(1, Guid.CreateVersion7(), Now.AddDays(1));
        await Assert.That(accepted.SourceWriterGeneration).IsEqualTo(4L);
        await Assert.That(accepted.WriterGeneration).IsEqualTo(5L);
        await Assert.That(accepted.MatchesCurrent(source, Now)).IsFalse();
        await Assert.That(() => accepted.Withdraw(2, Guid.CreateVersion7(), Now.AddDays(2)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("consumer")]
    [Arguments("extra-consumer")]
    [Arguments("staffing")]
    [Arguments("recovery")]
    [Arguments("remote")]
    [Arguments("recognition")]
    [Arguments("purpose")]
    [Arguments("expiry")]
    public async Task Every_missing_current_condition_blocks_composite_qualification(string omission)
    {
        var proof = Proof();
        var evidence = Evidence() with
        {
            Staffing = proof,
            Recovery = proof,
            Transport = proof,
            KeyConvergence = proof,
            RecognitionPrivacy = proof,
            RecognitionPurpose = 1,
            Consumers = [new("resource.payload", proof)]
        };
        evidence = omission switch
        {
            "consumer" => evidence with { Consumers = [new("resource.payload", null)] },
            "extra-consumer" => evidence with { Consumers = [new("resource.payload", proof), new("foreign", proof)] },
            "staffing" => evidence with { Staffing = null },
            "recovery" => evidence with { Recovery = null },
            "remote" => evidence with { Transport = null },
            "recognition" => evidence with { KeyConvergence = null },
            "purpose" => evidence with { RecognitionPurpose = 2 },
            _ => evidence with { Recovery = proof with { ValidUntil = Now } }
        };
        await Assert.That(evidence.IsComplete(["resource.payload"], 2, 1, Now)).IsFalse();
    }

    [Test]
    public async Task Embedded_waives_only_transport_and_never_canonical_or_erasure_recovery()
    {
        var evidence = Evidence() with
        {
            Staffing = Proof(),
            Recovery = Proof(),
            Consumers = [new("resource.payload", Proof())]
        };
        await Assert.That(evidence.IsComplete(["resource.payload"], 1, 0, Now)).IsTrue();
        await Assert.That(evidence.IsComplete(["resource.payload"], 2, 0, Now)).IsFalse();
        await Assert.That((evidence with { Recovery = null }).IsComplete(["resource.payload"], 1, 0, Now)).IsFalse();
        await Assert.That(evidence.IsComplete(["resource.payload", "native.operations"], 1, 0, Now)).IsFalse();
    }

    [Test]
    [Arguments("generation")]
    [Arguments("mode")]
    [Arguments("expiry")]
    [Arguments("controller")]
    [Arguments("duplicate")]
    [Arguments("variant")]
    public async Task Source_rejects_unbounded_or_ambiguous_native_evidence(string invalid)
    {
        await Assert.That(() => Source(invalid)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("019a0000-0000-7000-0000-000000000001")]
    [Arguments("019a0000-0000-7000-7000-000000000001")]
    [Arguments("019a0000-0000-7000-c000-000000000001")]
    [Arguments("019a0000-0000-7000-f000-000000000001")]
    public async Task NonRfcProofIdentity_CannotBeRetainedOrQualifyMeasuredOperations(string id)
    {
        var proof = Proof() with { Id = Guid.Parse(id) };
        var evidence = Evidence() with
        {
            Staffing = proof,
            Recovery = Proof(),
            Consumers = [new("resource.payload", Proof())]
        };
        await Assert.That(proof.IsCurrent(Now)).IsFalse();
        await Assert.That(() => evidence.Validate(Now)).Throws<ArgumentException>();
        await Assert.That(evidence.IsComplete(["resource.payload"], 1, 0, Now)).IsFalse();
    }

    private static ModerationMeasuredOperationsEvidence Evidence() => new(
        new string('a', 64), new string('b', 64), "Sqlite", "test-provider",
        [new("resource.payload", null)], null, null, 0, null, null, null);

    private static ModerationMeasuredOperationsProofReference Proof() =>
        new(Guid.CreateVersion7(), 1, 3, Now, Now.AddMinutes(3));

    [Test]
    [Arguments("effect")]
    [Arguments("snapshot")]
    [Arguments("binding")]
    [Arguments("custody")]
    [Arguments("bad-effect")]
    [Arguments("bad-hash")]
    [Arguments("expired-custody")]
    [Arguments("extended")]
    public async Task Original_report_requires_complete_exact_finite_custody(string invalid)
    {
        await Assert.That(() => OriginalSource(invalid)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Original_coordinates_preserve_gaps_and_independent_acceptance_ceiling()
    {
        var source = OriginalSource();
        await Assert.That(source.OriginalEffectId).IsNotNull();
        await Assert.That(source.OriginalSnapshotHash).IsEqualTo(new string('e', 64));
        await Assert.That(source.OriginalEffectBindingHash).IsEqualTo(new string('f', 64));
        await Assert.That(source.OriginalCustodyUntil).IsEqualTo(Now.AddMinutes(3));
        await Assert.That(source.Evidence.Recovery).IsNull();
        await Assert.That(source.Evidence.KeyConvergence).IsNull();
        await Assert.That(source.Evidence.Transport).IsNull();
        var acceptance = ModerationMeasuredOperationsAcceptance.Accept(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, source.ValidUntil);
        await Assert.That(acceptance.SourceRevision).IsEqualTo(1L);
        await Assert.That(acceptance.SourceWriterGeneration).IsEqualTo(4L);
        await Assert.That(acceptance.MatchesCurrent(source, source.ValidUntil)).IsFalse();
        await Assert.That(source.Evidence.IsComplete(["resource.payload"], 1, 0, Now)).IsFalse();
        await Assert.That(() => ModerationMeasuredOperationsAcceptance.Accept(Guid.CreateVersion7(), source,
            source.ReviewerHumanId, 5, Now, source.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
    }

    private static ModerationMeasuredOperationsSource OriginalSource(string? invalid = null)
    {
        var reviewer = Guid.CreateVersion7();
        var recorder = Guid.CreateVersion7();
        return ModerationMeasuredOperationsSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('c', 64), new string('d', 64),
            Evidence(), reviewer, reviewer, recorder, recorder, 4, Now, Now.AddMinutes(3),
            invalid == "effect" ? null : invalid == "bad-effect" ? Guid.Empty : Guid.CreateVersion7(),
            invalid == "snapshot" ? null : invalid == "bad-hash" ? "invalid" : new string('e', 64),
            invalid == "binding" ? null : new string('f', 64),
            invalid == "custody" ? null : invalid == "expired-custody" ? Now
                : invalid == "extended" ? Now.AddMinutes(3).AddTicks(-1) : Now.AddMinutes(3));
    }

    private static ModerationMeasuredOperationsSource Source(string? invalid = null)
    {
        var recorder = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var evidence = invalid switch
        {
            "duplicate" => Evidence() with { Consumers = [new("resource.payload", null), new("resource.payload", null)] },
            "variant" => Evidence() with
            {
                Staffing = Proof() with { Id = Guid.Parse("019a0000-0000-7000-0000-000000000001") }
            },
            _ => Evidence()
        };
        return ModerationMeasuredOperationsSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), invalid == "mode" ? 0 : 1,
            new string('c', 64), new string('d', 64), evidence, reviewer, reviewer,
            invalid == "controller" ? reviewer : recorder, recorder, invalid == "generation" ? 0 : 4,
            Now, invalid == "expiry" ? DateTimeOffset.MaxValue : Now.AddMinutes(3));
    }
}
