namespace ISLAMU.Moderation.Domain;

/// <summary>Bounded urgent protection with a review deadline, distinct from final discipline.</summary>
public sealed class ModerationProtectiveMeasure
{
    private ModerationProtectiveMeasure(
        Guid id, SubjectReference subject, SubjectReference protector, AuthorityScope scope,
        IEnumerable<ModerationCapability> capabilities, string grounds,
        DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset reviewAt)
    {
        Id = id;
        Subject = subject;
        Protector = protector;
        Scope = scope;
        Capabilities = EffectiveRestrictions.CreateCapabilitySet(capabilities);
        Grounds = grounds;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        ReviewAt = reviewAt.ToUniversalTime();
    }

    public Guid Id { get; }
    public SubjectReference Subject { get; }
    public SubjectReference Protector { get; }
    public AuthorityScope Scope { get; }
    public IReadOnlySet<ModerationCapability> Capabilities { get; }
    public string Grounds { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset EndsAt { get; }
    public DateTimeOffset ReviewAt { get; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public SubjectReference? Reviewer { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public long Revision { get; private set; } = 1;

    public static ModerationProtectiveMeasure Create(
        Guid id, SubjectReference subject, SubjectReference protector, AuthorityScope issuingScope,
        AuthorityScope scope, IEnumerable<ModerationCapability> capabilities, string grounds,
        DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset reviewAt)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(issuingScope);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(grounds);
        if (id == Guid.Empty || !issuingScope.Contains(scope)
            || subject.TrustDomainId != scope.TrustDomainId
            || protector.TrustDomainId != scope.TrustDomainId)
        {
            throw new ArgumentException("Protection requires qualified subject and issuer jurisdiction.", nameof(scope));
        }

        if (endsAt <= startsAt || reviewAt <= startsAt || reviewAt > endsAt || grounds.Length > 100)
        {
            throw new ArgumentException("Protection requires bounded grounds, interval and review deadline.", nameof(endsAt));
        }

        return new ModerationProtectiveMeasure(
            id, subject, protector, scope, capabilities, grounds, startsAt, endsAt, reviewAt);
    }

    public bool IsEffectiveAt(DateTimeOffset now) =>
        RevokedAt is null && now >= StartsAt && now < EndsAt;

    public bool IsReviewOverdueAt(DateTimeOffset now) =>
        ReviewedAt is null && now >= ReviewAt && IsEffectiveAt(now);

    public void RecordIndependentReview(
        SubjectReference reviewer, AuthorityScope qualification, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reviewer);
        ArgumentNullException.ThrowIfNull(qualification);
        if (expectedRevision != Revision || ReviewedAt is not null || !IsEffectiveAt(now)
            || reviewer.Kind != ModerationSubjectKind.Human
            || reviewer.TrustDomainId != Scope.TrustDomainId
            || !qualification.Contains(Scope)
            || reviewer.Id == Protector.Id || reviewer.Id == Subject.Id)
        {
            throw new InvalidOperationException("Protection requires a current independent qualified reviewer.");
        }

        var nextRevision = checked(Revision + 1);
        Reviewer = reviewer;
        ReviewedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    public void Revoke(long expectedRevision, DateTimeOffset now)
    {
        if (expectedRevision != Revision || RevokedAt is not null || now < StartsAt)
        {
            throw new InvalidOperationException("The protective contribution is not current.");
        }

        var nextRevision = checked(Revision + 1);
        RevokedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }
}
