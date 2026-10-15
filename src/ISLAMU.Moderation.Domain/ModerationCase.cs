namespace ISLAMU.Moderation.Domain;

public enum ModerationCaseState
{
    Open = 1,
    Closed = 2
}

/// <summary>An allegation occurrence. Opening it creates no finding or restriction.</summary>
public sealed class ModerationCase
{
    private readonly List<ModerationCaseEvidence> _evidence = [];

    private ModerationCase()
    {
    }

    private ModerationCase(Guid id, SubjectReference subject, AuthorityScope scope, Guid occurrenceId, DateTimeOffset now)
    {
        Id = id;
        Subject = subject;
        Scope = scope;
        OccurrenceId = occurrenceId;
        OccurrenceOwnerKind = (int)subject.Kind;
        OccurrenceOwnerId = subject.Id;
        CreatedAt = now.ToUniversalTime();
        UpdatedAt = CreatedAt;
        State = ModerationCaseState.Open;
        Revision = 1;
    }

    public Guid Id { get; private set; }
    public ModerationSubjectKind? SubjectKind { get; private set; }
    public Guid? SubjectId { get; private set; }
    public ModerationScopeKind? SubjectOriginKind { get; private set; }
    public Guid? SubjectOriginTrustDomainId { get; private set; }
    public Guid? SubjectOriginSolutionId { get; private set; }
    public Guid? SubjectOriginInstanceId { get; private set; }
    public Guid? SubjectOriginTenantId { get; private set; }
    public Guid? SubjectOriginCommunityId { get; private set; }
    public Guid? SubjectOriginResourceId { get; private set; }
    public Guid? SubjectOriginEventId { get; private set; }
    public ModerationScopeKind ScopeKind { get; private set; }
    public Guid ScopeTrustDomainId { get; private set; }
    public Guid? ScopeSolutionId { get; private set; }
    public Guid? ScopeInstanceId { get; private set; }
    public Guid? ScopeTenantId { get; private set; }
    public Guid? ScopeCommunityId { get; private set; }
    public Guid? ScopeResourceId { get; private set; }
    public Guid? ScopeEventId { get; private set; }
    public SubjectReference? Subject
    {
        get => SubjectReference.FromOptionalCoordinates(SubjectKind, SubjectId,
            SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
            SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId, SubjectOriginEventId);
        private set
        {
            ArgumentNullException.ThrowIfNull(value);
            SubjectKind = value.Kind;
            SubjectId = value.Id;
            (SubjectOriginKind, SubjectOriginTrustDomainId, SubjectOriginSolutionId, SubjectOriginInstanceId,
                SubjectOriginTenantId, SubjectOriginCommunityId, SubjectOriginResourceId, SubjectOriginEventId) = value.Origin.Coordinates;
        }
    }

    public ModerationCaseTargetKind? TargetKind { get; private set; }
    public Guid? TargetId { get; private set; }
    public Guid? TargetVersion { get; private set; }
    public ModerationScopeKind? TargetOriginKind { get; private set; }
    public Guid? TargetOriginTrustDomainId { get; private set; }
    public Guid? TargetOriginSolutionId { get; private set; }
    public Guid? TargetOriginInstanceId { get; private set; }
    public Guid? TargetOriginTenantId { get; private set; }
    public Guid? TargetOriginCommunityId { get; private set; }
    public Guid? TargetOriginResourceId { get; private set; }
    public Guid? TargetOriginEventId { get; private set; }

    public ModerationCaseTarget? Target
    {
        get
        {
            if (TargetKind is null && TargetId is null && TargetVersion is null && TargetOriginKind is null
                && TargetOriginTrustDomainId is null && TargetOriginSolutionId is null
                && TargetOriginInstanceId is null && TargetOriginTenantId is null
                && TargetOriginCommunityId is null && TargetOriginResourceId is null && TargetOriginEventId is null)
                return null;
            if (TargetKind is not { } kind || TargetId is not { } id || TargetVersion is not { } version
                || TargetOriginKind is not { } originKind || TargetOriginTrustDomainId is not { } trust)
                throw new ArgumentException("A case target must be absent or completely qualified.");
            return ModerationCaseTarget.Create(kind, id, AuthorityScope.FromCoordinates(
                originKind, trust, TargetOriginSolutionId, TargetOriginInstanceId,
                TargetOriginTenantId, TargetOriginCommunityId, TargetOriginResourceId, TargetOriginEventId), version);
        }
        private set
        {
            ArgumentNullException.ThrowIfNull(value);
            (TargetKind, TargetId, TargetVersion) = (value.Kind, value.Id, value.Version);
            (TargetOriginKind, TargetOriginTrustDomainId, TargetOriginSolutionId, TargetOriginInstanceId,
                TargetOriginTenantId, TargetOriginCommunityId, TargetOriginResourceId, TargetOriginEventId) = value.Origin.Coordinates;
        }
    }

    public IReadOnlyList<ModerationCaseEvidence> Evidence => _evidence.AsReadOnly();

    public AuthorityScope Scope
    {
        get => AuthorityScope.FromCoordinates(ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId, ScopeEventId);
        private set => (ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId, ScopeEventId) = value.Coordinates;
    }

    public Guid OccurrenceId { get; private set; }
    public int OccurrenceOwnerKind { get; private set; }
    public Guid OccurrenceOwnerId { get; private set; }
    public Guid? OccurrenceCaseId { get; private set; }
    public long? OccurrenceEvidenceRevision { get; private set; }
    public Guid? AttributionSourceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ModerationCaseState State { get; private set; }
    public long Revision { get; private set; }

    /// <summary>Checks stored structure only; the host must separately establish current authority.</summary>
    public void ValidatePersistedState()
    {
        var subject = Subject;
        var target = Target;
        var scope = Scope;
        if (Id == Guid.Empty || OccurrenceId == Guid.Empty || (subject is null) == (target is null)
            || subject is not null && subject.TrustDomainId != scope.TrustDomainId
            || UpdatedAt < CreatedAt || !Enum.IsDefined(State))
        {
            throw new ArgumentException("The persisted allegation identity, state or audit coordinates are inconsistent.");
        }

        if (subject is not null)
        {
            if (_evidence.Count != 0 || OccurrenceOwnerKind != (int)subject.Kind || OccurrenceOwnerId != subject.Id
                || State == ModerationCaseState.Open && (Revision != 1 || UpdatedAt != CreatedAt)
                || State == ModerationCaseState.Closed && Revision != 2)
                throw new ArgumentException("The attributed case revision or ownership is inconsistent.");
            if (OccurrenceCaseId is not null || OccurrenceEvidenceRevision is not null || AttributionSourceId is not null)
            {
                if (OccurrenceCaseId is null || OccurrenceCaseId == Guid.Empty || OccurrenceCaseId == Id
                    || OccurrenceEvidenceRevision is null or < 1 || AttributionSourceId is null
                    || AttributionSourceId == Guid.Empty || Scope.Kind != ModerationScopeKind.Tenant
                    || subject.Origin != Scope && subject.Origin != Scope.Parent)
                    throw new ArgumentException("An attributed case requires its immutable qualified occurrence link.");
            }
            return;
        }

        if (OccurrenceOwnerKind != 0 || OccurrenceOwnerId != Guid.Empty
            || OccurrenceCaseId is not null || OccurrenceEvidenceRevision is not null || AttributionSourceId is not null
            || _evidence.Count == 0 || CreatedAt == default
            || Revision != _evidence.Count + (State == ModerationCaseState.Closed ? 1L : 0L))
            throw new ArgumentException("Target ownership requires its complete retained evidence history.");
        var references = new HashSet<ModerationEvidenceReference>();
        var previousTime = CreatedAt;
        long previousRevision = 0;
        var closureGap = false;
        var ordered = _evidence.OrderBy(row => row.CaseRevision).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var row = ordered[index];
            var reference = row.ToReference(target!, OccurrenceId);
            RequireEvidenceScope(scope, reference);
            // Closure consumes one aggregate revision but does not end confidential intake.
            // Its one gap may precede later evidence or follow the final evidence row.
            if (State == ModerationCaseState.Closed && index > 0 && !closureGap
                && row.CaseRevision == previousRevision + 2)
                closureGap = true;
            else if (row.CaseRevision != previousRevision + 1)
                throw new ArgumentException("Case evidence cannot skip an unrecorded transition.");
            if (row.CaseId != Id || row.CaseRevision > Revision
                || row.RecordedAt < previousTime || row.RecordedAt > UpdatedAt
                || index == 0 && row.RecordedAt != CreatedAt || !references.Add(reference))
                throw new ArgumentException("Case evidence must retain distinct, ordered native provenance.");
            previousTime = row.RecordedAt;
            previousRevision = row.CaseRevision;
        }
        if ((State == ModerationCaseState.Open || closureGap) && UpdatedAt != previousTime)
            throw new ArgumentException("The latest evidence revision must match its retained time.");
    }

    /// <summary>
    /// Opens a target-owned allegation after the native writer qualifies its source and caller
    /// relationship. No subject is inferred, including for actor, service or participant targets.
    /// There is deliberately no attribution transition without a qualified native writer.
    /// </summary>
    public static ModerationCase OpenAllegation(
        Guid id, AuthorityScope scope, ModerationEvidenceReference evidence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(evidence);
        if (id == Guid.Empty || now == default)
            throw new ArgumentException("An allegation requires a native case identifier and creation time.");
        RequireEvidenceScope(scope, evidence);
        var allegation = new ModerationCase
        {
            Id = id, Scope = scope, Target = evidence.Target, OccurrenceId = evidence.OccurrenceId,
            CreatedAt = now.ToUniversalTime(), UpdatedAt = now.ToUniversalTime(),
            State = ModerationCaseState.Open, Revision = 1
        };
        allegation._evidence.Add(ModerationCaseEvidence.Capture(id, 1, evidence, now));
        return allegation;
    }

    /// <summary>
    /// Retains new material provenance without reopening a closed case or changing adjudication.
    /// An exact current retry changes neither history nor revision.
    /// </summary>
    public void RecordEvidence(ModerationEvidenceReference evidence, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ValidatePersistedState();
        if (Target is not { } target
            || Revision != expectedRevision || now < UpdatedAt)
            throw new InvalidOperationException("Evidence requires the exact current target-owned case.");
        if (evidence.Target != target || evidence.OccurrenceId != OccurrenceId)
            throw new ArgumentException("Evidence cannot rewrite the allegation target or occurrence.", nameof(evidence));
        RequireEvidenceScope(Scope, evidence);
        if (_evidence.Any(row => row.ToReference(target, OccurrenceId) == evidence))
            return;
        var nextRevision = checked(Revision + 1);
        _evidence.Add(ModerationCaseEvidence.Capture(Id, nextRevision, evidence, now));
        Revision = nextRevision;
        UpdatedAt = now.ToUniversalTime();
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

    /// <summary>
    /// Creates a separate adjudication owner, never a finding. The native writer must qualify
    /// the attribution source and recheck it through commit; this factory checks structure only.
    /// Later intake, closure and another subject's adjudication cannot rewrite this link.
    /// </summary>
    public static ModerationCase OpenAttributed(
        Guid id, SubjectReference subject, ModerationCase occurrence, long evidenceRevision,
        Guid attributionSourceId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(occurrence);
        occurrence.ValidatePersistedState();
        if (occurrence.Target is null || occurrence.Subject is not null || id == occurrence.Id
            || attributionSourceId == Guid.Empty || now < occurrence.CreatedAt
            || subject.Origin != occurrence.Scope && subject.Origin != occurrence.Scope.Parent
            || !occurrence.Evidence.Any(row => row.CaseRevision == evidenceRevision && row.RecordedAt <= now))
            throw new ArgumentException("Attribution requires one exact retained occurrence evidence revision.");
        var created = Open(id, subject, occurrence.Scope, occurrence.OccurrenceId, now);
        created.OccurrenceCaseId = occurrence.Id;
        created.OccurrenceEvidenceRevision = evidenceRevision;
        created.AttributionSourceId = attributionSourceId;
        created.ValidatePersistedState();
        return created;
    }

    public void Close(long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || State != ModerationCaseState.Open || now < UpdatedAt)
        {
            throw new InvalidOperationException("The case is not current and open.");
        }

        var nextRevision = checked(Revision + 1);
        State = ModerationCaseState.Closed;
        UpdatedAt = now.ToUniversalTime();
        Revision = nextRevision;
    }

    private static void RequireEvidenceScope(AuthorityScope scope, ModerationEvidenceReference evidence)
    {
        if (scope.Kind != ModerationScopeKind.Tenant
            || evidence.Target.Origin != scope && evidence.Target.Origin != scope.Parent
            || evidence.SourceOrigin != scope && evidence.SourceOrigin != scope.Parent)
            throw new ArgumentException("Protected intake must retain its exact tenant and native source ancestry.");
    }
}

/// <summary>Immutable, case-owned provenance. It has no independent aggregate lifecycle or disclosure permission.</summary>
public sealed class ModerationCaseEvidence
{
    private ModerationCaseEvidence() { }

    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid SourceVersion { get; private set; }
    public Guid MaterialFactId { get; private set; }
    public ModerationScopeKind SourceOriginKind { get; private set; }
    public Guid SourceOriginTrustDomainId { get; private set; }
    public Guid? SourceOriginSolutionId { get; private set; }
    public Guid? SourceOriginInstanceId { get; private set; }
    public Guid? SourceOriginTenantId { get; private set; }
    public Guid? SourceOriginCommunityId { get; private set; }
    public Guid? SourceOriginResourceId { get; private set; }
    public Guid? SourceOriginEventId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public ModerationEvidenceReference ToReference(ModerationCaseTarget target, Guid occurrenceId) =>
        ModerationEvidenceReference.Create(target, AuthorityScope.FromCoordinates(
            SourceOriginKind, SourceOriginTrustDomainId, SourceOriginSolutionId, SourceOriginInstanceId,
            SourceOriginTenantId, SourceOriginCommunityId, SourceOriginResourceId, SourceOriginEventId),
            occurrenceId, SourceId, SourceVersion, MaterialFactId);

    internal static ModerationCaseEvidence Capture(
        Guid caseId, long caseRevision, ModerationEvidenceReference reference, DateTimeOffset now)
    {
        var evidence = new ModerationCaseEvidence
        {
            CaseId = caseId, CaseRevision = caseRevision, SourceId = reference.SourceId,
            SourceVersion = reference.SourceVersion, MaterialFactId = reference.MaterialFactId,
            RecordedAt = now.ToUniversalTime()
        };
        (evidence.SourceOriginKind, evidence.SourceOriginTrustDomainId,
            evidence.SourceOriginSolutionId, evidence.SourceOriginInstanceId,
            evidence.SourceOriginTenantId, evidence.SourceOriginCommunityId,
            evidence.SourceOriginResourceId, evidence.SourceOriginEventId) = reference.SourceOrigin.Coordinates;
        return evidence;
    }

    public override string ToString() => nameof(ModerationCaseEvidence);
}
