using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Private existing-Human ProfileCompletion assessment proposal, recorded by the exact
/// nominated collection owner. An explicit complete required set, including an empty set,
/// is metadata, not an authenticated or lawful applicability determination. Native owners
/// determine applicability, authenticate people and current policy/publication/profile/mode,
/// and fence latest sources and individual assessment-slot generations. This row carries no
/// individual evidence or prerequisite and must be hard-deleted with PII by later native owners.
/// </summary>
public sealed class ModerationProfileCollectionAssessmentSource
{
    private ModerationProfileCollectionAssessmentSource() { }

    public Guid Id { get; private set; }
    public ModerationProfileCollectionProcedureSource ProcedureSource { get; private set; } = null!;
    public Guid ProcedureSourceId { get; private set; }
    public Guid SubjectHumanId { get; private set; }
    public Guid OwnerControllerHumanId { get; private set; }
    public Guid ReviewerControllerHumanId { get; private set; }
    public Guid ProcedureAdoptionId { get; private set; }
    public long ProcedureAdoptionRevision { get; private set; }
    public long ProcedureAdoptionWriterGeneration { get; private set; }
    public Guid ProcedureAdoptedByHumanId { get; private set; }
    public DateTimeOffset ProcedureAdoptedAt { get; private set; }
    public DateTimeOffset ProcedureAdoptionValidUntil { get; private set; }
    public ModerationIndividualBinding Binding { get; private set; } = null!;
    public IReadOnlyList<ModerationCollectionPrerequisiteKind> Required { get; private set; } = null!;
    public Guid RecordedByHumanId { get; private set; }
    public long Generation { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    /// <summary>
    /// Records an explicitly supplied native determination proposal, never inferring requirements
    /// from age, account eligibility or field availability. Generation belongs to this exact
    /// individual assessment slot; it is not compared with global policy writers or observations.
    /// </summary>
    public static ModerationProfileCollectionAssessmentSource Record(Guid id,
        ModerationProfileCollectionProcedureSource procedureSource,
        ModerationProfileCollectionProcedureAdoption procedureAdoption,
        ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption parentAdoption,
        ModerationIndividualBinding binding, IEnumerable<ModerationCollectionPrerequisiteKind> required,
        Guid humanId, long generation, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(procedureSource);
        ArgumentNullException.ThrowIfNull(procedureAdoption);
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(parentAdoption);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(required);
        procedureSource.ValidatePersistedState();
        procedureAdoption.ValidatePersistedState();
        age.ValidatePersistedState();
        parentAdoption.ValidatePersistedState();
        if (!procedureAdoption.MatchesCurrent(procedureSource, age, parentAdoption, now))
            throw new ArgumentException("Assessment recording requires exact live procedure and child-age parents.");
        var source = new ModerationProfileCollectionAssessmentSource
        {
            Id = id,
            ProcedureSource = procedureSource,
            ProcedureSourceId = procedureSource.Id,
            SubjectHumanId = binding.Target.HumanSubject?.Id
                ?? throw new ArgumentException("Assessment recording requires the original Human subject."),
            OwnerControllerHumanId = procedureSource.OwnerControllerHumanId,
            ReviewerControllerHumanId = procedureSource.ReviewerControllerHumanId,
            ProcedureAdoptionId = procedureAdoption.Id,
            ProcedureAdoptionRevision = procedureAdoption.Revision,
            ProcedureAdoptionWriterGeneration = procedureAdoption.WriterGeneration,
            ProcedureAdoptedByHumanId = procedureAdoption.AdoptedByHumanId,
            ProcedureAdoptedAt = procedureAdoption.AdoptedAt,
            ProcedureAdoptionValidUntil = procedureAdoption.ValidUntil,
            Binding = binding,
            Required = Array.AsReadOnly(required.Take(4).Order().ToArray()),
            RecordedByHumanId = humanId,
            Generation = generation,
            Revision = 1,
            RecordedAt = now,
            ValidUntil = validUntil
        };
        source.ValidatePersistedState();
        return source;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (ProcedureSource is null || Binding is null || Required is null)
            throw new ArgumentException("Assessment requires original procedure, individual binding and explicit complete required set.");
        ProcedureSource.ValidatePersistedState();
        ModerationHostBinding.RequireUuidV7(ProcedureAdoptionId, nameof(ProcedureAdoptionId));
        ModerationQualificationPolicySource.RequireValidity(ProcedureAdoptedAt, ProcedureAdoptionValidUntil);
        if (!IsInstant(RecordedAt) || !IsInstant(ValidUntil)
            || !IsInstant(ProcedureAdoptedAt) || !IsInstant(ProcedureAdoptionValidUntil)
            || ConflictsWithIdentity(Id)
            || new[] { ProcedureSource.Id, ProcedureSource.ChildAgeSourceId, ProcedureSource.ChildAgeAdoptionId,
                ProcedureSource.HostBindingId, ProcedureSource.TrustDomainId, ProcedureSource.TenantId,
                ProcedureSource.PolicyId, ProcedureSource.PublicationId, ProcedureSource.DefinitionId,
                ProcedureSource.ProfileId }.Contains(ProcedureAdoptionId)
            || ProcedureAdoptionRevision != 1
            || ProcedureAdoptionWriterGeneration <= ProcedureSource.WriterGeneration
            || ProcedureAdoptionWriterGeneration == long.MaxValue
            || ProcedureAdoptedByHumanId != ProcedureSource.ReviewerHumanId
            || ProcedureAdoptedByHumanId == ProcedureSource.RecordedByHumanId
            || ProcedureAdoptedByHumanId == ProcedureSource.OwnerControllerHumanId
            || ProcedureSource.ReviewerControllerHumanId == ProcedureSource.OwnerControllerHumanId
            || ProcedureAdoptedAt < ProcedureSource.RecordedAt
            || ProcedureAdoptionValidUntil > ProcedureSource.ValidUntil
            || RecordedByHumanId != ProcedureSource.OwnerHumanId
            || ProcedureSourceId != ProcedureSource.Id
            || OwnerControllerHumanId != ProcedureSource.OwnerControllerHumanId
            || ReviewerControllerHumanId != ProcedureSource.ReviewerControllerHumanId
            || Binding.Target.Kind != ModerationIndividualTargetKind.Human
            || Binding.Target.HumanSubject is not { Kind: ModerationSubjectKind.Human } subject
            || SubjectHumanId != subject.Id
            || subject.Origin.Kind != ModerationScopeKind.Instance
            || Binding.Purpose != ModerationIndividualPurpose.ProfileCompletion
            || Binding.PolicyId != ProcedureSource.PolicyId || Binding.PolicyRevision != ProcedureSource.PolicyRevision
            || Binding.Jurisdiction.TrustDomainId != ProcedureSource.TrustDomainId
            || Binding.Jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
            || Binding.Jurisdiction.Kind == ModerationScopeKind.Tenant
                && Binding.Jurisdiction.Id != ProcedureSource.TenantId
            || !ProcedureSource.Procedure.Supports(Binding)
            || Required.Count > 3 || Required.Any(kind => !Enum.IsDefined(kind))
            || Required.Distinct().Count() != Required.Count
            || !Required.SequenceEqual(Required.Order())
            || Required.Any(kind => !SupportsRequirement(kind))
            || Generation is < 1 or long.MaxValue || Revision != 1
            || RecordedAt < Binding.ObservedAt || ValidUntil > Binding.ValidUntil
            || RecordedAt < ProcedureAdoptedAt || ValidUntil > ProcedureAdoptionValidUntil)
            throw new ArgumentException("Assessment requires exact nominated owner, native Human profile policy, supported complete set and original finite coverage.");
    }

    /// <summary>
    /// Structural currentness only. Native owners supply resolved current parents and binding
    /// under fences; echoed snapshots cannot prove authentication, latest policy or applicability.
    /// </summary>
    public bool MatchesCurrentMetadata(ModerationProfileCollectionProcedureSource procedureSource,
        ModerationProfileCollectionProcedureAdoption procedureAdoption, ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption parentAdoption, ModerationIndividualBinding binding, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(procedureSource);
        ArgumentNullException.ThrowIfNull(procedureAdoption);
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(parentAdoption);
        ArgumentNullException.ThrowIfNull(binding);
        ValidatePersistedState();
        procedureSource.ValidatePersistedState();
        procedureAdoption.ValidatePersistedState();
        age.ValidatePersistedState();
        parentAdoption.ValidatePersistedState();
        return IsInstant(now) && now >= RecordedAt && now < ValidUntil && Binding == binding
            && ProcedureSource.Matches(procedureSource)
            && ProcedureAdoptionId == procedureAdoption.Id && ProcedureAdoptionRevision == procedureAdoption.Revision
            && ProcedureAdoptionWriterGeneration == procedureAdoption.WriterGeneration
            && ProcedureAdoptedByHumanId == procedureAdoption.AdoptedByHumanId
            && ProcedureAdoptedAt == procedureAdoption.AdoptedAt
            && ProcedureAdoptionValidUntil == procedureAdoption.ValidUntil
            && procedureAdoption.MatchesCurrent(procedureSource, age, parentAdoption, now);
    }

    private bool SupportsRequirement(ModerationCollectionPrerequisiteKind kind) => kind switch
    {
        ModerationCollectionPrerequisiteKind.AccountEligibility =>
            ProcedureSource.Procedure.AllowsPreliminaryField(ModerationProfilePreliminaryFieldKind.AccountEligibilityReference),
        ModerationCollectionPrerequisiteKind.SubjectConsent =>
            ProcedureSource.Procedure.AllowsPreliminaryField(ModerationProfilePreliminaryFieldKind.SubjectConsentReference),
        ModerationCollectionPrerequisiteKind.GuardianConsent =>
            ProcedureSource.Procedure.AllowsPreliminaryField(ModerationProfilePreliminaryFieldKind.GuardianConsentReference)
            && ProcedureSource.Procedure.AllowsPreliminaryField(ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference),
        _ => false
    };

    internal bool ConflictsWithIdentity(Guid id) =>
        new[] { ProcedureSource.Id, ProcedureAdoptionId, ProcedureSource.ChildAgeSourceId,
            ProcedureSource.ChildAgeAdoptionId, ProcedureSource.HostBindingId, ProcedureSource.TrustDomainId,
            ProcedureSource.TenantId, ProcedureSource.PolicyId, ProcedureSource.PublicationId,
            ProcedureSource.DefinitionId, ProcedureSource.ProfileId, Binding.SourceId, Binding.ObservationStamp,
            Binding.Target.HumanSubject?.Id ?? Guid.Empty }.Contains(id);

    internal bool Matches(ModerationProfileCollectionAssessmentSource other) =>
        Id == other.Id && Revision == other.Revision && Generation == other.Generation
        && RecordedByHumanId == other.RecordedByHumanId && RecordedAt == other.RecordedAt
        && ValidUntil == other.ValidUntil && Binding == other.Binding && Required.SequenceEqual(other.Required)
        && ProcedureSource.Matches(other.ProcedureSource)
        && ProcedureAdoptionId == other.ProcedureAdoptionId && ProcedureAdoptionRevision == other.ProcedureAdoptionRevision
        && ProcedureAdoptionWriterGeneration == other.ProcedureAdoptionWriterGeneration
        && ProcedureAdoptedByHumanId == other.ProcedureAdoptedByHumanId
        && ProcedureAdoptedAt == other.ProcedureAdoptedAt
        && ProcedureAdoptionValidUntil == other.ProcedureAdoptionValidUntil;

    internal static bool IsInstant(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;

    public override string ToString() => nameof(ModerationProfileCollectionAssessmentSource);
}
