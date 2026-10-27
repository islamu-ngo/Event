using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Original canonical existing-Human nomination metadata, hard-deleted with that Human's PII.
/// Neither this row nor its binding is an applicability assessment, prerequisite or collection
/// receipt. Native owners authenticate the subject and current parents; factories validate
/// structure only. The immutable procedure row retains the original child-age snapshots.
/// </summary>
public sealed class ModerationProfileCollectionSubjectObservation
{
    private ModerationProfileCollectionSubjectObservation() { }

    public Guid Id { get; private set; }
    public long Revision { get; private set; }
    public long Generation { get; private set; }
    public long ObservationSequence { get; private set; }
    public Guid ObservationStamp { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid SubjectHumanId { get; private set; }
    public Guid? SubjectLoginId { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid SolutionId { get; private set; }
    public Guid InstanceId { get; private set; }
    public Guid TenantId { get; private set; }
    public int JurisdictionKind { get; private set; }
    public Guid EpochId { get; private set; }
    public long EpochRevision { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int ModeCode { get; private set; }
    public ModerationProfileCollectionProcedureSource ProcedureSource { get; private set; } = null!;
    public Guid ProcedureSourceId { get; private set; }
    public long ProcedureSourceRevision { get; private set; }
    public long ProcedureSourceWriterGeneration { get; private set; }
    public Guid ProcedureAdoptionId { get; private set; }
    public long ProcedureAdoptionRevision { get; private set; }
    public long ProcedureAdoptionWriterGeneration { get; private set; }
    public Guid ProcedureAdoptedByHumanId { get; private set; }
    public DateTimeOffset ProcedureAdoptedAt { get; private set; }
    public DateTimeOffset ProcedureAdoptionValidUntil { get; private set; }
    public string OriginalProcedureDigest { get; private set; } = string.Empty;
    public string OriginalPolicyDigest { get; private set; } = string.Empty;
    public string OriginalSelectionDigest { get; private set; } = string.Empty;

    /// <summary>
    /// Records supplied native originals, never resolving identity or authorizing collection.
    /// The sequence belongs only to this Human/profile/mode observation slot, not policy writers.
    /// The native owner chooses every coordinate except the nominated profile and mode.
    /// </summary>
    public static ModerationProfileCollectionSubjectObservation Record(
        Guid id, Guid human, Guid? loginId, AuthorityScope instance, AuthorityScope jurisdiction,
        Guid epochId, long epochRevision, ModerationProfileCollectionProcedureSource source,
        ModerationProfileCollectionProcedureAdoption adoption, ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption parent, long sequence, Guid stamp,
        DateTimeOffset observedAt, DateTimeOffset validUntil,
        string originalProcedureDigest, string originalPolicyDigest, string originalSelectionDigest)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(jurisdiction);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adoption);
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(parent);
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        age.ValidatePersistedState();
        parent.ValidatePersistedState();
        if (instance.Kind != ModerationScopeKind.Instance || !instance.Contains(jurisdiction)
            || jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
            || jurisdiction.Kind == ModerationScopeKind.Tenant && jurisdiction.Id != source.TenantId
            || instance.TrustDomainId != source.TrustDomainId
            || !adoption.MatchesCurrent(source, age, parent, observedAt))
            throw new ArgumentException("Observation requires exact native scope and live accepted original parents.");
        var row = new ModerationProfileCollectionSubjectObservation
        {
            Id = id,
            Revision = 1,
            Generation = sequence,
            ObservationSequence = sequence,
            ObservationStamp = stamp,
            ObservedAt = observedAt,
            ValidUntil = validUntil,
            SubjectHumanId = human,
            SubjectLoginId = loginId,
            HostBindingId = source.HostBindingId,
            TrustDomainId = source.TrustDomainId,
            SolutionId = instance.Coordinates.SolutionId!.Value,
            InstanceId = instance.Id,
            TenantId = source.TenantId,
            JurisdictionKind = (int)jurisdiction.Kind,
            EpochId = epochId,
            EpochRevision = epochRevision,
            PolicyId = source.PolicyId,
            PolicyRevision = source.PolicyRevision,
            PublicationId = source.PublicationId,
            DefinitionId = source.DefinitionId,
            ProfileId = source.ProfileId,
            ModeCode = source.ModeCode,
            ProcedureSource = source,
            ProcedureSourceId = source.Id,
            ProcedureSourceRevision = source.Revision,
            ProcedureSourceWriterGeneration = source.WriterGeneration,
            ProcedureAdoptionId = adoption.Id,
            ProcedureAdoptionRevision = adoption.Revision,
            ProcedureAdoptionWriterGeneration = adoption.WriterGeneration,
            ProcedureAdoptedByHumanId = adoption.AdoptedByHumanId,
            ProcedureAdoptedAt = adoption.AdoptedAt,
            ProcedureAdoptionValidUntil = adoption.ValidUntil,
            OriginalProcedureDigest = originalProcedureDigest,
            OriginalPolicyDigest = originalPolicyDigest,
            OriginalSelectionDigest = originalSelectionDigest
        };
        row.ValidatePersistedState();
        return row;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(ObservationStamp, nameof(ObservationStamp));
        ModerationHostBinding.RequireUuidV7(EpochId, nameof(EpochId));
        ModerationHostBinding.RequireUuidV7(ProcedureAdoptionId, nameof(ProcedureAdoptionId));
        if (ProcedureSource is null) throw new ArgumentException("Observation requires its original procedure.");
        ProcedureSource.ValidatePersistedState();
        ModerationQualificationPolicySource.RequireValidity(ObservedAt, ValidUntil);
        ModerationQualificationPolicySource.RequireValidity(ProcedureAdoptedAt, ProcedureAdoptionValidUntil);
        var identities = new[] { SubjectHumanId, HostBindingId, TrustDomainId, SolutionId, InstanceId,
            TenantId, PolicyId, PublicationId, DefinitionId, ProfileId, EpochId, ProcedureSourceId,
            ProcedureAdoptionId, ProcedureSource.ChildAgeSourceId, ProcedureSource.ChildAgeAdoptionId };
        if (identities.Any(value => value == Guid.Empty) || identities.Contains(Id)
            || identities.Contains(ObservationStamp) || Id == ObservationStamp
            || new[] { ProcedureSource.Id, ProcedureSource.ChildAgeSourceId, ProcedureSource.ChildAgeAdoptionId,
                HostBindingId, TrustDomainId, TenantId, PolicyId, PublicationId, DefinitionId, ProfileId }
                .Contains(ProcedureAdoptionId)
            || SubjectLoginId == Guid.Empty || SubjectLoginId == Id || SubjectLoginId == ObservationStamp
            || Revision != 1 || Generation is < 1 or long.MaxValue || ObservationSequence != Generation
            || EpochRevision is < 1 or long.MaxValue || EpochRevision < ProcedureAdoptionWriterGeneration
            || JurisdictionKind != (int)ModerationScopeKind.Instance && JurisdictionKind != (int)ModerationScopeKind.Tenant
            || PolicyRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || HostBindingId != ProcedureSource.HostBindingId || TrustDomainId != ProcedureSource.TrustDomainId
            || TenantId != ProcedureSource.TenantId || PolicyId != ProcedureSource.PolicyId
            || PolicyRevision != ProcedureSource.PolicyRevision || PublicationId != ProcedureSource.PublicationId
            || DefinitionId != ProcedureSource.DefinitionId || ProfileId != ProcedureSource.ProfileId
            || ModeCode != ProcedureSource.ModeCode
            || ProcedureSourceId != ProcedureSource.Id || ProcedureSourceRevision != 1
            || ProcedureSourceRevision != ProcedureSource.Revision
            || ProcedureSourceWriterGeneration != ProcedureSource.WriterGeneration
            || ProcedureAdoptionRevision != 1 || ProcedureAdoptionWriterGeneration <= ProcedureSourceWriterGeneration
            || ProcedureAdoptionWriterGeneration == long.MaxValue
            || ProcedureAdoptedByHumanId != ProcedureSource.ReviewerHumanId
            || ProcedureAdoptedByHumanId == ProcedureSource.RecordedByHumanId
            || ProcedureAdoptedByHumanId == ProcedureSource.OwnerControllerHumanId
            || ProcedureSource.OwnerControllerHumanId == ProcedureSource.ReviewerControllerHumanId
            || ProcedureSource.Procedure.Purpose != ModerationIndividualPurpose.ProfileCompletion
            || !IsInstant(ObservedAt) || !IsInstant(ValidUntil)
            || !IsInstant(ProcedureAdoptedAt) || !IsInstant(ProcedureAdoptionValidUntil)
            || ProcedureAdoptedAt < ProcedureSource.RecordedAt || ProcedureAdoptionValidUntil > ProcedureSource.ValidUntil
            || ObservedAt < ProcedureAdoptedAt || ValidUntil > ProcedureAdoptionValidUntil
            || !ModerationDeploymentSource.Digest(OriginalProcedureDigest)
            || !ModerationDeploymentSource.Digest(OriginalPolicyDigest)
            || !ModerationDeploymentSource.Digest(OriginalSelectionDigest))
            throw new ArgumentException("Observation requires immutable original Human, scope, parents, own sequence and finite UTC coverage.");
    }

    /// <summary>
    /// Reconstructs only exact original metadata through Domain factories. This deliberately
    /// does not assert that a retained row is latest, live, authenticated or applicable.
    /// </summary>
    public ModerationIndividualBinding ToBinding()
    {
        ValidatePersistedState();
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(TrustDomainId), SolutionId), InstanceId);
        var jurisdiction = JurisdictionKind == (int)ModerationScopeKind.Tenant
            ? AuthorityScope.Tenant(instance, TenantId) : instance;
        return new(ModerationIndividualTarget.Human(SubjectReference.Create(
                ModerationSubjectKind.Human, SubjectHumanId, instance)),
            new ModerationAgePolicy(PolicyId, PolicyRevision, jurisdiction),
            ModerationIndividualPurpose.ProfileCompletion, Id, Revision, Generation,
            ObservationStamp, ObservationSequence, ObservedAt, ValidUntil);
    }

    public bool Covers(DateTimeOffset now)
    {
        ValidatePersistedState();
        return IsInstant(now) && ObservedAt <= now && now < ValidUntil;
    }

    internal bool Matches(ModerationProfileCollectionSubjectObservation other) =>
        ToBinding() == other.ToBinding() && SubjectLoginId == other.SubjectLoginId
        && HostBindingId == other.HostBindingId && TenantId == other.TenantId
        && EpochId == other.EpochId && EpochRevision == other.EpochRevision
        && PublicationId == other.PublicationId && DefinitionId == other.DefinitionId
        && ProfileId == other.ProfileId && ModeCode == other.ModeCode
        && ProcedureSourceId == other.ProcedureSourceId && ProcedureSourceRevision == other.ProcedureSourceRevision
        && ProcedureSourceWriterGeneration == other.ProcedureSourceWriterGeneration
        && ProcedureAdoptionId == other.ProcedureAdoptionId && ProcedureAdoptionRevision == other.ProcedureAdoptionRevision
        && ProcedureAdoptionWriterGeneration == other.ProcedureAdoptionWriterGeneration
        && ProcedureAdoptedByHumanId == other.ProcedureAdoptedByHumanId
        && ProcedureAdoptedAt == other.ProcedureAdoptedAt
        && ProcedureAdoptionValidUntil == other.ProcedureAdoptionValidUntil
        && OriginalProcedureDigest == other.OriginalProcedureDigest
        && OriginalPolicyDigest == other.OriginalPolicyDigest && OriginalSelectionDigest == other.OriginalSelectionDigest
        && ProcedureSource.Matches(other.ProcedureSource);

    private static bool IsInstant(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;

    public override string ToString() => nameof(ModerationProfileCollectionSubjectObservation);
}
