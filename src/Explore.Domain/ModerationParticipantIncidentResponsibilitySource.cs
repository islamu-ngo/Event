using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable encrypted investigator assessment provenance for one participant incident and alleged Human.
/// Record validates storage shape and supplied entity linkage, not native qualification or permission.
/// The native writer must authenticate the assessment and original narrative, reacquire current investigator
/// authority and independent read/custody grants, and establish substantive incident-specific grounds.
/// These scalar observations cannot prove who acted. A roster/link alone, vague accusation, finding,
/// sanction, risk admission or automatic restriction is not supplied or conferred by this row.
/// One independently qualified assessment may support opening responsibility UNDER REVIEW without
/// mandatory corroboration. Withdrawal removes this source's availability, never rewrites its origin.
/// </summary>
public sealed class ModerationParticipantIncidentResponsibilitySource
{
    private ModerationParticipantIncidentResponsibilitySource() { }
    private byte[] protectedAssessment = [];

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OccurrenceCaseId { get; private set; }
    public Guid OccurrenceId { get; private set; }
    public long OccurrenceCaseRevision { get; private set; }
    public long EvidenceCaseRevision { get; private set; }
    public Guid EvidenceSourceId { get; private set; }
    public Guid EvidenceSourceVersion { get; private set; }
    public Guid MaterialFactId { get; private set; }
    public Guid OriginalNarrativeId { get; private set; }
    public DateTimeOffset OriginalNarrativePurgeAt { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventVersion { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid SessionGeneration { get; private set; }
    public DateTimeOffset IncidentWindowFrom { get; private set; }
    public DateTimeOffset IncidentWindowUntil { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid OrderGeneration { get; private set; }
    public Guid ParticipantId { get; private set; }
    public Guid ParticipantGeneration { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid AssessorHumanId { get; private set; }
    public Guid AssessorRoleGrantId { get; private set; }
    public Guid ReadIssuerHumanId { get; private set; }
    public Guid ReadGrantId { get; private set; }
    public long ReadGrantRevision { get; private set; }
    public DateTimeOffset ReadGrantExpiresAt { get; private set; }
    public Guid QualificationPolicyId { get; private set; }
    public long QualificationPolicyRevision { get; private set; }
    public long MaximumQualificationLifetimeTicks { get; private set; }
    public ModerationCaseTargetKind TargetKind { get; private set; }
    public ModerationCaseNarrativePurpose Purpose { get; private set; }
    public Guid CustodySourceId { get; private set; }
    public long CustodySourceRevision { get; private set; }
    public long CustodySourceWriterGeneration { get; private set; }
    public DateTimeOffset CustodySourceValidUntil { get; private set; }
    public long MaximumRetentionTicks { get; private set; }
    public Guid CustodyAdoptionId { get; private set; }
    public long CustodyAdoptionRevision { get; private set; }
    public long CustodyAdoptionWriterGeneration { get; private set; }
    public DateTimeOffset CustodyAdoptionValidUntil { get; private set; }
    public DateTimeOffset QualifiedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public DateTimeOffset PurgeAt { get; private set; }
    public string AssessmentDigest { get; private set; } = string.Empty;
    public ReadOnlyMemory<byte> ProtectedAssessment => protectedAssessment.ToArray();
    public long Revision { get; private set; }
    public Guid? WithdrawalOperationId { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>
    /// Captures supplied native coordinates and already authenticated encrypted material.
    /// The original narrative is independently held allegation custody; investigation custody
    /// requires its own purpose-specific source and adoption and cannot borrow that authorization.
    /// Session.Event supplies the actual Event/version; no participant/account link is treated
    /// as proof of the alleged Human's conduct. Ciphertext must contain the host's authenticated
    /// encryption envelope; byte shape alone does not verify encryption or authentication.
    /// </summary>
    public static ModerationParticipantIncidentResponsibilitySource Record(
        Guid id, ModerationHostBinding host, ModerationCase occurrence, long evidenceRevision,
        ModerationCaseTargetSource material, ModerationCaseNarrative originalNarrative,
        EventSession session, RegistrationOrder order, RegistrationParticipant participant,
        SubjectReference subject, Guid assessorRoleGrantId, ModerationCaseTargetPolicy qualificationPolicy,
        ModerationCaseNarrativePolicySource custodySource, ModerationCaseNarrativeAdoption custodyAdoption,
        ModerationCaseReadGrant readGrant, DateTimeOffset incidentWindowFrom, DateTimeOffset incidentWindowUntil,
        DateTimeOffset qualifiedAt, DateTimeOffset validUntil, DateTimeOffset purgeAt,
        ReadOnlySpan<byte> ciphertext, string assessmentDigest)
    {
        occurrence.ValidatePersistedState();
        originalNarrative.ValidatePersistedState();
        custodySource.ValidatePersistedState();
        custodyAdoption.ValidatePersistedState();
        var evidence = material.Evidence(occurrence.Scope);
        var @event = session.Event;
        if (occurrence.Scope.Kind != ModerationScopeKind.Tenant
            || occurrence.Scope.Coordinates.SolutionId != host.SolutionId
            || occurrence.Scope.Coordinates.InstanceId != host.InstanceId
            || occurrence.Scope.TrustDomainId != host.TrustDomainId
            || occurrence.Target is null || occurrence.Subject is not null
            || !occurrence.Evidence.Any(row => row.CaseRevision == evidenceRevision
                && row.ToReference(occurrence.Target, occurrence.OccurrenceId) == evidence)
            || material.Kind != ModerationCaseTargetKind.ParticipantIncident
            || material.NativeKind != ModerationCaseTargetKind.ParticipantIncident
            || originalNarrative.CaseId != occurrence.Id || originalNarrative.SourceId != material.Id
            || originalNarrative.SourceVersion != material.Version || originalNarrative.MaterialFactId != material.MaterialFactId
            || originalNarrative.HostBindingId != host.Id || originalNarrative.TenantId != occurrence.Scope.Id
            || originalNarrative.TrustDomainId != host.TrustDomainId
            || qualifiedAt < originalNarrative.CapturedAt || qualifiedAt < material.RecordedAt
            || material.EventId != @event.Id || material.EventVersion != @event.ConcurrencyStamp
            || session.EventId != @event.Id || session.TenantId != occurrence.Scope.Id
            || @event.TenantId != occurrence.Scope.Id || order.EventId != @event.Id
            || order.TenantId != occurrence.Scope.Id || participant.TenantId != occurrence.Scope.Id
            || participant.RegistrationOrderId != order.Id
            || material.NativeTargetId != participant.Id || material.NativeTargetVersion != participant.ConcurrencyStamp
            || subject.Kind != ModerationSubjectKind.Human || subject.Origin != occurrence.Scope.Parent
            || readGrant.CaseId != occurrence.Id || readGrant.CaseRevision != occurrence.Revision
            || readGrant.SourceId != material.Id || readGrant.TenantId != occurrence.Scope.Id
            || readGrant.TrustDomainId != host.TrustDomainId || !readGrant.IsCurrentAt(qualifiedAt)
            || qualificationPolicy.TargetKind != ModerationCaseTargetKind.ParticipantIncident
            || qualificationPolicy.TenantId != occurrence.Scope.Id || qualificationPolicy.TrustDomainId != host.TrustDomainId
            || !qualificationPolicy.Enabled
            || custodySource.Purpose != ModerationCaseNarrativePurpose.ParticipantInvestigation
            || custodySource.TargetKind != ModerationCaseTargetKind.ParticipantIncident
            || custodySource.HostBindingId != host.Id || custodySource.TrustDomainId != host.TrustDomainId
            || custodySource.TenantId != occurrence.Scope.Id || !custodyAdoption.MatchesCurrent(custodySource, qualifiedAt))
            throw new ArgumentException("Incident assessment storage requires exact original evidence, Human ancestry and separately adopted investigation custody.");

        ModerationQualificationPolicySource.RequireValidity(readGrant.IssuedAt, readGrant.ExpiresAt);
        var source = new ModerationParticipantIncidentResponsibilitySource
        {
            Id = id,
            HostBindingId = host.Id,
            TrustDomainId = host.TrustDomainId,
            TenantId = occurrence.Scope.Id,
            OccurrenceCaseId = occurrence.Id,
            OccurrenceId = occurrence.OccurrenceId,
            OccurrenceCaseRevision = occurrence.Revision,
            EvidenceCaseRevision = evidenceRevision,
            EvidenceSourceId = material.Id,
            EvidenceSourceVersion = material.Version,
            MaterialFactId = material.MaterialFactId,
            OriginalNarrativeId = originalNarrative.Id,
            OriginalNarrativePurgeAt = originalNarrative.PurgeAt,
            EventId = @event.Id,
            EventVersion = @event.ConcurrencyStamp,
            SessionId = session.Id,
            SessionGeneration = session.ConcurrencyStamp,
            IncidentWindowFrom = incidentWindowFrom,
            IncidentWindowUntil = incidentWindowUntil,
            OrderId = order.Id,
            OrderGeneration = order.ConcurrencyStamp,
            ParticipantId = participant.Id,
            ParticipantGeneration = participant.ConcurrencyStamp,
            SubjectKind = subject.Kind,
            SubjectId = subject.Id,
            AssessorHumanId = readGrant.CallerUserId,
            AssessorRoleGrantId = assessorRoleGrantId,
            ReadIssuerHumanId = readGrant.IssuerUserId,
            ReadGrantId = readGrant.Id,
            ReadGrantRevision = readGrant.Revision,
            ReadGrantExpiresAt = readGrant.ExpiresAt,
            QualificationPolicyId = qualificationPolicy.Id,
            QualificationPolicyRevision = qualificationPolicy.Revision,
            MaximumQualificationLifetimeTicks = qualificationPolicy.ReadLifetimeTicks,
            TargetKind = custodySource.TargetKind,
            Purpose = custodySource.Purpose,
            CustodySourceId = custodySource.Id,
            CustodySourceRevision = custodySource.Revision,
            CustodySourceWriterGeneration = custodySource.WriterGeneration,
            CustodySourceValidUntil = custodySource.ValidUntil,
            MaximumRetentionTicks = custodySource.MaximumRetentionTicks,
            CustodyAdoptionId = custodyAdoption.Id,
            CustodyAdoptionRevision = custodyAdoption.Revision,
            CustodyAdoptionWriterGeneration = custodyAdoption.WriterGeneration,
            CustodyAdoptionValidUntil = custodyAdoption.ValidUntil,
            QualifiedAt = qualifiedAt,
            ValidUntil = validUntil,
            PurgeAt = purgeAt,
            protectedAssessment = ciphertext.ToArray(),
            AssessmentDigest = assessmentDigest,
            Revision = 1
        };
        source.ValidatePersistedState();
        return source;
    }

    /// <summary>Local storage availability only; use requires fresh native qualification.</summary>
    public bool IsCurrentAt(DateTimeOffset now) => WithdrawnAt is null && now >= QualifiedAt && now < ValidUntil;

    public void Withdraw(Guid operationId, long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || WithdrawnAt is not null)
            throw new InvalidOperationException("Withdrawal requires the original unwithdrawn incident assessment revision.");
        ModerationHostBinding.RequireUuidV7(operationId, nameof(operationId));
        ModerationHostBinding.RequireUuidV7(humanId, nameof(humanId));
        if (now.Offset != TimeSpan.Zero || now < QualifiedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Withdrawal requires a finite non-regressing UTC instant.");
        WithdrawalOperationId = operationId;
        WithdrawnByHumanId = humanId;
        WithdrawnAt = now;
        Revision = 2;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, OccurrenceCaseId, OccurrenceId,
            EvidenceSourceId, MaterialFactId, OriginalNarrativeId, EventId, SessionId, OrderId, ParticipantId,
            SubjectId, AssessorHumanId, AssessorRoleGrantId, ReadIssuerHumanId, ReadGrantId,
            QualificationPolicyId, CustodySourceId, CustodyAdoptionId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(QualifiedAt, ValidUntil);
        ModerationQualificationPolicySource.RequireValidity(QualifiedAt, PurgeAt);
        ModerationQualificationPolicySource.RequireValidity(QualifiedAt, OriginalNarrativePurgeAt);
        ModerationQualificationPolicySource.RequireValidity(QualifiedAt, ReadGrantExpiresAt);
        ModerationQualificationPolicySource.RequireValidity(QualifiedAt, CustodySourceValidUntil);
        ModerationQualificationPolicySource.RequireValidity(QualifiedAt, CustodyAdoptionValidUntil);
        ModerationQualificationPolicySource.RequireValidity(IncidentWindowFrom, IncidentWindowUntil);
        if (TargetKind != ModerationCaseTargetKind.ParticipantIncident
            || Purpose != ModerationCaseNarrativePurpose.ParticipantInvestigation
            || SubjectKind != ModerationSubjectKind.Human || SubjectId == AssessorHumanId
            || SubjectId == ReadIssuerHumanId
            || EvidenceCaseRevision < 1 || OccurrenceCaseRevision < EvidenceCaseRevision
            || EvidenceSourceVersion == Guid.Empty || EventVersion == Guid.Empty || SessionGeneration == Guid.Empty
            || OrderGeneration == Guid.Empty || ParticipantGeneration == Guid.Empty || IncidentWindowUntil > QualifiedAt
            || ReadGrantRevision != 1 || QualificationPolicyRevision < 1 || CustodySourceRevision != 1
            || CustodyAdoptionRevision != 1 || CustodySourceWriterGeneration is < 1 or long.MaxValue
            || CustodyAdoptionWriterGeneration <= CustodySourceWriterGeneration
            || CustodyAdoptionWriterGeneration == long.MaxValue || CustodyAdoptionId == CustodySourceId
            || MaximumQualificationLifetimeTicks is <= 0 or long.MaxValue || MaximumRetentionTicks is <= 0 or long.MaxValue
            || ValidUntil > PurgeAt || PurgeAt > OriginalNarrativePurgeAt || PurgeAt > ReadGrantExpiresAt
            || PurgeAt > CustodyAdoptionValidUntil || CustodyAdoptionValidUntil > CustodySourceValidUntil
            || PurgeAt.UtcTicks - QualifiedAt.UtcTicks > MaximumRetentionTicks
            || ValidUntil.UtcTicks - QualifiedAt.UtcTicks > MaximumQualificationLifetimeTicks
            || protectedAssessment.Length is < 32 or > 131072
            || AssessmentDigest.Length != 64 || !AssessmentDigest.All(char.IsAsciiHexDigitLower)
            || !(Revision == 1 && WithdrawnAt is null && WithdrawnByHumanId is null && WithdrawalOperationId is null
                || Revision == 2 && WithdrawnAt is { } at && at >= QualifiedAt && at.Offset == TimeSpan.Zero
                    && at != DateTimeOffset.MaxValue && WithdrawnByHumanId is not null && WithdrawalOperationId is not null))
            throw new ArgumentException("Incident assessment storage has inconsistent immutable origin, finite custody or withdrawal coordinates.");
        if (WithdrawnByHumanId is { } human)
            ModerationHostBinding.RequireUuidV7(human, nameof(WithdrawnByHumanId));
        if (WithdrawalOperationId is { } operation)
            ModerationHostBinding.RequireUuidV7(operation, nameof(WithdrawalOperationId));
    }

    public override string ToString() => nameof(ModerationParticipantIncidentResponsibilitySource);
}
