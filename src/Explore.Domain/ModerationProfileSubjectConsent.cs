using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Private existing-Human ProfileCompletion self-consent metadata, not authenticated permission
/// or a collection receipt. Native owners authenticate the actual subject and independently
/// accepted applicability assessment, source/adoption and latest generations under fences.
/// Cancellation, identity resolution and before-body acceptance remain native responsibilities.
/// Other assessed prerequisites are retained, never satisfied by this subject operation.
/// Later native PII erasure must hard-delete this private owner; it is not an immortal
/// pseudonymous recognition record and cannot reconstruct DOB, history or strikes.
/// </summary>
public sealed class ModerationProfileSubjectConsent
{
    private ModerationProfileSubjectConsent() { }

    public Guid Id { get; private set; }
    public ModerationProfileCollectionProcedureSource Source { get; private set; } = null!;
    public Guid ProcedureAdoptionId { get; private set; }
    public long ProcedureAdoptionRevision { get; private set; }
    public long ProcedureAdoptionWriterGeneration { get; private set; }
    public Guid ProcedureAdoptedByHumanId { get; private set; }
    public DateTimeOffset ProcedureAdoptedAt { get; private set; }
    public DateTimeOffset ProcedureAdoptionValidUntil { get; private set; }
    public ModerationIndividualBinding Binding { get; private set; } = null!;
    public ModerationCollectionPrerequisiteAssessment Assessment { get; private set; } = null!;
    public Guid SubjectHumanId { get; private set; }
    public ModerationIndividualPurpose Purpose => Binding.Purpose;
    public long Generation { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    /// <summary>
    /// Records the explicit operation of the actual authenticated Human supplied by the native
    /// owner. Equality with the bound subject checks metadata only; it does not authenticate
    /// the caller or accept an assessment. Generation is the native consent slot's own ordered
    /// generation, unrelated to individual observation or global qualification writer sequences.
    /// </summary>
    public static ModerationProfileSubjectConsent Grant(Guid id,
        ModerationProfileCollectionProcedureSource source, ModerationProfileCollectionProcedureAdoption adoption,
        ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption parentAdoption,
        ModerationIndividualBinding binding, ModerationCollectionPrerequisiteAssessment assessment,
        Guid actualAuthenticatedHumanId, long generation, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adoption);
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(parentAdoption);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(assessment);
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        age.ValidatePersistedState();
        parentAdoption.ValidatePersistedState();
        if (!adoption.MatchesCurrent(source, age, parentAdoption, now))
            throw new ArgumentException("Subject consent requires structurally current exact adopted procedure and child-age parents.");

        var consent = new ModerationProfileSubjectConsent
        {
            Id = id,
            Source = source,
            ProcedureAdoptionId = adoption.Id,
            ProcedureAdoptionRevision = adoption.Revision,
            ProcedureAdoptionWriterGeneration = adoption.WriterGeneration,
            ProcedureAdoptedByHumanId = adoption.AdoptedByHumanId,
            ProcedureAdoptedAt = adoption.AdoptedAt,
            ProcedureAdoptionValidUntil = adoption.ValidUntil,
            Binding = binding,
            Assessment = new(assessment.Binding, assessment.Reference, assessment.Required),
            SubjectHumanId = actualAuthenticatedHumanId,
            Generation = generation,
            Revision = 1,
            GrantedAt = now,
            ValidUntil = validUntil
        };
        consent.ValidatePersistedState();
        return consent;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(GrantedAt, ValidUntil);
        if (Source is null || Binding is null || Assessment is null)
            throw new ArgumentException("Subject consent requires its original procedure, binding and assessment.");
        Source.ValidatePersistedState();
        ModerationHostBinding.RequireUuidV7(ProcedureAdoptionId, nameof(ProcedureAdoptionId));
        ModerationQualificationPolicySource.RequireValidity(ProcedureAdoptedAt, ProcedureAdoptionValidUntil);
        if (new[] { Source.Id, ProcedureAdoptionId, Source.ChildAgeSourceId, Source.ChildAgeAdoptionId,
                Source.HostBindingId, Source.TrustDomainId, Source.TenantId, Source.PolicyId, Source.PublicationId,
                Source.DefinitionId, Source.ProfileId, Binding.SourceId, Binding.ObservationStamp,
                Assessment.Reference.Id, SubjectHumanId }.Contains(Id)
            || new[] { Source.Id, Source.ChildAgeSourceId, Source.ChildAgeAdoptionId,
                Source.HostBindingId, Source.TrustDomainId, Source.TenantId, Source.PolicyId,
                Source.PublicationId, Source.DefinitionId, Source.ProfileId }.Contains(ProcedureAdoptionId)
            || ProcedureAdoptionRevision != 1
            || ProcedureAdoptionWriterGeneration <= Source.WriterGeneration
            || ProcedureAdoptionWriterGeneration == long.MaxValue
            || ProcedureAdoptedByHumanId != Source.ReviewerHumanId
            || ProcedureAdoptedAt < Source.RecordedAt || ProcedureAdoptionValidUntil > Source.ValidUntil
            || Binding.Target.Kind != ModerationIndividualTargetKind.Human
            || Binding.Target.HumanSubject is not { Kind: ModerationSubjectKind.Human } subject
            || subject.Origin.Kind != ModerationScopeKind.Instance
            || SubjectHumanId == Guid.Empty || SubjectHumanId != subject.Id
            || Binding.Purpose != ModerationIndividualPurpose.ProfileCompletion
            || Binding.PolicyId != Source.PolicyId || Binding.PolicyRevision != Source.PolicyRevision
            || Binding.Jurisdiction.TrustDomainId != Source.TrustDomainId
            || Binding.Jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
            || Binding.Jurisdiction.Kind == ModerationScopeKind.Tenant && Binding.Jurisdiction.Id != Source.TenantId
            || !Source.Procedure.Supports(Binding)
            || !Source.Procedure.AllowsPreliminaryField(ModerationProfilePreliminaryFieldKind.SubjectConsentReference)
            || Assessment.Binding != Binding
            || !Assessment.Required.Contains(ModerationCollectionPrerequisiteKind.SubjectConsent)
            || Assessment.Required.Any(kind => !Enum.IsDefined(kind))
            || Assessment.Required.Distinct().Count() != Assessment.Required.Count
            || Generation is < 1 or long.MaxValue
            || GrantedAt < Binding.ObservedAt || ValidUntil > Binding.ValidUntil
            || GrantedAt < Assessment.Reference.ObservedAt || ValidUntil > Assessment.Reference.ValidUntil
            || GrantedAt < ProcedureAdoptedAt || ValidUntil > ProcedureAdoptionValidUntil
            || Revision is not (1 or 2)
            || Revision == 1 && WithdrawnAt is not null
            || Revision == 2 && (WithdrawnAt is not { } withdrawn || !IsInstant(withdrawn) || withdrawn < GrantedAt))
            throw new ArgumentException("Subject consent requires exact original self, assessment, procedure and finite grant coverage with terminal withdrawal.");
    }

    /// <summary>
    /// Structural currentness against originals, not authentication or a latest-generation
    /// lookup. Native owners must supply independently resolved current inputs, never echo
    /// retained snapshots. Any replacement requires a new native decision, not rebinding.
    /// </summary>
    public bool MatchesCurrentMetadata(ModerationProfileCollectionProcedureSource source,
        ModerationProfileCollectionProcedureAdoption adoption, ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption parentAdoption, ModerationIndividualBinding binding,
        ModerationCollectionPrerequisiteAssessment assessment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adoption);
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(parentAdoption);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(assessment);
        ValidatePersistedState();
        return Revision == 1 && WithdrawnAt is null && IsInstant(now) && now >= GrantedAt && now < ValidUntil
            && Binding == binding && Assessment.Binding == assessment.Binding
            && Assessment.Reference == assessment.Reference && Assessment.Required.SequenceEqual(assessment.Required)
            && Source.Matches(source)
            && ProcedureAdoptionId == adoption.Id && ProcedureAdoptionRevision == adoption.Revision
            && ProcedureAdoptionWriterGeneration == adoption.WriterGeneration
            && ProcedureAdoptedByHumanId == adoption.AdoptedByHumanId
            && ProcedureAdoptedAt == adoption.AdoptedAt && ProcedureAdoptionValidUntil == adoption.ValidUntil
            && adoption.MatchesCurrent(source, age, parentAdoption, now);
    }

    /// <summary>
    /// Metadata conversion only, never authenticated permission or a fabricated collection
    /// receipt. The native reference retains this aggregate's exact revision, consent-slot
    /// generation and ORIGINAL finite grant window, not the time of conversion.
    /// </summary>
    public ModerationCollectionPrerequisite ToPrerequisite(ModerationProfileCollectionProcedureSource source,
        ModerationProfileCollectionProcedureAdoption adoption, ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption parentAdoption, ModerationIndividualBinding binding,
        ModerationCollectionPrerequisiteAssessment assessment, DateTimeOffset now)
    {
        if (!MatchesCurrentMetadata(source, adoption, age, parentAdoption, binding, assessment, now))
            throw new InvalidOperationException("Only live exact original subject consent can supply prerequisite metadata.");
        return new(Binding, ModerationCollectionPrerequisiteKind.SubjectConsent,
            new(Id, Revision, Generation, GrantedAt, ValidUntil));
    }

    /// <summary>
    /// Terminal exact-revision subject withdrawal, independent of current policy, assessment,
    /// parent availability or expiry. Native owners authenticate the original subject and
    /// fence this write; no grant reversal, renewal or erasure implementation is supplied.
    /// </summary>
    public void Withdraw(long expectedRevision, Guid actualAuthenticatedHumanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || Revision != 1 || WithdrawnAt is not null)
            throw new InvalidOperationException("Subject withdrawal requires the exact live consent revision.");
        if (actualAuthenticatedHumanId == Guid.Empty || actualAuthenticatedHumanId != SubjectHumanId
            || !IsInstant(now) || now < GrantedAt)
            throw new ArgumentException("Subject withdrawal requires the original actual Human and nonregressing finite UTC time.");
        Revision = 2;
        WithdrawnAt = now;
    }

    private static bool IsInstant(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;

    public override string ToString() => nameof(ModerationProfileSubjectConsent);
}
