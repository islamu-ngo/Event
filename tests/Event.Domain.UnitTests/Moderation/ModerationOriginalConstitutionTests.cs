namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationOriginalConstitutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task OriginalSource_RetainsExactPurposeAndIndependentFiniteAdoption()
    {
        var effect = Guid.CreateVersion7();
        var source = Source(effect, new string('a', 64), new string('b', 64), Now.AddMinutes(6));
        var adoption = ModerationConstitutionAdoption.Adopt(Guid.CreateVersion7(), source,
            Guid.CreateVersion7(), 5, Now, Now.AddMinutes(4));
        await Assert.That(source.OriginalEffectId).IsEqualTo(effect);
        await Assert.That(source.OriginalSnapshotHash).IsEqualTo(new string('a', 64));
        await Assert.That(source.OriginalEffectBindingHash).IsEqualTo(new string('b', 64));
        await Assert.That(source.OriginalCustodyUntil).IsEqualTo(Now.AddMinutes(6));
        await Assert.That(adoption.MatchesCurrent(source, Now)).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, Now.AddMinutes(4))).IsFalse();
        await Assert.That(adoption.MatchesCurrent(Source(Guid.CreateVersion7(),
            new string('a', 64), new string('b', 64), Now.AddMinutes(6)), Now)).IsFalse();
        await Assert.That(() => ModerationConstitutionAdoption.Adopt(Guid.CreateVersion7(), source,
            Guid.CreateVersion7(), 5, Now, source.ValidUntil.AddTicks(1))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task OriginalSource_RejectsIncompletePurposeOrCustodyExtension(int change)
    {
        await Assert.That(() => Source(change == 0 ? null : change == 4 ? Guid.Empty : Guid.CreateVersion7(),
            change == 1 ? null : change == 5 ? "not-a-digest" : new string('a', 64),
            change == 2 ? null : new string('b', 64),
            change == 3 ? null : change == 6 ? Now.AddMinutes(5).AddTicks(-1) : Now.AddMinutes(6)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task OrdinarySource_HasNoOriginalPurposeAndDoesNotAcquireOneThroughAdoption()
    {
        var source = Source(null, null, null, null);
        await Assert.That(source.OriginalEffectId).IsNull();
        await Assert.That(source.OriginalCustodyUntil).IsNull();
        var adoption = ModerationConstitutionAdoption.Adopt(Guid.CreateVersion7(), source,
            Guid.CreateVersion7(), 5, Now, Now.AddMinutes(4));
        await Assert.That(adoption.SourceId).IsEqualTo(source.Id);
        await Assert.That(source.OriginalEffectId).IsNull();
    }

    private static ModerationConstitutionSource Source(Guid? effect, string? snapshot,
        string? binding, DateTimeOffset? custody) =>
        ModerationConstitutionSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            new string('c', 64), ModerationConstitutionTests.Terms(),
            [new(Guid.CreateVersion7(), 1, 2, Guid.CreateVersion7(), Guid.CreateVersion7(),
                "Verified native canonical controller.",
                "Qualified substantive governance reviewer.",
                "Independent from implicated controllers.")],
            Guid.CreateVersion7(), 4, Now, Now.AddMinutes(5), effect, snapshot, binding, custody);
}
