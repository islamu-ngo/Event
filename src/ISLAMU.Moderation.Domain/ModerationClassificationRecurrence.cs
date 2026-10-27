using ISLAMU.Moderation.Domain.Policies;

namespace ISLAMU.Moderation.Domain;

/// <summary>
/// Historical acceptance coordinates for one classification violation rule, not proof of acceptance or misconduct.
/// The native writer must qualify the actual accepted policy, lawful purpose and canonical subject under its fences.
/// </summary>
public sealed record ModerationClassificationPolicyAcceptance
{
    private ModerationClassificationPolicyAcceptance(Guid id, SubjectReference subject, AuthorityScope scope,
        ModerationRuleReference rule, DateTimeOffset acceptedAt)
    {
        Id = id;
        Subject = subject;
        Scope = scope;
        Rule = rule;
        AcceptedAt = acceptedAt.ToUniversalTime();
    }

    public Guid Id { get; }
    public SubjectReference Subject { get; }
    public AuthorityScope Scope { get; }
    public ModerationRuleReference Rule { get; }
    public DateTimeOffset AcceptedAt { get; }

    public static ModerationClassificationPolicyAcceptance Record(Guid id, SubjectReference subject,
        AuthorityScope scope, ModerationRuleReference rule, DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(rule);
        if (id == Guid.Empty || acceptedAt == default || subject.TrustDomainId != scope.TrustDomainId)
            throw new ArgumentException("Acceptance requires a subject, exact policy rule, jurisdiction and historical time.");
        return new(id, subject, scope, rule, acceptedAt);
    }

    public override string ToString() => nameof(ModerationClassificationPolicyAcceptance);
}

/// <summary>
/// An owned snapshot of actual finding ancestry and separately supplied historical acceptance.
/// Original conduct time, classification grounds and complete current history require native qualification;
/// reports, audience ineligibility and honest correction alone cannot supply a finding.
/// </summary>
public sealed record ModerationClassificationFinding
{
    private ModerationClassificationFinding(ModerationFinding finding, DateTimeOffset occurredAt,
        ModerationClassificationPolicyAcceptance? acceptance)
    {
        FindingId = finding.Id;
        FindingRevision = finding.Revision;
        OccurrenceId = finding.CaseOccurrenceId;
        AttributionSourceId = finding.AttributionSourceId;
        AttributionGeneration = finding.AttributionGeneration;
        Subject = finding.Subject;
        Scope = finding.Scope;
        Rule = finding.Rule;
        State = finding.State;
        FinalizedAt = finding.FinalizedAt;
        OccurredAt = occurredAt.ToUniversalTime();
        Acceptance = acceptance;
    }

    public Guid FindingId { get; }
    public long FindingRevision { get; }
    public Guid OccurrenceId { get; }
    public Guid? AttributionSourceId { get; }
    public long AttributionGeneration { get; }
    public SubjectReference Subject { get; }
    public AuthorityScope Scope { get; }
    public ModerationRuleReference Rule { get; }
    public ModerationFindingState State { get; }
    public DateTimeOffset FinalizedAt { get; }
    public DateTimeOffset OccurredAt { get; }
    public ModerationClassificationPolicyAcceptance? Acceptance { get; }

    public static ModerationClassificationFinding Capture(ModerationFinding finding, DateTimeOffset occurredAt,
        ModerationClassificationPolicyAcceptance? acceptance)
    {
        ArgumentNullException.ThrowIfNull(finding);
        finding.ValidatePersistedState();
        if (occurredAt == default || occurredAt > finding.CaseCreatedAt)
            throw new ArgumentException("Original conduct cannot occur after its case was opened.", nameof(occurredAt));
        return new(finding, occurredAt, acceptance);
    }

    public override string ToString() => nameof(ModerationClassificationFinding);
}

public sealed record ModerationClassificationContribution(Guid FindingId, long FindingRevision, Guid OccurrenceId,
    Guid? AttributionSourceId, long AttributionGeneration, Guid PolicyAcceptanceId,
    ModerationRuleReference Rule, DateTimeOffset OccurredAt)
{
    public override string ToString() => nameof(ModerationClassificationContribution);
}

/// <summary>A reference-stage calculation with immutable causes, never a sanction, interval or activation receipt.</summary>
public sealed class ModerationClassificationAssessment
{
    internal ModerationClassificationAssessment(SubjectReference subject, AuthorityScope scope,
        DateTimeOffset assessedAt, DateTimeOffset windowStartExclusive, ModerationClassificationContribution[] contributions)
    {
        Subject = subject;
        Scope = scope;
        AssessedAt = assessedAt;
        WindowStartExclusive = windowStartExclusive;
        Contributions = Array.AsReadOnly(contributions);
        QualifyingOccurrenceCount = contributions.Select(cause => cause.OccurrenceId).Distinct().Count();
        ReferenceProfileId = QualifyingOccurrenceCount switch
        {
            0 => null,
            1 => ModerationReferenceProfileId.CFG30,
            2 => ModerationReferenceProfileId.CFG31,
            3 => ModerationReferenceProfileId.CFG32,
            _ => ModerationReferenceProfileId.CFG33
        };
    }

    public SubjectReference Subject { get; }
    public AuthorityScope Scope { get; }
    public DateTimeOffset AssessedAt { get; }
    public DateTimeOffset WindowStartExclusive { get; }
    public int QualifyingOccurrenceCount { get; }
    public ModerationReferenceProfileId? ReferenceProfileId { get; }
    public IReadOnlyList<ModerationClassificationContribution> Contributions { get; }
    public override string ToString() => nameof(ModerationClassificationAssessment);
}

/// <summary>
/// Calculates ordinary reference recurrence from distinct original occurrences in (now - 180 days, now].
/// A fenced writer must supply complete current findings and real historical acceptance before using a result.
/// Persistent-evasion short-circuiting requires its own qualified producer and is deliberately not inferred here.
/// </summary>
public static class ModerationClassificationRecurrence
{
    public static ModerationClassificationAssessment Assess(SubjectReference subject, AuthorityScope scope,
        IEnumerable<ModerationClassificationFinding> history, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(history);
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG30);
        var window = reference.RecurrenceWindow!.Value;
        if (subject.TrustDomainId != scope.TrustDomainId || !reference.ScopeKinds.Contains(scope.Kind)
            || !reference.SubjectKinds.Contains(subject.Kind) || now == default || now < DateTimeOffset.MinValue.Add(window))
            throw new ArgumentException("Assessment requires a supported canonical subject, exact local jurisdiction and current time.");

        now = now.ToUniversalTime();
        var start = now.Subtract(window);
        var entries = history.Select(entry => entry ?? throw new ArgumentException("History cannot contain absent findings.", nameof(history)))
            .Where(entry => entry.Subject == subject && entry.Scope == scope).Distinct().ToArray();
        if (entries.GroupBy(entry => entry.FindingId).Any(group => group.Count() != 1)
            || entries.GroupBy(entry => entry.OccurrenceId).Any(group => group.Select(entry => entry.OccurredAt).Distinct().Count() != 1)
            || entries.Where(entry => entry.Acceptance is not null).Select(entry => entry.Acceptance!)
                .GroupBy(acceptance => acceptance.Id).Any(group => group.Distinct().Count() != 1))
            throw new ArgumentException("History contains conflicting finding, occurrence or acceptance snapshots.", nameof(history));

        var contributions = entries.Where(entry => entry.State == ModerationFindingState.Final
                && entry.FinalizedAt <= now && entry.OccurredAt > start && entry.OccurredAt <= now
                && entry.Acceptance is { } acceptance && acceptance.Subject == subject && acceptance.Scope == scope
                && acceptance.Rule == entry.Rule && acceptance.AcceptedAt <= entry.OccurredAt)
            .OrderBy(entry => entry.OccurredAt).ThenBy(entry => entry.OccurrenceId).ThenBy(entry => entry.FindingId)
            .Select(entry => new ModerationClassificationContribution(entry.FindingId, entry.FindingRevision,
                entry.OccurrenceId, entry.AttributionSourceId, entry.AttributionGeneration,
                entry.Acceptance!.Id, entry.Rule, entry.OccurredAt)).ToArray();
        return new(subject, scope, now, start, contributions);
    }
}
