namespace Explore.Domain;

/// <summary>
/// Immutable proposal recorded by the exact parent collection owner, not native qualification,
/// reviewer acceptance or permission to collect PII. Equality checks bind metadata; they do not
/// authenticate people. Native owners authenticate canonical identity, administrator and parent
/// receipts, substantive mandates and current policy/publication/profile/mode under write fences.
/// Latest native generations, retirement and supersession cannot be discovered from retained
/// objects; callers must supply current parents and never fall back to an older accepted source.
/// </summary>
public sealed class ModerationProfileCollectionProcedureSource
{
    private ModerationProfileCollectionProcedureSource() { }

    private ModerationProfileCollectionProcedureSource(Guid id, ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption adoption, ModerationProfileCollectionProcedure procedure,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        Id = id;
        HostBindingId = age.HostBindingId;
        TrustDomainId = age.TrustDomainId;
        TenantId = age.TenantId;
        PolicyId = age.PolicyId;
        PolicyRevision = age.PolicyRevision;
        PublicationId = age.PublicationId;
        DefinitionId = age.DefinitionId;
        ProfileId = age.ProfileId;
        ModeCode = age.ModeCode;
        ProposedStateDigest = age.ProposedStateDigest;
        ChildAgeSourceId = age.Id;
        ChildAgeSourceRevision = age.Revision;
        ChildAgeSourceWriterGeneration = age.WriterGeneration;
        ChildAgeRecordedByHumanId = age.RecordedByHumanId;
        ChildAgeRecordedAt = age.RecordedAt;
        ChildAgeValidUntil = age.ValidUntil;
        ChildAgeTerms = age.Terms;
        ChildAgeOwnership = age.Ownership;
        ChildAgeAdoptionId = adoption.Id;
        ChildAgeAdoptionRevision = adoption.Revision;
        ChildAgeAdoptionWriterGeneration = adoption.WriterGeneration;
        ChildAgeAdoptedAt = adoption.AdoptedAt;
        ChildAgeAdoptionValidUntil = adoption.ValidUntil;
        Procedure = procedure;
        RecordedByHumanId = humanId;
        WriterGeneration = writerGeneration;
        Revision = 1;
        RecordedAt = now;
        ValidUntil = validUntil;
    }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int ModeCode { get; private set; }
    public string ProposedStateDigest { get; private set; } = string.Empty;
    public Guid ChildAgeSourceId { get; private set; }
    public long ChildAgeSourceRevision { get; private set; }
    public long ChildAgeSourceWriterGeneration { get; private set; }
    public Guid ChildAgeRecordedByHumanId { get; private set; }
    public DateTimeOffset ChildAgeRecordedAt { get; private set; }
    public DateTimeOffset ChildAgeValidUntil { get; private set; }
    public ModerationChildAgePolicyTerms ChildAgeTerms { get; private set; } = null!;
    public ModerationChildAgePolicyOwnership ChildAgeOwnership { get; private set; } = null!;
    public Guid ChildAgeAdoptionId { get; private set; }
    public long ChildAgeAdoptionRevision { get; private set; }
    public long ChildAgeAdoptionWriterGeneration { get; private set; }
    public DateTimeOffset ChildAgeAdoptedAt { get; private set; }
    public DateTimeOffset ChildAgeAdoptionValidUntil { get; private set; }
    public Guid OwnerHumanId => ChildAgeOwnership.CollectionOwnerHumanId;
    public Guid OwnerControllerHumanId => ChildAgeOwnership.CollectionControllerHumanId;
    public Guid ReviewerHumanId => ChildAgeOwnership.ReviewerHumanId;
    public Guid ReviewerControllerHumanId => ChildAgeOwnership.ReviewerControllerHumanId;
    public ModerationProfileCollectionProcedure Procedure { get; private set; } = null!;
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (ChildAgeTerms is null || ChildAgeOwnership is null || Procedure is null
            || new[] { HostBindingId, TrustDomainId, TenantId, PolicyId, PublicationId, DefinitionId,
                ProfileId, ChildAgeSourceId, ChildAgeAdoptionId, ChildAgeRecordedByHumanId, RecordedByHumanId }
                .Any(id => id == Guid.Empty)
            || Revision != 1 || PolicyRevision < 1 || ModeCode is < 1 or > 2
            || ChildAgeSourceRevision != 1 || ChildAgeAdoptionRevision != 1
            || ChildAgeSourceWriterGeneration < 1
            || ChildAgeAdoptionWriterGeneration <= ChildAgeSourceWriterGeneration
            || WriterGeneration <= ChildAgeAdoptionWriterGeneration || WriterGeneration == long.MaxValue
            || RecordedByHumanId != OwnerHumanId || RecordedAt < ChildAgeRecordedAt
            || RecordedAt < ChildAgeAdoptedAt || ValidUntil > ChildAgeValidUntil
            || ValidUntil > ChildAgeAdoptionValidUntil || !ModerationDeploymentSource.Digest(ProposedStateDigest))
            throw new ArgumentException("Persisted profile procedure requires exact finite original parent and owner coordinates.");
        ChildAgeTerms.Validate();
        ChildAgeOwnership.Validate(ChildAgeRecordedByHumanId);
    }

    public static ModerationProfileCollectionProcedureSource Record(Guid id, ModerationChildAgePolicySource age,
        ModerationChildAgePolicyAdoption adoption, ModerationProfileCollectionProcedure procedure,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(adoption);
        ArgumentNullException.ThrowIfNull(procedure);
        age.ValidatePersistedState();
        adoption.ValidatePersistedState();
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        ModerationQualificationPolicySource.RequireValidity(now, validUntil);
        if (new[] { age.Id, adoption.Id, age.HostBindingId, age.TrustDomainId, age.TenantId,
                age.PolicyId, age.PublicationId, age.DefinitionId, age.ProfileId }.Contains(id)
            || humanId != age.CollectionOwnerHumanId
            || writerGeneration <= adoption.WriterGeneration || writerGeneration == long.MaxValue
            || !adoption.MatchesCurrent(age, now) || now < age.RecordedAt
            || validUntil > age.ValidUntil || validUntil > adoption.ValidUntil)
            throw new ArgumentException("Procedure recording requires distinct identity, nominated parent owner and current finite adopted source coverage.");
        var source = new ModerationProfileCollectionProcedureSource(
            id, age, adoption, procedure, humanId, writerGeneration, now, validUntil);
        source.ValidatePersistedState();
        return source;
    }

    /// <summary>Exact structural coverage only; current policy and latest source/adoption generations must be supplied by native owners.</summary>
    public bool MatchesCurrent(ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption adoption,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(adoption);
        return now.Offset == TimeSpan.Zero && now >= RecordedAt && now < ValidUntil
            && ChildAgeSourceId == age.Id && ChildAgeSourceRevision == age.Revision
            && ChildAgeSourceWriterGeneration == age.WriterGeneration
            && ChildAgeRecordedByHumanId == age.RecordedByHumanId && ChildAgeRecordedAt == age.RecordedAt
            && ChildAgeValidUntil == age.ValidUntil && ChildAgeTerms == age.Terms && ChildAgeOwnership == age.Ownership
            && HostBindingId == age.HostBindingId && TrustDomainId == age.TrustDomainId && TenantId == age.TenantId
            && PolicyId == age.PolicyId && PolicyRevision == age.PolicyRevision
            && PublicationId == age.PublicationId && DefinitionId == age.DefinitionId
            && ProfileId == age.ProfileId && ModeCode == age.ModeCode && ProposedStateDigest == age.ProposedStateDigest
            && ChildAgeAdoptionId == adoption.Id && ChildAgeAdoptionRevision == adoption.Revision
            && ChildAgeAdoptionWriterGeneration == adoption.WriterGeneration
            && ChildAgeAdoptedAt == adoption.AdoptedAt && ChildAgeAdoptionValidUntil == adoption.ValidUntil
            && adoption.MatchesCurrent(age, now) && RecordedAt >= adoption.AdoptedAt
            && ValidUntil <= age.ValidUntil && ValidUntil <= adoption.ValidUntil;
    }

    internal bool Matches(ModerationProfileCollectionProcedureSource other) =>
        Id == other.Id && Revision == other.Revision && WriterGeneration == other.WriterGeneration
        && HostBindingId == other.HostBindingId && TrustDomainId == other.TrustDomainId && TenantId == other.TenantId
        && PolicyId == other.PolicyId && PolicyRevision == other.PolicyRevision
        && PublicationId == other.PublicationId && DefinitionId == other.DefinitionId && ProfileId == other.ProfileId
        && ModeCode == other.ModeCode && ProposedStateDigest == other.ProposedStateDigest
        && ChildAgeSourceId == other.ChildAgeSourceId && ChildAgeSourceRevision == other.ChildAgeSourceRevision
        && ChildAgeSourceWriterGeneration == other.ChildAgeSourceWriterGeneration
        && ChildAgeRecordedByHumanId == other.ChildAgeRecordedByHumanId && ChildAgeRecordedAt == other.ChildAgeRecordedAt
        && ChildAgeValidUntil == other.ChildAgeValidUntil && ChildAgeTerms == other.ChildAgeTerms
        && ChildAgeOwnership == other.ChildAgeOwnership && ChildAgeAdoptionId == other.ChildAgeAdoptionId
        && ChildAgeAdoptionRevision == other.ChildAgeAdoptionRevision
        && ChildAgeAdoptionWriterGeneration == other.ChildAgeAdoptionWriterGeneration
        && ChildAgeAdoptedAt == other.ChildAgeAdoptedAt && ChildAgeAdoptionValidUntil == other.ChildAgeAdoptionValidUntil
        && RecordedByHumanId == other.RecordedByHumanId && RecordedAt == other.RecordedAt
        && ValidUntil == other.ValidUntil && Procedure.Matches(other.Procedure);

    public override string ToString() => nameof(ModerationProfileCollectionProcedureSource);
}
