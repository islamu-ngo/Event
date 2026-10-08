namespace ISLAMU.Moderation.Domain;

/// <summary>Bounded urgent protection with a review deadline, distinct from final discipline.</summary>
public sealed class ModerationProtectiveMeasure
{
    private ModerationProtectiveMeasure()
    {
    }

    private ModerationProtectiveMeasure(
        Guid id, SubjectReference subject, SubjectReference protector, AuthorityScope issuingScope, AuthorityScope scope,
        IEnumerable<ModerationCapability> capabilities, string grounds,
        DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset reviewAt)
    {
        Id = id;
        Subject = subject;
        Protector = protector;
        IssuingScope = issuingScope;
        Scope = scope;
        CapabilityMask = EffectiveRestrictions.CreateCapabilityMask(capabilities);
        Grounds = grounds;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        ReviewAt = reviewAt.ToUniversalTime();
        Revision = 1;
    }

    public Guid Id { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public ModerationScopeKind SubjectOriginKind { get; private set; }
    public Guid SubjectOriginTrustDomainId { get; private set; }
    public Guid? SubjectOriginSolutionId { get; private set; }
    public Guid? SubjectOriginInstanceId { get; private set; }
    public Guid? SubjectOriginTenantId { get; private set; }
    public Guid? SubjectOriginCommunityId { get; private set; }
    public Guid? SubjectOriginResourceId { get; private set; }
    public ModerationSubjectKind ProtectorKind { get; private set; }
    public Guid ProtectorId { get; private set; }
    public ModerationScopeKind ProtectorOriginKind { get; private set; }
    public Guid ProtectorOriginTrustDomainId { get; private set; }
    public Guid? ProtectorOriginSolutionId { get; private set; }
    public Guid? ProtectorOriginInstanceId { get; private set; }
    public Guid? ProtectorOriginTenantId { get; private set; }
    public Guid? ProtectorOriginCommunityId { get; private set; }
    public Guid? ProtectorOriginResourceId { get; private set; }
    public ModerationScopeKind IssuingScopeKind { get; private set; }
    public Guid IssuingScopeTrustDomainId { get; private set; }
    public Guid? IssuingScopeSolutionId { get; private set; }
    public Guid? IssuingScopeInstanceId { get; private set; }
    public Guid? IssuingScopeTenantId { get; private set; }
    public Guid? IssuingScopeCommunityId { get; private set; }
    public Guid? IssuingScopeResourceId { get; private set; }
    public ModerationScopeKind ScopeKind { get; private set; }
    public Guid ScopeTrustDomainId { get; private set; }
    public Guid? ScopeSolutionId { get; private set; }
    public Guid? ScopeInstanceId { get; private set; }
    public Guid? ScopeTenantId { get; private set; }
    public Guid? ScopeCommunityId { get; private set; }
    public Guid? ScopeResourceId { get; private set; }
    public ModerationSubjectKind? ReviewerKind { get; private set; }
    public Guid? ReviewerId { get; private set; }
    public ModerationScopeKind? ReviewerOriginKind { get; private set; }
    public Guid? ReviewerOriginTrustDomainId { get; private set; }
    public Guid? ReviewerOriginSolutionId { get; private set; }
    public Guid? ReviewerOriginInstanceId { get; private set; }
    public Guid? ReviewerOriginTenantId { get; private set; }
    public Guid? ReviewerOriginCommunityId { get; private set; }
    public Guid? ReviewerOriginResourceId { get; private set; }
    public int CapabilityMask { get; private set; }

    public SubjectReference Subject
    {
        get => SubjectReference.Create(SubjectKind, SubjectId, AuthorityScope.FromCoordinates(
            SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
            SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId));
        private set
        {
            SubjectKind = value.Kind;
            SubjectId = value.Id;
            (SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
                SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId) = value.Origin.Coordinates;
        }
    }

    public SubjectReference Protector
    {
        get => SubjectReference.Create(ProtectorKind, ProtectorId, AuthorityScope.FromCoordinates(
            ProtectorOriginKind, ProtectorOriginTrustDomainId, ProtectorOriginSolutionId, ProtectorOriginInstanceId,
            ProtectorOriginTenantId, ProtectorOriginCommunityId, ProtectorOriginResourceId));
        private set
        {
            ProtectorKind = value.Kind;
            ProtectorId = value.Id;
            (ProtectorOriginKind, ProtectorOriginTrustDomainId, ProtectorOriginSolutionId, ProtectorOriginInstanceId,
                ProtectorOriginTenantId, ProtectorOriginCommunityId, ProtectorOriginResourceId) = value.Origin.Coordinates;
        }
    }

    public AuthorityScope IssuingScope
    {
        get => AuthorityScope.FromCoordinates(IssuingScopeKind, IssuingScopeTrustDomainId, IssuingScopeSolutionId,
            IssuingScopeInstanceId, IssuingScopeTenantId, IssuingScopeCommunityId, IssuingScopeResourceId);
        private set => (IssuingScopeKind, IssuingScopeTrustDomainId, IssuingScopeSolutionId, IssuingScopeInstanceId,
            IssuingScopeTenantId, IssuingScopeCommunityId, IssuingScopeResourceId) = value.Coordinates;
    }

    public AuthorityScope Scope
    {
        get => AuthorityScope.FromCoordinates(ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId);
        private set => (ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId) = value.Coordinates;
    }

    public IReadOnlySet<ModerationCapability> Capabilities => EffectiveRestrictions.FromCapabilityMask(CapabilityMask);
    public string Grounds { get; private set; } = string.Empty;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public DateTimeOffset ReviewAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public SubjectReference? Reviewer => SubjectReference.FromOptionalCoordinates(
        ReviewerKind, ReviewerId, ReviewerOriginKind, ReviewerOriginTrustDomainId, ReviewerOriginSolutionId,
        ReviewerOriginInstanceId, ReviewerOriginTenantId, ReviewerOriginCommunityId, ReviewerOriginResourceId);
    public DateTimeOffset? RevokedAt { get; private set; }
    public long Revision { get; private set; }

    /// <summary>Checks stored protection and review shape, not historical qualification or current authority.</summary>
    public void ValidatePersistedState()
    {
        var subject = Subject;
        var protector = Protector;
        var scope = Scope;
        var reviewer = Reviewer;
        _ = Capabilities;
        if (Id == Guid.Empty || subject.TrustDomainId != scope.TrustDomainId
            || protector.TrustDomainId != scope.TrustDomainId || !IssuingScope.Contains(scope)
            || string.IsNullOrWhiteSpace(Grounds) || Grounds.Length > 100
            || EndsAt <= StartsAt || ReviewAt <= StartsAt || ReviewAt > EndsAt
            || (ReviewedAt is null) != (reviewer is null)
            || (ReviewedAt is not null && (ReviewedAt < StartsAt || ReviewedAt >= EndsAt))
            || (reviewer is not null && (reviewer.Kind != ModerationSubjectKind.Human
                || reviewer.TrustDomainId != scope.TrustDomainId || reviewer.Id == protector.Id || reviewer.Id == subject.Id))
            || (RevokedAt is not null && RevokedAt < StartsAt)
            || Revision != 1 + (ReviewedAt is null ? 0 : 1) + (RevokedAt is null ? 0 : 1))
        {
            throw new ArgumentException("The persisted protection interval, review or revision is inconsistent.");
        }
    }

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
            id, subject, protector, issuingScope, scope, capabilities, grounds, startsAt, endsAt, reviewAt);
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
        ReviewerKind = reviewer.Kind;
        ReviewerId = reviewer.Id;
        (ReviewerOriginKind, ReviewerOriginTrustDomainId, ReviewerOriginSolutionId, ReviewerOriginInstanceId,
            ReviewerOriginTenantId, ReviewerOriginCommunityId, ReviewerOriginResourceId) = reviewer.Origin.Coordinates;
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
