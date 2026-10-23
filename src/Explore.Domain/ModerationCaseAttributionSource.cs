using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// The native relationship being assessed, not a conclusion about guilt. No basis maps
/// publisher ownership, employment, or a participant roster to another person's conduct.
/// </summary>
public enum ModerationResponsibilityBasis
{
    EventSubmission = 1,
    EventContent = 2,
    OrganizationalActor = 3,
    ServiceIdentity = 4,
    ParticipantInvestigation = 5
}

/// <summary>
/// Finite local qualification of one alleged subject and one retained material revision.
/// Withdrawal invalidates qualification, not the immutable link or another subject's finding.
/// Current native grants and responsibility facts must be reacquired before use.
/// Responsibility grounds and reviewer/grant/policy qualification have separate digests so
/// replacing authorization metadata alone cannot become new grounds for live renewal.
/// </summary>
public sealed class ModerationCaseAttributionSource
{
    private ModerationCaseAttributionSource() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public long Generation { get; private set; }
    public Guid OccurrenceCaseId { get; private set; }
    public Guid OccurrenceId { get; private set; }
    public long OccurrenceCaseRevision { get; private set; }
    public long EvidenceCaseRevision { get; private set; }
    public Guid EvidenceSourceId { get; private set; }
    public Guid EvidenceSourceVersion { get; private set; }
    public Guid MaterialFactId { get; private set; }
    public Guid? InvestigationSourceId { get; private set; }
    public long? InvestigationSourceRevision { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid QualificationEpochId { get; private set; }
    public long QualificationRevision { get; private set; }
    public ModerationResponsibilityBasis Basis { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid QualifiedByHumanId { get; private set; }
    public Guid QualifierRoleGrantId { get; private set; }
    public bool QualifierPlatformGrant { get; private set; }
    public Guid ReadGrantId { get; private set; }
    public long ReadGrantRevision { get; private set; }
    public Guid LifetimePolicyId { get; private set; }
    public long LifetimePolicyRevision { get; private set; }
    public string NativeResponsibilityDigest { get; private set; } = string.Empty;
    public string QualificationFactsDigest { get; private set; } = string.Empty;
    public DateTimeOffset QualifiedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public Guid? WithdrawalOperationId { get; private set; }
    public long Revision { get; private set; }

    /// <summary>
    /// Snapshots exact local assessment provenance for Human responsibility under review.
    /// Supplied assessment storage shape is not native authentication or permission; the native
    /// owner must authenticate and reacquire it. EventSubmission requires the actual EventVersion
    /// submitter, independently established by that owner, never a participant, link or organizer.
    /// </summary>
    public static ModerationCaseAttributionSource Qualify(
        Guid id, Guid caseId, long generation, ModerationCase occurrence, long evidenceRevision,
        ModerationCaseTargetSource material, SubjectReference subject, ModerationResponsibilityBasis basis,
        ModerationHostBinding host, ModerationQualificationEpoch epoch,
        ModerationCaseReadGrant readGrant, ModerationCaseTargetPolicy policy,
        Guid qualifierRoleGrantId, bool qualifierPlatformGrant, string responsibilityDigest, string qualificationFactsDigest,
        DateTimeOffset now, DateTimeOffset validUntil,
        ModerationParticipantIncidentResponsibilitySource? investigation = null)
    {
        occurrence.ValidatePersistedState();
        if (basis == ModerationResponsibilityBasis.ParticipantInvestigation)
        {
            if (investigation is null)
                throw new ArgumentException("Participant investigation requires an exact current assessment.", nameof(investigation));
            investigation.ValidatePersistedState();
            if (investigation.Revision != 1 || !investigation.IsCurrentAt(now)
                || subject.Kind != ModerationSubjectKind.Human
                || investigation.SubjectKind != subject.Kind || investigation.SubjectId != subject.Id
                || investigation.HostBindingId != host.Id || investigation.TrustDomainId != host.TrustDomainId
                || investigation.TenantId != occurrence.Scope.Id
                || occurrence.Scope.Coordinates.SolutionId != host.SolutionId
                || occurrence.Scope.Coordinates.InstanceId != host.InstanceId
                || investigation.OccurrenceCaseId != occurrence.Id || investigation.OccurrenceId != occurrence.OccurrenceId
                || investigation.OccurrenceCaseRevision != occurrence.Revision
                || investigation.EvidenceCaseRevision != evidenceRevision
                || investigation.EvidenceSourceId != material.Id || investigation.EvidenceSourceVersion != material.Version
                || investigation.MaterialFactId != material.MaterialFactId
                || material.Kind != ModerationCaseTargetKind.ParticipantIncident
                || material.NativeKind != ModerationCaseTargetKind.ParticipantIncident
                || investigation.EventId != material.EventId || investigation.EventVersion != material.EventVersion
                || validUntil > investigation.ValidUntil)
                throw new ArgumentException("Participant investigation requires matching current Human, occurrence and material assessment coordinates.");
        }
        else if (investigation is not null
            || basis == ModerationResponsibilityBasis.EventSubmission && material.NativeKind != ModerationCaseTargetKind.EventVersion)
            throw new ArgumentException("Only participant investigation accepts assessment provenance; EventSubmission requires native EventVersion material.");
        var reference = material.Evidence(occurrence.Scope);
        if (occurrence.Scope.Kind != ModerationScopeKind.Tenant
            || occurrence.Scope.Coordinates.SolutionId != host.SolutionId
            || occurrence.Scope.Coordinates.InstanceId != host.InstanceId
            || occurrence.Target is null || occurrence.Subject is not null || occurrence.Id == caseId
            || !occurrence.Evidence.Any(row => row.CaseRevision == evidenceRevision
                && row.ToReference(occurrence.Target, occurrence.OccurrenceId) == reference)
            || readGrant.CaseId != occurrence.Id || readGrant.CaseRevision != occurrence.Revision
            || readGrant.SourceId != material.Id || readGrant.TenantId != occurrence.Scope.Id
            || readGrant.TrustDomainId != occurrence.Scope.TrustDomainId || !readGrant.IsCurrentAt(now)
            || host.TrustDomainId != occurrence.Scope.TrustDomainId || epoch.HostBindingId != host.Id
            || epoch.TenantId != occurrence.Scope.Id || policy.TenantId != occurrence.Scope.Id
            || policy.TrustDomainId != host.TrustDomainId || policy.TargetKind != material.Kind || !policy.Enabled
            || subject.Origin != (subject.Kind == ModerationSubjectKind.Content ? occurrence.Scope : occurrence.Scope.Parent)
            || subject.Kind == ModerationSubjectKind.Human
                && (subject.Id == readGrant.CallerUserId || subject.Id == readGrant.IssuerUserId)
            || now < material.RecordedAt || validUntil > readGrant.ExpiresAt
            || validUntil - now > policy.ReadLifetime)
            throw new ArgumentException("Attribution requires current native material, protected review and exact subject ancestry.");
        var source = new ModerationCaseAttributionSource
        {
            Id = id,
            CaseId = caseId,
            Generation = generation,
            OccurrenceCaseId = occurrence.Id,
            OccurrenceId = occurrence.OccurrenceId,
            OccurrenceCaseRevision = occurrence.Revision,
            EvidenceCaseRevision = evidenceRevision,
            EvidenceSourceId = material.Id,
            EvidenceSourceVersion = material.Version,
            MaterialFactId = material.MaterialFactId,
            InvestigationSourceId = investigation?.Id,
            InvestigationSourceRevision = investigation?.Revision,
            TenantId = occurrence.Scope.Id,
            TrustDomainId = host.TrustDomainId,
            HostBindingId = host.Id,
            QualificationEpochId = epoch.Id,
            QualificationRevision = epoch.Revision,
            Basis = basis,
            SubjectKind = subject.Kind,
            SubjectId = subject.Id,
            QualifiedByHumanId = readGrant.CallerUserId,
            QualifierRoleGrantId = qualifierRoleGrantId,
            QualifierPlatformGrant = qualifierPlatformGrant,
            ReadGrantId = readGrant.Id,
            ReadGrantRevision = readGrant.Revision,
            LifetimePolicyId = policy.Id,
            LifetimePolicyRevision = policy.Revision,
            NativeResponsibilityDigest = responsibilityDigest,
            QualificationFactsDigest = qualificationFactsDigest,
            QualifiedAt = now.ToUniversalTime(),
            ValidUntil = validUntil.ToUniversalTime(),
            Revision = 1
        };
        source.ValidatePersistedState();
        return source;
    }

    public bool IsCurrentAt(DateTimeOffset now) =>
        WithdrawnAt is null && now >= QualifiedAt && now < ValidUntil;

    public void Withdraw(Guid operationId, long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        ModerationHostBinding.RequireUuidV7(operationId, nameof(operationId));
        ModerationHostBinding.RequireUuidV7(humanId, nameof(humanId));
        if (expectedRevision != Revision || WithdrawnAt is not null)
            throw new InvalidOperationException("The attribution source is no longer current.");
        if (now.Offset != TimeSpan.Zero || now < QualifiedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Withdrawal requires a finite non-regressing UTC instant.", nameof(now));
        WithdrawalOperationId = operationId;
        WithdrawnByHumanId = humanId;
        WithdrawnAt = now;
        Revision = checked(Revision + 1);
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, CaseId, OccurrenceCaseId, OccurrenceId, EvidenceSourceId,
            MaterialFactId, TenantId, TrustDomainId, HostBindingId, QualificationEpochId, SubjectId,
            QualifiedByHumanId, QualifierRoleGrantId, ReadGrantId, LifetimePolicyId })
            ModerationHostBinding.RequireUuidV7(id, nameof(id));
        var subjectKind = Basis switch
        {
            ModerationResponsibilityBasis.EventSubmission => ModerationSubjectKind.Human,
            ModerationResponsibilityBasis.EventContent => ModerationSubjectKind.Content,
            ModerationResponsibilityBasis.OrganizationalActor => ModerationSubjectKind.Organization,
            ModerationResponsibilityBasis.ServiceIdentity => ModerationSubjectKind.Service,
            ModerationResponsibilityBasis.ParticipantInvestigation => ModerationSubjectKind.Human,
            _ => throw new ArgumentException("Unknown responsibility basis.")
        };
        if (Basis == ModerationResponsibilityBasis.ParticipantInvestigation)
        {
            if (InvestigationSourceId is not { } investigationId || InvestigationSourceRevision != 1)
                throw new ArgumentException("Participant investigation requires exact original assessment coordinates.");
            ModerationHostBinding.RequireUuidV7(investigationId, nameof(InvestigationSourceId));
        }
        else if (InvestigationSourceId is not null || InvestigationSourceRevision is not null)
            throw new ArgumentException("Other responsibility bases cannot contain investigation coordinates.");
        if (Generation < 1 || CaseId == OccurrenceCaseId || EvidenceCaseRevision < 1 || OccurrenceCaseRevision < EvidenceCaseRevision
            || EvidenceSourceVersion == Guid.Empty || QualificationRevision < 1 || ReadGrantRevision < 1
            || LifetimePolicyRevision < 1 || SubjectKind != subjectKind
            || SubjectKind == ModerationSubjectKind.Human && SubjectId == QualifiedByHumanId
            || NativeResponsibilityDigest.Length != 64 || !NativeResponsibilityDigest.All(char.IsAsciiHexDigitLower)
            || QualificationFactsDigest.Length != 64 || !QualificationFactsDigest.All(char.IsAsciiHexDigitLower)
            || QualifiedAt == default || QualifiedAt.Offset != TimeSpan.Zero || ValidUntil.Offset != TimeSpan.Zero
            || ValidUntil <= QualifiedAt || ValidUntil == DateTimeOffset.MaxValue
            || (WithdrawnAt is null
                ? Revision != 1 || WithdrawnByHumanId is not null || WithdrawalOperationId is not null
                : Revision != 2 || WithdrawnAt < QualifiedAt || WithdrawnByHumanId is null
                    || WithdrawnAt.Value.Offset != TimeSpan.Zero || WithdrawnAt == DateTimeOffset.MaxValue
                    || WithdrawalOperationId is null))
            throw new ArgumentException("The finite attribution source has inconsistent ownership or qualification.");
        if (WithdrawnByHumanId is { } human)
            ModerationHostBinding.RequireUuidV7(human, nameof(WithdrawnByHumanId));
        if (WithdrawalOperationId is { } operation)
            ModerationHostBinding.RequireUuidV7(operation, nameof(WithdrawalOperationId));
    }

    public override string ToString() => nameof(ModerationCaseAttributionSource);
}
