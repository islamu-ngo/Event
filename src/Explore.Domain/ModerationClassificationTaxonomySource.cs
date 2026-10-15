using System.Collections.Immutable;

namespace Explore.Domain;

/// <summary>
/// Immutable finite taxonomy proposal. The latest logical taxonomy generation replaces the
/// complete inventory, including removals. Recording is not reviewer acceptance or material approval.
/// </summary>
public sealed class ModerationClassificationTaxonomySource
{
    private ModerationClassificationTaxonomySource() { }
    private readonly List<ModerationClassificationTaxonomyCategory> _categories = [];
    public IReadOnlyList<ModerationClassificationTaxonomyCategory> Categories => _categories.AsReadOnly();
    public Guid Id { get; private set; }
    public Guid TaxonomyId { get; private set; }
    public long TaxonomyRevision { get; private set; }
    public long PreviousGeneration { get; private set; }
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
    public long ChildAgeSourceGeneration { get; private set; }
    public Guid ChildAgeAdoptionId { get; private set; }
    public long ChildAgeAdoptionRevision { get; private set; }
    public long ChildAgeAdoptionGeneration { get; private set; }
    public int AdoptedDebateMinimumAge { get; private set; }
    public Guid ConstitutionSourceId { get; private set; }
    public long ConstitutionSourceRevision { get; private set; }
    public long ConstitutionSourceGeneration { get; private set; }
    public Guid ConstitutionAdoptionId { get; private set; }
    public long ConstitutionAdoptionRevision { get; private set; }
    public long ConstitutionAdoptionGeneration { get; private set; }
    public Guid OwnerHumanId { get; private set; }
    public Guid OwnerControllerHumanId { get; private set; }
    public Guid ReviewerHumanId { get; private set; }
    public Guid ReviewerControllerHumanId { get; private set; }
    public string OwnershipGrounds { get; private set; } = string.Empty;
    public string ReviewMandateGrounds { get; private set; } = string.Empty;
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public ModerationClassificationTaxonomyMandate Mandate => new(OwnerHumanId, OwnerControllerHumanId,
        ReviewerHumanId, ReviewerControllerHumanId, OwnershipGrounds, ReviewMandateGrounds);

    public static ModerationClassificationTaxonomySource Record(Guid id, Guid taxonomyId, long taxonomyRevision,
        long previousGeneration, ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption ageAdoption,
        ModerationConstitutionSource governance, ModerationConstitutionAdoption governanceAdoption,
        ModerationClassificationTaxonomyMandate mandate, IEnumerable<ModerationClassificationTaxonomyCategoryTerms> categories,
        long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(categories);
        age.ValidatePersistedState();
        ageAdoption.ValidatePersistedState();
        governance.ValidatePersistedState();
        governanceAdoption.ValidatePersistedState();
        mandate.Validate();
        var result = new ModerationClassificationTaxonomySource
        {
            Id = id, TaxonomyId = taxonomyId, TaxonomyRevision = taxonomyRevision, PreviousGeneration = previousGeneration,
            HostBindingId = age.HostBindingId, TrustDomainId = age.TrustDomainId, TenantId = age.TenantId,
            PolicyId = age.PolicyId, PolicyRevision = age.PolicyRevision, PublicationId = age.PublicationId,
            DefinitionId = age.DefinitionId, ProfileId = age.ProfileId, ModeCode = age.ModeCode,
            ProposedStateDigest = age.ProposedStateDigest,
            ChildAgeSourceId = age.Id, ChildAgeSourceRevision = age.Revision, ChildAgeSourceGeneration = age.WriterGeneration,
            ChildAgeAdoptionId = ageAdoption.Id, ChildAgeAdoptionRevision = ageAdoption.Revision,
            ChildAgeAdoptionGeneration = ageAdoption.WriterGeneration, AdoptedDebateMinimumAge = age.DebateMinimumAge,
            ConstitutionSourceId = governance.Id, ConstitutionSourceRevision = governance.Revision,
            ConstitutionSourceGeneration = governance.WriterGeneration, ConstitutionAdoptionId = governanceAdoption.Id,
            ConstitutionAdoptionRevision = governanceAdoption.Revision, ConstitutionAdoptionGeneration = governanceAdoption.WriterGeneration,
            OwnerHumanId = mandate.OwnerHumanId, OwnerControllerHumanId = mandate.OwnerControllerHumanId,
            ReviewerHumanId = mandate.ReviewerHumanId, ReviewerControllerHumanId = mandate.ReviewerControllerHumanId,
            OwnershipGrounds = mandate.OwnershipGrounds, ReviewMandateGrounds = mandate.ReviewMandateGrounds,
            WriterGeneration = writerGeneration, Revision = 1, RecordedAt = now, ValidUntil = validUntil
        };
        result._categories.AddRange(categories.Take(65).Select((item, index) =>
            ModerationClassificationTaxonomyCategory.Capture(id, index, item)));
        result.ValidatePersistedState();
        if (!result.MatchesPrerequisites(age, ageAdoption, governance, governanceAdoption, now))
            throw new ArgumentException("Taxonomy requires exact independently adopted governance and child-age sources.");
        return result;
    }

    public bool MatchesPrerequisites(ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption ageAdoption,
        ModerationConstitutionSource governance, ModerationConstitutionAdoption governanceAdoption, DateTimeOffset now) =>
        now.Offset == TimeSpan.Zero && now >= RecordedAt && now < ValidUntil
        && ChildAgeSourceId == age.Id && ChildAgeSourceRevision == age.Revision && ChildAgeSourceGeneration == age.WriterGeneration
        && ChildAgeAdoptionId == ageAdoption.Id && ChildAgeAdoptionRevision == ageAdoption.Revision
        && ChildAgeAdoptionGeneration == ageAdoption.WriterGeneration && AdoptedDebateMinimumAge == age.DebateMinimumAge
        && ConstitutionSourceId == governance.Id && ConstitutionSourceRevision == governance.Revision
        && ConstitutionSourceGeneration == governance.WriterGeneration && ConstitutionAdoptionId == governanceAdoption.Id
        && ConstitutionAdoptionRevision == governanceAdoption.Revision && ConstitutionAdoptionGeneration == governanceAdoption.WriterGeneration
        && HostBindingId == age.HostBindingId && HostBindingId == governance.HostBindingId
        && TrustDomainId == age.TrustDomainId && TrustDomainId == governance.TrustDomainId
        && TenantId == age.TenantId && TenantId == governance.TenantId
        && PolicyId == age.PolicyId && PolicyId == governance.PolicyId
        && PolicyRevision == age.PolicyRevision && PolicyRevision == governance.PolicyRevision
        && PublicationId == age.PublicationId && PublicationId == governance.PublicationId
        && DefinitionId == age.DefinitionId && DefinitionId == governance.DefinitionId
        && ProfileId == age.ProfileId && ProfileId == governance.ProfileId
        && ModeCode == age.ModeCode && ModeCode == governance.ModeCode
        && ProposedStateDigest == age.ProposedStateDigest && ProposedStateDigest == governance.ProposedStateDigest
        && governance.OriginalEffectId is null && governance.CoordinatorHumanId == OwnerHumanId
        && governance.Reviewers.Any(row => row.ReviewerHumanId == OwnerHumanId && row.ControllerHumanId == OwnerControllerHumanId)
        && governance.Reviewers.Any(row => row.ReviewerHumanId == ReviewerHumanId && row.ControllerHumanId == ReviewerControllerHumanId)
        && ageAdoption.MatchesCurrent(age, now) && governanceAdoption.MatchesCurrent(governance, now)
        && RecordedAt >= ageAdoption.AdoptedAt && RecordedAt >= governanceAdoption.AdoptedAt
        && ValidUntil <= ageAdoption.ValidUntil && ValidUntil <= governanceAdoption.ValidUntil;

    public ImmutableArray<EventContentClassificationVersion> Project(
        ModerationChildAgePolicySource age, ModerationChildAgePolicyAdoption adoption)
    {
        ValidatePersistedState();
        if (ChildAgeSourceId != age.Id || ChildAgeSourceRevision != age.Revision
            || ChildAgeSourceGeneration != age.WriterGeneration || ChildAgeAdoptionId != adoption.Id
            || ChildAgeAdoptionRevision != adoption.Revision || ChildAgeAdoptionGeneration != adoption.WriterGeneration
            || AdoptedDebateMinimumAge != age.DebateMinimumAge
            || HostBindingId != age.HostBindingId || TrustDomainId != age.TrustDomainId || TenantId != age.TenantId
            || PolicyId != age.PolicyId || PolicyRevision != age.PolicyRevision || PublicationId != age.PublicationId
            || DefinitionId != age.DefinitionId || ProfileId != age.ProfileId || ModeCode != age.ModeCode
            || ProposedStateDigest != age.ProposedStateDigest)
            throw new InvalidOperationException("Taxonomy projection requires its exact recorded child-age source and adoption.");
        return Categories.Select(row => EventContentClassificationVersion.Record(row.Id, TaxonomyId, row.CategoryId,
            TaxonomyRevision, row.Code, row.Kind, row.MinimumAge, row.Meaning, row.ReviewRationale, age, adoption,
            OwnerHumanId, OwnerControllerHumanId, ReviewerHumanId, ReviewerControllerHumanId,
            WriterGeneration, RecordedAt, ValidUntil)).ToImmutableArray();
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, TaxonomyId, HostBindingId, TrustDomainId, TenantId, PolicyId, PublicationId,
            DefinitionId, ProfileId, ChildAgeSourceId, ChildAgeAdoptionId, ConstitutionSourceId, ConstitutionAdoptionId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        Mandate.Validate();
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (Revision != 1 || TaxonomyRevision is < 1 or long.MaxValue || PreviousGeneration < 0
            || (TaxonomyRevision == 1) != (PreviousGeneration == 0)
            || PolicyRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || ChildAgeSourceRevision != 1 || ChildAgeAdoptionRevision != 1
            || ConstitutionSourceRevision != 1 || ConstitutionAdoptionRevision != 1
            || ChildAgeSourceGeneration < 1 || ChildAgeAdoptionGeneration <= ChildAgeSourceGeneration
            || ConstitutionSourceGeneration < 1 || ConstitutionAdoptionGeneration <= ConstitutionSourceGeneration
            || WriterGeneration <= Math.Max(PreviousGeneration, Math.Max(ChildAgeAdoptionGeneration, ConstitutionAdoptionGeneration))
            || WriterGeneration == long.MaxValue || !ModerationDeploymentSource.Digest(ProposedStateDigest)
            || Categories.Count is < 1 or > 64 || Categories.Select(row => row.Id).Distinct().Count() != Categories.Count
            || Categories.Select(row => row.CategoryId).Distinct().Count() != Categories.Count
            || Categories.Select(row => row.Code).Distinct(StringComparer.Ordinal).Count() != Categories.Count)
            throw new ArgumentException("Taxonomy requires finite unique category slots and exact immutable source generations.");
        for (var index = 0; index < Categories.Count; index++)
        {
            var category = Categories[index];
            category.Terms.Validate(AdoptedDebateMinimumAge);
            if (category.SourceId != Id || category.Ordinal != index)
                throw new ArgumentException("Taxonomy category is not owned by its exact proposal.");
        }
    }

    public override string ToString() => nameof(ModerationClassificationTaxonomySource);
}
