namespace Explore.Domain;

public enum ModerationQualificationEvidencePurpose
{
    Response = 1,
    Controller = 2,
    Conflict = 3,
    Urgency = 4
}

/// <summary>
/// Immutable case-specific assertions, not proof of authority or canonical human ownership.
/// The native writer must resolve the case, humans, source fences and policy validity ceilings.
/// </summary>
public sealed class ModerationQualificationEvidence
{
    private ModerationQualificationEvidence()
    {
    }

    public Guid Id { get; private set; }
    public ModerationQualificationEvidencePurpose Purpose { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? SubjectHumanId { get; private set; }
    public Guid? ReviewerHumanId { get; private set; }
    public Guid? ControllerHumanId { get; private set; }
    public Guid AssertedByHumanId { get; private set; }
    public string Justification { get; private set; } = string.Empty;
    public DateTimeOffset? ValidUntil { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public int? ConflictState { get; private set; }
    public long Revision { get; private set; }

    public static ModerationQualificationEvidence RecordResponse(
        Guid id, Guid caseId, Guid trustDomainId, Guid tenantId, Guid subjectHumanId,
        Guid recordedByHumanId, string statement, DateTimeOffset now) =>
        Create(id, ModerationQualificationEvidencePurpose.Response, caseId, trustDomainId, tenantId,
            subjectHumanId, null, null, recordedByHumanId, statement, null, now, null);

    public static ModerationQualificationEvidence QualifyController(
        Guid id, Guid caseId, Guid trustDomainId, Guid tenantId, Guid reviewerHumanId,
        Guid controllerHumanId, Guid assessorHumanId, string justification,
        DateTimeOffset validUntil, DateTimeOffset now) =>
        Create(id, ModerationQualificationEvidencePurpose.Controller, caseId, trustDomainId, tenantId,
            null, reviewerHumanId, controllerHumanId, assessorHumanId, justification, validUntil, now, null);

    public static ModerationQualificationEvidence AssessConflict(
        Guid id, Guid caseId, Guid trustDomainId, Guid tenantId, Guid reviewerHumanId,
        Guid controllerHumanId, Guid assessorHumanId, int conflictState, string justification,
        DateTimeOffset validUntil, DateTimeOffset now) =>
        Create(id, ModerationQualificationEvidencePurpose.Conflict, caseId, trustDomainId, tenantId,
            null, reviewerHumanId, controllerHumanId, assessorHumanId, justification, validUntil, now, conflictState);

    public static ModerationQualificationEvidence QualifyUrgency(
        Guid id, Guid caseId, Guid trustDomainId, Guid tenantId, Guid? subjectHumanId,
        Guid assessorHumanId, string justification, DateTimeOffset validUntil, DateTimeOffset now) =>
        Create(id, ModerationQualificationEvidencePurpose.Urgency, caseId, trustDomainId, tenantId,
            subjectHumanId, null, null, assessorHumanId, justification, validUntil, now, null);

    /// <summary>
    /// Validates hydrated scalars without renewing evidence or checking today's clock.
    /// Controller/conflict factories do not know the subject; the writer must independently
    /// exclude the actual case subject from assessment.
    /// </summary>
    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(CaseId, nameof(CaseId));
        ModerationHostBinding.RequireUuidV7(TrustDomainId, nameof(TrustDomainId));
        ModerationHostBinding.RequireUuidV7(TenantId, nameof(TenantId));
        if (AssertedByHumanId == Guid.Empty || SubjectHumanId == Guid.Empty
            || ReviewerHumanId == Guid.Empty || ControllerHumanId == Guid.Empty
            || Revision != 1 || RecordedAt == DateTimeOffset.MinValue
            || RecordedAt == DateTimeOffset.MaxValue || RecordedAt.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(Justification) || Justification.Length > 4000
            || Justification != Justification.Trim() || !Justification.Any(char.IsLetterOrDigit))
        {
            throw new ArgumentException("Evidence requires canonical identities, substantive bounded text and a recorded instant.");
        }

        var validShape = Purpose switch
        {
            ModerationQualificationEvidencePurpose.Response =>
                SubjectHumanId is not null && AssertedByHumanId == SubjectHumanId
                && ReviewerHumanId is null && ControllerHumanId is null
                && ConflictState is null && ValidUntil is null,
            ModerationQualificationEvidencePurpose.Controller =>
                SubjectHumanId is null && ReviewerHumanId is not null && ControllerHumanId is not null
                && ConflictState is null,
            ModerationQualificationEvidencePurpose.Conflict =>
                SubjectHumanId is null && ReviewerHumanId is not null && ControllerHumanId is not null
                && ConflictState is 2 or 3,
            ModerationQualificationEvidencePurpose.Urgency =>
                ReviewerHumanId is null && ControllerHumanId is null && ConflictState is null,
            _ => false
        };
        if (!validShape)
        {
            throw new ArgumentException("Evidence purpose and optional scalar fields are inconsistent.");
        }

        if (Purpose != ModerationQualificationEvidencePurpose.Response
            && (ValidUntil is null || ValidUntil <= RecordedAt || ValidUntil == DateTimeOffset.MaxValue
                || ValidUntil.Value.Offset != TimeSpan.Zero || AssertedByHumanId == ReviewerHumanId
                || AssertedByHumanId == ControllerHumanId || AssertedByHumanId == SubjectHumanId))
        {
            throw new ArgumentException("Attestations require an independent assessor and future finite validity.");
        }
    }

    private static ModerationQualificationEvidence Create(
        Guid id, ModerationQualificationEvidencePurpose purpose, Guid caseId, Guid trustDomainId,
        Guid tenantId, Guid? subjectHumanId, Guid? reviewerHumanId, Guid? controllerHumanId,
        Guid assertedByHumanId, string justification, DateTimeOffset? validUntil,
        DateTimeOffset now, int? conflictState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        var evidence = new ModerationQualificationEvidence
        {
            Id = id,
            Purpose = purpose,
            CaseId = caseId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            SubjectHumanId = subjectHumanId,
            ReviewerHumanId = reviewerHumanId,
            ControllerHumanId = controllerHumanId,
            AssertedByHumanId = assertedByHumanId,
            Justification = justification.Trim(),
            ValidUntil = validUntil?.ToUniversalTime(),
            RecordedAt = now.ToUniversalTime(),
            ConflictState = conflictState,
            Revision = 1
        };
        evidence.ValidatePersistedState();
        return evidence;
    }

    public override string ToString() => nameof(ModerationQualificationEvidence);
}
