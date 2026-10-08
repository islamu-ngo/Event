namespace ISLAMU.Moderation.Domain;

public enum ModerationCaseState
{
    Open = 1,
    Closed = 2
}

/// <summary>An allegation occurrence. Opening it creates no finding or restriction.</summary>
public sealed class ModerationCase
{
    private ModerationCase()
    {
    }

    private ModerationCase(Guid id, SubjectReference subject, AuthorityScope scope, Guid occurrenceId, DateTimeOffset now)
    {
        Id = id;
        Subject = subject;
        Scope = scope;
        OccurrenceId = occurrenceId;
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
        State = ModerationCaseState.Open;
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
    public ModerationScopeKind ScopeKind { get; private set; }
    public Guid ScopeTrustDomainId { get; private set; }
    public Guid? ScopeSolutionId { get; private set; }
    public Guid? ScopeInstanceId { get; private set; }
    public Guid? ScopeTenantId { get; private set; }
    public Guid? ScopeCommunityId { get; private set; }
    public Guid? ScopeResourceId { get; private set; }
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

    public AuthorityScope Scope
    {
        get => AuthorityScope.FromCoordinates(ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId);
        private set => (ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId) = value.Coordinates;
    }

    public Guid OccurrenceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ModerationCaseState State { get; private set; }
    public long Revision { get; private set; }

    /// <summary>Checks stored structure only; the host must separately establish current authority.</summary>
    public void ValidatePersistedState()
    {
        if (Id == Guid.Empty || OccurrenceId == Guid.Empty || Subject.TrustDomainId != Scope.TrustDomainId
            || UpdatedAt < CreatedAt
            || (State == ModerationCaseState.Open && (Revision != 1 || UpdatedAt != CreatedAt))
            || (State == ModerationCaseState.Closed && Revision != 2)
            || !Enum.IsDefined(State))
        {
            throw new ArgumentException("The persisted allegation identity, state or audit coordinates are inconsistent.");
        }
    }

    public static ModerationCase Open(
        Guid id, SubjectReference subject, AuthorityScope scope, Guid occurrenceId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);
        if (id == Guid.Empty || occurrenceId == Guid.Empty || subject.TrustDomainId != scope.TrustDomainId)
        {
            throw new ArgumentException("A qualified allegation and distinct occurrence are required.", nameof(id));
        }

        return new ModerationCase(id, subject, scope, occurrenceId, now);
    }

    public void Close(long expectedRevision, DateTimeOffset now)
    {
        if (expectedRevision != Revision || State != ModerationCaseState.Open || now < UpdatedAt)
        {
            throw new InvalidOperationException("The case is not current and open.");
        }

        var nextRevision = checked(Revision + 1);
        State = ModerationCaseState.Closed;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }
}
