using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationOperationalBudgetSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task WindowCannotUnderflowItsOriginalObservation()
    {
        await Assert.That(() => Source(terms: Terms() with { Window = TimeSpan.FromTicks(Now.UtcTicks) }))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task CapacityMustBeExplicitAndPositive(int maximum)
    {
        await Assert.That(() => (Terms() with { MaximumOccurrences = maximum }).Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(long.MaxValue)]
    public async Task WindowMustBeExplicitFiniteAndPositive(long ticks)
    {
        await Assert.That(() => (Terms() with { Window = TimeSpan.FromTicks(ticks) }).Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(99)]
    public async Task NonHumanSubjectsCannotInferAServiceMandate(int kind)
    {
        await Assert.That(() => (Terms() with { SubjectKind = (ModerationSubjectKind)kind }).Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UndefinedActionsCannotAcquireBudgetTerms()
    {
        await Assert.That(() => (Terms() with { Action = (ModerationOperationalAction)99 }).Validate())
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task EveryDefinedActionPreservesExplicitHumanTerms()
    {
        foreach (var action in Enum.GetValues<ModerationOperationalAction>())
        {
            var terms = Terms() with { Action = action };
            var source = Source(terms: terms);
            await Assert.That(source.Terms).IsEqualTo(terms);
        }
    }

    [Test]
    public async Task RecorderCannotBeTheNamedAcceptingOperator()
    {
        var recorder = Guid.NewGuid();
        await Assert.That(() => Source(operatorHumanId: recorder, recorder: recorder))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task EmptyHumansCannotRecordOrAcceptGovernance()
    {
        await Assert.That(() => Source(operatorHumanId: Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => Source(recorder: Guid.Empty)).Throws<ArgumentException>();
    }

    [Test]
    public async Task AggregateCoordinatesRequireActualUuidVersionSeven()
    {
        await Assert.That(() => Source(id: Guid.NewGuid())).Throws<ArgumentException>();
        await Assert.That(() => Source(host: Guid.NewGuid())).Throws<ArgumentException>();
        await Assert.That(() => Source(trust: Guid.NewGuid())).Throws<ArgumentException>();
        await Assert.That(() => Source(tenant: Guid.NewGuid())).Throws<ArgumentException>();
    }

    [Test]
    public async Task WindowCannotOverflowItsOriginalRecordedInstant()
    {
        await Assert.That(() => Source(terms: Terms() with
        {
            Window = TimeSpan.FromTicks(DateTimeOffset.MaxValue.UtcTicks - Now.UtcTicks)
        })).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(long.MaxValue)]
    public async Task SourceGenerationMustBePositiveAndFinite(long generation)
    {
        await Assert.That(() => Source(generation: generation)).Throws<ArgumentException>();
    }

    [Test]
    public async Task OnlyNamedIndependentOperatorCanAdopt()
    {
        var source = Source();
        await Assert.That(() => Adopt(source, source.RecordedByHumanId)).Throws<ArgumentException>();
        await Assert.That(() => Adopt(source, Guid.NewGuid())).Throws<ArgumentException>();
        var adoption = Adopt(source);
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
    }

    [Test]
    public async Task AdoptionRequiresANewerFiniteGeneration()
    {
        var source = Source();
        await Assert.That(() => Adopt(source, generation: 2)).Throws<ArgumentException>();
        await Assert.That(() => Adopt(source, generation: long.MaxValue)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ChangedSourceGenerationCannotBorrowAcceptance()
    {
        var source = Source();
        var adoption = Adopt(source);
        var replacement = Source(source.Id, source.HostBindingId, source.TrustDomainId, source.TenantId,
            source.OperatorHumanId, source.RecordedByHumanId, generation: 3);
        await Assert.That(adoption.MatchesCurrent(replacement, Now)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ForeignHostTrustOrTenantCannotBorrowAcceptance(int coordinate)
    {
        var source = Source();
        var adoption = Adopt(source);
        var foreign = Source(source.Id,
            coordinate == 0 ? Guid.CreateVersion7() : source.HostBindingId,
            coordinate == 1 ? Guid.CreateVersion7() : source.TrustDomainId,
            coordinate == 2 ? Guid.CreateVersion7() : source.TenantId,
            source.OperatorHumanId, source.RecordedByHumanId);
        await Assert.That(adoption.MatchesCurrent(foreign, Now)).IsFalse();
    }

    [Test]
    public async Task AcceptanceCannotPrecedeOrOutliveItsSource()
    {
        var source = Source();
        await Assert.That(() => Adopt(source, now: Now.AddTicks(-1))).Throws<ArgumentException>();
        await Assert.That(() => Adopt(source, until: source.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
        await Assert.That(() => Adopt(source, now: source.ValidUntil, until: source.ValidUntil))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AcceptanceExpiresAtItsExactOriginalCutoff()
    {
        var source = Source();
        var adoption = Adopt(source);
        await Assert.That(adoption.MatchesCurrent(source, Now.AddTicks(-1))).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, adoption.ValidUntil.AddTicks(-1))).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, adoption.ValidUntil)).IsFalse();
    }

    [Test]
    public async Task WithdrawalIsOneWayAndPreservesOriginalValidity()
    {
        var source = Source();
        var adoption = Adopt(source);
        var originalExpiry = adoption.ValidUntil;
        await Assert.That(() => adoption.Withdraw(2, source.OperatorHumanId, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => adoption.Withdraw(1, Guid.Empty, Now)).Throws<ArgumentException>();
        await Assert.That(() => adoption.Withdraw(1, source.OperatorHumanId, Now.AddTicks(-1)))
            .Throws<ArgumentException>();
        adoption.Withdraw(1, source.OperatorHumanId, Now.AddMinutes(1));
        adoption.ValidatePersistedState();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.ValidUntil).IsEqualTo(originalExpiry);
        await Assert.That(adoption.MatchesCurrent(source, Now.AddMinutes(2))).IsFalse();
        await Assert.That(() => adoption.Withdraw(2, source.OperatorHumanId, Now.AddMinutes(3)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DiagnosticStringsDoNotExposeHumanCoordinates()
    {
        var source = Source();
        await Assert.That(source.ToString().Contains(source.OperatorHumanId.ToString(), StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(source.ToString().Contains(source.RecordedByHumanId.ToString(), StringComparison.OrdinalIgnoreCase)).IsFalse();
        var adoption = Adopt(source);
        await Assert.That(adoption.ToString().Contains(adoption.AdoptedByHumanId.ToString(), StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    private static ModerationOperationalBudgetTerms Terms() =>
        new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 7, TimeSpan.FromMinutes(10));

    private static ModerationOperationalBudgetSource Source(
        Guid? id = null, Guid? host = null, Guid? trust = null, Guid? tenant = null,
        Guid? operatorHumanId = null, Guid? recorder = null, long generation = 2,
        ModerationOperationalBudgetTerms? terms = null) =>
        ModerationOperationalBudgetSource.Record(id ?? Guid.CreateVersion7(), host ?? Guid.CreateVersion7(),
            trust ?? Guid.CreateVersion7(), tenant ?? Guid.CreateVersion7(), terms ?? Terms(),
            operatorHumanId ?? Guid.NewGuid(), recorder ?? Guid.NewGuid(), generation, Now, Now.AddHours(2));

    private static ModerationOperationalBudgetAdoption Adopt(ModerationOperationalBudgetSource source,
        Guid? human = null, long generation = 3, DateTimeOffset? now = null, DateTimeOffset? until = null) =>
        ModerationOperationalBudgetAdoption.Adopt(Guid.CreateVersion7(), source,
            human ?? source.OperatorHumanId, generation, now ?? Now, until ?? Now.AddHours(1));
}
