using Explore.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationQualificationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Host = Guid.CreateVersion7();
    private static readonly Guid Trust = Guid.CreateVersion7();
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Case = Guid.CreateVersion7();
    private static readonly Guid Human = Guid.NewGuid();

    [Test]
    public async Task Adoption_retains_exact_limits_and_does_not_require_a_second_human()
    {
        var source = Publish();
        var adoption = ModerationQualificationPolicyAdoption.Adopt(
            Guid.CreateVersion7(), source, Human, Now, Now.AddHours(2));
        await Assert.That(adoption.SourceId).IsEqualTo(source.Id);
        await Assert.That(adoption.SourceRevision).IsEqualTo(1L);
        await Assert.That(adoption.WriterGeneration).IsEqualTo(3L);
        await Assert.That(adoption.MaximumReviewerLifetime).IsEqualTo(TimeSpan.FromMinutes(30));
        await Assert.That(adoption.MaximumUrgencyLifetime).IsEqualTo(TimeSpan.FromMinutes(10));
        await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(source.PublishedByHumanId);
        await Assert.That(adoption.MatchesCurrent(source, 1, Now)).IsTrue();
    }

    [Test]
    public async Task Replacing_source_invalidates_retained_adoption_without_rewriting_approval()
    {
        var source = Publish();
        var adoption = ModerationQualificationPolicyAdoption.Adopt(
            Guid.CreateVersion7(), source, Human, Now, Now.AddHours(2));
        source.Replace(1, 1, 5, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(5),
            Human, Now.AddMinutes(1), Now.AddHours(3));
        await Assert.That(adoption.MatchesCurrent(source, 1, Now.AddMinutes(1))).IsFalse();
        await Assert.That(adoption.SourceRevision).IsEqualTo(1L);
        await Assert.That(adoption.MaximumReviewerLifetime).IsEqualTo(TimeSpan.FromMinutes(30));
        await Assert.That(source.Revision).IsEqualTo(2L);
        await Assert.That(source.WriterGeneration).IsEqualTo(5L);
    }

    [Test]
    public async Task Adopted_validity_is_half_open_and_bound_to_exact_case_revision()
    {
        var source = Publish();
        var adoption = ModerationQualificationPolicyAdoption.Adopt(
            Guid.CreateVersion7(), source, Human, Now, Now.AddHours(2));
        await Assert.That(adoption.MatchesCurrent(source, 1, Now.AddTicks(-1))).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, 2, Now)).IsFalse();
        await Assert.That(adoption.MatchesCurrent(source, 1, Now.AddHours(2).AddTicks(-1))).IsTrue();
        await Assert.That(adoption.MatchesCurrent(source, 1, Now.AddHours(2))).IsFalse();
        await Assert.That(() => ModerationQualificationPolicyAdoption.Adopt(
            Guid.CreateVersion7(), source, Human, Now.AddHours(3), Now.AddHours(4))).Throws<ArgumentException>();
    }

    [Test]
    public async Task Revocation_preserves_the_approved_generation_and_is_not_renewal()
    {
        var source = Publish();
        var adoption = ModerationQualificationPolicyAdoption.Adopt(
            Guid.CreateVersion7(), source, Human, Now, Now.AddHours(2));
        adoption.Revoke(1, Human, Now.AddMinutes(1));
        await Assert.That(adoption.MatchesCurrent(source, 1, Now.AddMinutes(1))).IsFalse();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.SourceRevision).IsEqualTo(1L);
        await Assert.That(adoption.ValidUntil).IsEqualTo(Now.AddHours(2));
        await Assert.That(() => adoption.Revoke(2, Human, Now.AddMinutes(2))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(long.MaxValue)]
    public async Task No_implicit_or_unbounded_lifetime_can_be_published(long ticks)
    {
        await Assert.That(() => Publish(TimeSpan.FromTicks(ticks))).Throws<ArgumentException>();
    }

    [Test]
    public async Task Source_replacement_requires_expected_revision_and_new_writer_generation()
    {
        var source = Publish();
        await Assert.That(() => source.Replace(2, 1, 4, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1),
            Human, Now, Now.AddHours(1))).Throws<InvalidOperationException>();
        await Assert.That(() => source.Replace(1, 1, 3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1),
            Human, Now, Now.AddHours(1))).Throws<ArgumentException>();
        await Assert.That(source.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Adoption_cannot_outlive_source_or_share_its_identity()
    {
        var source = Publish();
        await Assert.That(() => ModerationQualificationPolicyAdoption.Adopt(
            source.Id, source, Human, Now, Now.AddHours(1))).Throws<ArgumentException>();
        await Assert.That(() => ModerationQualificationPolicyAdoption.Adopt(
            Guid.CreateVersion7(), source, Human, Now, Now.AddHours(4))).Throws<ArgumentException>();
        await Assert.That(() => Publish(validUntil: DateTimeOffset.MaxValue)).Throws<ArgumentException>();
    }

    private static ModerationQualificationPolicySource Publish(
        TimeSpan? reviewer = null, DateTimeOffset? validUntil = null) =>
        ModerationQualificationPolicySource.Publish(Guid.CreateVersion7(), Host, Trust, Tenant, Case, 1, 3,
            reviewer ?? TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), Human, Now,
            validUntil ?? Now.AddHours(3));
}
