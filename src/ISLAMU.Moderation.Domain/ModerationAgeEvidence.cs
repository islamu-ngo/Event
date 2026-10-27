namespace ISLAMU.Moderation.Domain;

public enum ModerationAgeEvidenceKind { Unknown = 1, Declared = 2, Assured = 3 }
public enum ModerationAgeAssuranceMethod { LocalQualifiedManual = 1, UnsupportedVendorClaim = 2 }

/// <summary>
/// Accountable local manual assertion provenance, not authenticated identity or professional
/// qualification. Native owners must prove issuer/controller identity, independent qualification,
/// subject separation (including anonymous subjects), and the exact threshold assertion.
/// Known canonical identity/controller equality is rejected here; inequality is not proof.
/// </summary>
public sealed record ModerationAgeAssuranceProvenance
{
    public ModerationAgeAssuranceProvenance(
        ModerationIndividualBinding binding, ModerationAgeAssuranceMethod method,
        SubjectReference issuer, SubjectReference issuerController,
        SubjectReference qualifiedBy, SubjectReference qualifierController,
        SubjectReference? subjectController, ModerationIndividualNativeReference identityReference,
        ModerationIndividualNativeReference qualificationReference)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(identityReference);
        ArgumentNullException.ThrowIfNull(qualificationReference);
        var humans = new[] { issuer, issuerController, qualifiedBy, qualifierController };
        foreach (var human in humans)
        {
            ModerationIndividualTarget.RequireHuman(human);
            if (!human.Origin.Contains(binding.Jurisdiction))
                throw new ArgumentException("Local assurance identities must share the native policy instance.");
        }
        if (subjectController is not null)
        {
            ModerationIndividualTarget.RequireHuman(subjectController);
            if (!subjectController.Origin.Contains(binding.Jurisdiction))
                throw new ArgumentException("The subject controller must share the native policy instance.");
        }
        var subject = binding.Target.HumanSubject;
        if (method != ModerationAgeAssuranceMethod.LocalQualifiedManual
            || subject is not null && subjectController is null
            || humans.Any(human => human == subject || human == subjectController)
            || issuer == qualifiedBy || issuer == qualifierController
            || issuerController == qualifiedBy || issuerController == qualifierController
            || identityReference.Id == qualificationReference.Id)
            throw new ArgumentException("Manual assurance requires distinct subject and independently qualified controllers.");
        Binding = binding;
        Method = method;
        Issuer = issuer;
        IssuerController = issuerController;
        QualifiedBy = qualifiedBy;
        QualifierController = qualifierController;
        SubjectController = subjectController;
        IdentityReference = identityReference;
        QualificationReference = qualificationReference;
    }

    public ModerationIndividualBinding Binding { get; }
    public ModerationAgeAssuranceMethod Method { get; }
    public SubjectReference Issuer { get; }
    public SubjectReference IssuerController { get; }
    public SubjectReference QualifiedBy { get; }
    public SubjectReference QualifierController { get; }
    public SubjectReference? SubjectController { get; }
    public ModerationIndividualNativeReference IdentityReference { get; }
    public ModerationIndividualNativeReference QualificationReference { get; }

    internal bool Covers(DateTimeOffset start, DateTimeOffset end) =>
        IdentityReference.Covers(start, end) && QualificationReference.Covers(start, end);

    public override string ToString() => nameof(ModerationAgeAssuranceProvenance);
}

/// <summary>
/// Threshold metadata only. Even Assured is not an audience grant, collection permission,
/// legal certification or finding. Unknown conveys no guilt or historical eligibility.
/// </summary>
public sealed record ModerationAgeThresholdMetadata
{
    internal ModerationAgeThresholdMetadata(
        ModerationAgeEvidenceKind kind, ModerationDeclaredAgeOutcome outcome, int minimumAge)
    {
        Kind = kind;
        Outcome = outcome;
        MinimumAge = minimumAge;
    }

    public ModerationAgeEvidenceKind Kind { get; }
    public ModerationDeclaredAgeOutcome Outcome { get; }
    public int MinimumAge { get; }
}

/// <summary>
/// Finite minimal threshold assertion with no DOB or reconstructive evidence. Native owners
/// authenticate every supplied coordinate and fence current evidence, qualification and adoption.
/// Retirement discards the usable payload; correction requires a newly observed record and
/// cannot revive this record. Erasure here clears metadata, not copies held by other owners.
/// </summary>
public sealed class ModerationAgeEvidence
{
    private ModerationAgeEvidence(Guid id, ModerationIndividualBinding binding, int minimumAge,
        ModerationAgeEvidenceKind kind, ModerationDeclaredAgeOutcome outcome,
        ModerationAgeAssuranceProvenance? provenance, DateTimeOffset recordedAt, DateTimeOffset validUntil)
    {
        Id = id;
        Binding = binding;
        MinimumAge = minimumAge;
        Kind = kind;
        Outcome = outcome;
        Provenance = provenance;
        RecordedAt = recordedAt;
        UpdatedAt = recordedAt;
        ValidUntil = validUntil;
        State = ModerationIndividualRecordState.Current;
        Revision = 1;
    }

    public Guid Id { get; }
    public ModerationIndividualBinding? Binding { get; private set; }
    public int MinimumAge { get; }
    public ModerationAgeEvidenceKind Kind { get; private set; }
    public ModerationDeclaredAgeOutcome Outcome { get; private set; }
    public ModerationAgeAssuranceProvenance? Provenance { get; private set; }
    public DateTimeOffset RecordedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ModerationIndividualRecordState State { get; private set; }
    public long Revision { get; private set; }

    public static ModerationAgeEvidence Record(
        Guid id, ModerationIndividualBinding binding, int minimumAge,
        ModerationAgeEvidenceKind kind, ModerationDeclaredAgeOutcome outcome,
        ModerationAgeAssuranceProvenance? provenance, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ModerationPolicy.RequireVersion7(id, nameof(id));
        ArgumentNullException.ThrowIfNull(binding);
        ModerationIndividualValidity.RequireWindow(now, validUntil);
        var shape = kind switch
        {
            ModerationAgeEvidenceKind.Unknown =>
                outcome == ModerationDeclaredAgeOutcome.Unknown && provenance is null,
            ModerationAgeEvidenceKind.Declared =>
                outcome is ModerationDeclaredAgeOutcome.UnderThreshold or ModerationDeclaredAgeOutcome.AtLeastThreshold
                && provenance is null,
            ModerationAgeEvidenceKind.Assured =>
                outcome is ModerationDeclaredAgeOutcome.UnderThreshold or ModerationDeclaredAgeOutcome.AtLeastThreshold
                && provenance is not null && provenance.Binding == binding && provenance.Covers(now, validUntil),
            _ => false
        };
        if (!shape || minimumAge < 0 || now < binding.ObservedAt || validUntil > binding.ValidUntil)
            throw new ArgumentException("Age evidence requires a finite exact binding and consistent threshold provenance.");
        return new(id, binding, minimumAge, kind, outcome, provenance, now, validUntil);
    }

    /// <summary>
    /// Matches retained metadata against independently reacquired native coordinates. The caller
    /// must supply the live authority snapshot, not echo these properties. Failure is Unknown,
    /// never retrospective ineligibility, guilt or a sanction.
    /// </summary>
    public ModerationAgeThresholdMetadata EvaluateMetadata(
        ModerationIndividualBinding currentBinding, ModerationAgeAssuranceProvenance? currentProvenance,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(currentBinding);
        if (State != ModerationIndividualRecordState.Current || Binding != currentBinding
            || Provenance != currentProvenance
            || !ModerationIndividualValidity.Contains(RecordedAt, ValidUntil, now))
            return new(ModerationAgeEvidenceKind.Unknown, ModerationDeclaredAgeOutcome.Unknown, MinimumAge);
        return new(Kind, Outcome, MinimumAge);
    }

    public void Retire(long expectedRevision, ModerationIndividualRecordState state, DateTimeOffset now)
    {
        ModerationIndividualValidity.RequireRetirement(State, state, Revision, expectedRevision, UpdatedAt, ValidUntil, now);
        Binding = null;
        Provenance = null;
        Kind = ModerationAgeEvidenceKind.Unknown;
        Outcome = ModerationDeclaredAgeOutcome.Unknown;
        State = state;
        UpdatedAt = now;
        Revision = checked(Revision + 1);
    }

    public override string ToString() => nameof(ModerationAgeEvidence);
}
