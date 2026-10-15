namespace Explore.Domain;

/// <summary>
/// Immutable, owner-qualified meaning and floor of one taxonomy category version.
/// Lookup identity, semantic kind and policy coordinates are distinct from EventType.
/// This validates supplied review metadata, not taxonomy ownership, human/controller
/// identity, mandates, consent, source receipts or permissions. Native owners must
/// authenticate those facts and supply the current taxonomy on the original transaction.
/// Replacing or withdrawing a category removes this version from that current taxonomy;
/// retained material reviews never silently acquire its replacement meaning.
/// </summary>
public sealed record EventContentClassificationVersion
{
    public enum CategoryKind { General = 1, Mature = 2, Debate = 3, OtherReviewed = 4 }

    private EventContentClassificationVersion() { }

    public Guid Id { get; private init; }
    public Guid TaxonomyId { get; private init; }
    public int CategoryId { get; private init; }
    public long Revision { get; private init; }
    public string Code { get; private init; } = string.Empty;
    public CategoryKind Kind { get; private init; }
    public int MinimumAge { get; private init; }
    public string Meaning { get; private init; } = string.Empty;
    public string ReviewRationale { get; private init; } = string.Empty;
    public Guid HostBindingId { get; private init; }
    public Guid TrustDomainId { get; private init; }
    public Guid TenantId { get; private init; }
    public Guid PolicyId { get; private init; }
    public long PolicyRevision { get; private init; }
    public Guid PublicationId { get; private init; }
    public Guid DefinitionId { get; private init; }
    public Guid ProfileId { get; private init; }
    public int ModeCode { get; private init; }
    public string ProposedStateDigest { get; private init; } = string.Empty;
    public Guid SourceId { get; private init; }
    public long SourceRevision { get; private init; }
    public long SourceWriterGeneration { get; private init; }
    public Guid AdoptionId { get; private init; }
    public long AdoptionRevision { get; private init; }
    public long AdoptionWriterGeneration { get; private init; }
    public int AdoptedDebateMinimumAge { get; private init; }
    public Guid OwnerHumanId { get; private init; }
    public Guid OwnerControllerHumanId { get; private init; }
    public Guid ReviewerHumanId { get; private init; }
    public Guid ReviewerControllerHumanId { get; private init; }
    public long WriterGeneration { get; private init; }
    public DateTimeOffset RecordedAt { get; private init; }
    public DateTimeOffset ValidUntil { get; private init; }

    public static EventContentClassificationVersion Record(
        Guid id, Guid taxonomyId, int categoryId, long revision, string code, CategoryKind kind,
        int minimumAge, string meaning, string reviewRationale,
        ModerationChildAgePolicySource source, ModerationChildAgePolicyAdoption adoption,
        Guid ownerHumanId, Guid ownerControllerHumanId, Guid reviewerHumanId, Guid reviewerControllerHumanId,
        long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adoption);
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        ModerationHostBinding.RequireUuidV7(taxonomyId, nameof(taxonomyId));
        RequireIndependentReview(ownerHumanId, ownerControllerHumanId, reviewerHumanId, reviewerControllerHumanId);
        ModerationQualificationPolicySource.RequireValidity(now, validUntil);
        if (categoryId < 1 || revision is < 1 or long.MaxValue || !Enum.IsDefined(kind)
            || minimumAge is < 0 or > 130 || string.IsNullOrEmpty(code) || code.Length > 100
            || !char.IsAsciiLetterLower(code[0])
            || code.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character)
                && character is not '-' and not '.')
            || !ModerationChildAgePolicyTerms.Grounds(meaning) || !ModerationChildAgePolicyTerms.Grounds(reviewRationale)
            || !adoption.MatchesCurrent(source, now) || validUntil > adoption.ValidUntil
            || writerGeneration <= adoption.WriterGeneration || writerGeneration == long.MaxValue)
            throw new ArgumentException("Category metadata requires explicit versioned meaning, a bounded reviewed floor and current finite adopted policy coordinates.");

        return new()
        {
            Id = id, TaxonomyId = taxonomyId, CategoryId = categoryId, Revision = revision,
            Code = code, Kind = kind, MinimumAge = minimumAge, Meaning = meaning, ReviewRationale = reviewRationale,
            HostBindingId = source.HostBindingId, TrustDomainId = source.TrustDomainId, TenantId = source.TenantId,
            PolicyId = source.PolicyId, PolicyRevision = source.PolicyRevision, PublicationId = source.PublicationId,
            DefinitionId = source.DefinitionId, ProfileId = source.ProfileId, ModeCode = source.ModeCode,
            ProposedStateDigest = source.ProposedStateDigest, SourceId = source.Id, SourceRevision = source.Revision,
            SourceWriterGeneration = source.WriterGeneration, AdoptionId = adoption.Id,
            AdoptionRevision = adoption.Revision, AdoptionWriterGeneration = adoption.WriterGeneration,
            AdoptedDebateMinimumAge = source.DebateMinimumAge,
            OwnerHumanId = ownerHumanId, OwnerControllerHumanId = ownerControllerHumanId,
            ReviewerHumanId = reviewerHumanId, ReviewerControllerHumanId = reviewerControllerHumanId,
            WriterGeneration = writerGeneration, RecordedAt = now, ValidUntil = validUntil
        };
    }

    /// <summary>Exact current metadata equality and finite validity; never authenticated adoption or eligibility.</summary>
    public bool MatchesPolicy(ModerationChildAgePolicySource source, ModerationChildAgePolicyAdoption adoption,
        DateTimeOffset now) =>
        now.Offset == TimeSpan.Zero && now >= RecordedAt && now < ValidUntil
        && HostBindingId == source.HostBindingId && TrustDomainId == source.TrustDomainId && TenantId == source.TenantId
        && PolicyId == source.PolicyId && PolicyRevision == source.PolicyRevision && PublicationId == source.PublicationId
        && DefinitionId == source.DefinitionId && ProfileId == source.ProfileId && ModeCode == source.ModeCode
        && ProposedStateDigest == source.ProposedStateDigest && SourceId == source.Id && SourceRevision == source.Revision
        && SourceWriterGeneration == source.WriterGeneration && AdoptionId == adoption.Id && AdoptionRevision == adoption.Revision
        && AdoptionWriterGeneration == adoption.WriterGeneration && AdoptedDebateMinimumAge == source.DebateMinimumAge
        && adoption.MatchesCurrent(source, now) && RecordedAt >= adoption.AdoptedAt && ValidUntil <= adoption.ValidUntil;

    internal static void RequireIndependentReview(Guid owner, Guid ownerController, Guid reviewer, Guid reviewerController)
    {
        if (owner == Guid.Empty || ownerController == Guid.Empty || reviewer == Guid.Empty || reviewerController == Guid.Empty
            || owner == reviewer || ownerController == reviewerController || owner == reviewerController || reviewer == ownerController)
            throw new ArgumentException("Classification review requires accountable independent humans and canonical controllers.");
    }

    public override string ToString() => nameof(EventContentClassificationVersion);
}
