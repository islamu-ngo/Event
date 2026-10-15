using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationOperationalBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly AuthorityScope Instance = AuthorityScope.Instance(
        AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
        Guid.CreateVersion7());
    private static readonly SubjectReference Human = SubjectReference.Create(
        ModerationSubjectKind.Human, Guid.CreateVersion7(), Instance);

    [Test]
    public async Task Distinct_occurrences_exhaust_capacity_but_an_exact_retry_does_not_charge_again()
    {
        var first = Guid.CreateVersion7();
        var budget = Budget();
        var history = new[] { Occurrence(first), Occurrence(first), Occurrence(Guid.CreateVersion7()) };
        await Assert.That(budget.Assess(Guid.CreateVersion7(), Now, history).State)
            .IsEqualTo(ModerationOperationalBudgetState.Exhausted);
        var retry = budget.Assess(first, Now, history);
        await Assert.That(retry.State).IsEqualTo(ModerationOperationalBudgetState.Duplicate);
        await Assert.That(retry.DistinctOccurrences).IsEqualTo(2);
    }

    [Test]
    public async Task Original_occurrence_time_survives_retry_and_changed_limit()
    {
        var first = Guid.CreateVersion7();
        var history = new[] { Occurrence(first) with { RecordedAt = Now.AddHours(-2) } };
        await Assert.That(Budget().Assess(first, Now, history).State)
            .IsEqualTo(ModerationOperationalBudgetState.Duplicate);
        var recent = new[] { Occurrence(first), Occurrence(Guid.CreateVersion7()) };
        await Assert.That(Budget(1).Assess(Guid.CreateVersion7(), Now, recent).State)
            .IsEqualTo(ModerationOperationalBudgetState.Exhausted);
        await Assert.That(Budget(3).Assess(Guid.CreateVersion7(), Now, recent).DistinctOccurrences).IsEqualTo(2);
    }

    [Test]
    public async Task Rolling_window_counts_the_boundary_and_excludes_only_older_occurrences()
    {
        var budget = Budget();
        var history = new[]
        {
            Occurrence(Guid.CreateVersion7()) with { RecordedAt = Now.AddHours(-1) },
            Occurrence(Guid.CreateVersion7()) with { RecordedAt = Now.AddHours(-1).AddTicks(-1) }
        };
        var result = budget.Assess(Guid.CreateVersion7(), Now, history);
        await Assert.That(result.State).IsEqualTo(ModerationOperationalBudgetState.Available);
        await Assert.That(result.DistinctOccurrences).IsEqualTo(1);
    }

    [Test]
    [Arguments("subject")]
    [Arguments("origin")]
    [Arguments("scope")]
    [Arguments("mandate")]
    [Arguments("action")]
    public async Task Another_budget_dimension_cannot_spend_this_subjects_capacity(string change)
    {
        var otherInstance = AuthorityScope.Instance(Instance.Parent!, Guid.CreateVersion7());
        var occurrence = Occurrence(Guid.CreateVersion7());
        occurrence = change switch
        {
            "subject" => occurrence with { Subject = SubjectReference.Create(
                ModerationSubjectKind.Human, Guid.CreateVersion7(), Instance) },
            "origin" => occurrence with { Subject = SubjectReference.Create(
                ModerationSubjectKind.Human, Human.Id, otherInstance) },
            "scope" => occurrence with { Scope = AuthorityScope.Tenant(Instance, Guid.CreateVersion7()) },
            "mandate" => occurrence with { MandateId = Guid.CreateVersion7() },
            _ => occurrence with { Action = ModerationOperationalAction.PublishEvent }
        };
        var result = Budget(1).Assess(Guid.CreateVersion7(), Now, [occurrence]);
        await Assert.That(result.State).IsEqualTo(ModerationOperationalBudgetState.Available);
        await Assert.That(result.DistinctOccurrences).IsEqualTo(0);
    }

    [Test]
    public async Task Service_accounting_requires_an_explicit_mandate_and_does_not_use_a_key_identity()
    {
        var service = SubjectReference.Create(ModerationSubjectKind.Service, Guid.CreateVersion7(), Instance);
        await Assert.That(() => new ModerationOperationalBudget(service, Instance, null,
            ModerationOperationalAction.ServiceEffect, 2, TimeSpan.FromHours(1))).Throws<ArgumentException>();
        var mandate = Guid.CreateVersion7();
        var budget = new ModerationOperationalBudget(service, Instance, mandate,
            ModerationOperationalAction.ServiceEffect, 2, TimeSpan.FromHours(1));
        var history = new[] { new ModerationOperationalOccurrence(
            Guid.CreateVersion7(), service, Instance, mandate, ModerationOperationalAction.ServiceEffect, Now) };
        await Assert.That(budget.Assess(Guid.CreateVersion7(), Now, history).DistinctOccurrences).IsEqualTo(1);
    }

    [Test]
    [Arguments("future")]
    [Arguments("offset")]
    [Arguments("duplicate-time")]
    public async Task Unreliable_occurrence_time_cannot_certify_remaining_capacity(string change)
    {
        var first = Occurrence(Guid.CreateVersion7());
        var second = first with { RecordedAt = change switch
        {
            "future" => Now.AddTicks(1),
            "offset" => Now.ToOffset(TimeSpan.FromHours(1)),
            _ => Now.AddTicks(-1)
        } };
        await Assert.That(() => Budget().Assess(Guid.CreateVersion7(), Now, [first, second]))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Unbounded_or_disabled_limits_are_not_qualified_budget_terms(int limit) =>
        await Assert.That(() => Budget(limit)).Throws<ArgumentException>();

    private static ModerationOperationalBudget Budget(int limit = 2) =>
        new(Human, Instance, null, ModerationOperationalAction.SubmitReport, limit, TimeSpan.FromHours(1));

    private static ModerationOperationalOccurrence Occurrence(Guid id) =>
        new(id, Human, Instance, null, ModerationOperationalAction.SubmitReport, Now);
}
