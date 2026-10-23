using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>Immutable substantive constitution and roster for one exact native publication/profile/mode.</summary>
public sealed class ModerationConstitutionSource
{
    private ModerationConstitutionSource() { }
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
    public ModerationScopeKind ScopeCeiling { get; private set; }
    public ModerationConstitutionEvidence Evidence { get; private set; }
    public ModerationConstitutionRemedies Remedies { get; private set; }
    public ModerationConstitutionIndependence Independence { get; private set; }
    public ModerationConstitutionExposure Exposure { get; private set; }
    public ModerationConstitutionOverload Overload { get; private set; }
    public int MinimumIndependentReviewers { get; private set; }
    public int ProtectedComplaintCalendarMonths { get; private set; }
    public long MaximumReviewIntervalTicks { get; private set; }
    public long EscalationAfterTicks { get; private set; }
    public string SupportRoute { get; private set; } = string.Empty;
    public string IndependentRedressRoute { get; private set; } = string.Empty;
    public Guid CoordinatorHumanId { get; private set; }
    public Guid FallbackHumanId { get; private set; }
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? OriginalEffectId { get; private set; }
    public string? OriginalSnapshotHash { get; private set; }
    public string? OriginalEffectBindingHash { get; private set; }
    public DateTimeOffset? OriginalCustodyUntil { get; private set; }
    public List<ModerationConstitutionReviewer> Reviewers { get; private set; } = [];

    public ModerationConstitutionTerms Terms => new(ScopeCeiling, Evidence, Remedies, Independence, Exposure, Overload,
        MinimumIndependentReviewers, ProtectedComplaintCalendarMonths, TimeSpan.FromTicks(MaximumReviewIntervalTicks),
        TimeSpan.FromTicks(EscalationAfterTicks), SupportRoute, IndependentRedressRoute, CoordinatorHumanId, FallbackHumanId);

    public static ModerationConstitutionSource Record(Guid id, Guid hostId, Guid trustId, Guid tenantId,
        Guid policyId, long policyRevision, Guid publicationId, Guid definitionId, Guid profileId, int modeCode,
        string proposedStateDigest, ModerationConstitutionTerms terms,
        IEnumerable<ModerationConstitutionReviewerTerms> reviewers, Guid humanId, long writerGeneration,
        DateTimeOffset now, DateTimeOffset validUntil, Guid? originalEffectId = null,
        string? originalSnapshotHash = null, string? originalEffectBindingHash = null,
        DateTimeOffset? originalCustodyUntil = null)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(reviewers);
        terms.Validate();
        var source = new ModerationConstitutionSource
        {
            Id = id,
            HostBindingId = hostId,
            TrustDomainId = trustId,
            TenantId = tenantId,
            PolicyId = policyId,
            PolicyRevision = policyRevision,
            PublicationId = publicationId,
            DefinitionId = definitionId,
            ProfileId = profileId,
            ModeCode = modeCode,
            ProposedStateDigest = proposedStateDigest,
            ScopeCeiling = terms.ScopeCeiling,
            Evidence = terms.Evidence,
            Remedies = terms.Remedies,
            Independence = terms.Independence,
            Exposure = terms.Exposure,
            Overload = terms.Overload,
            MinimumIndependentReviewers = terms.MinimumIndependentReviewers,
            ProtectedComplaintCalendarMonths = terms.ProtectedComplaintCalendarMonths,
            MaximumReviewIntervalTicks = terms.MaximumReviewInterval.Ticks,
            EscalationAfterTicks = terms.EscalationAfter.Ticks,
            SupportRoute = terms.SupportRoute,
            IndependentRedressRoute = terms.IndependentRedressRoute,
            CoordinatorHumanId = terms.CoordinatorHumanId,
            FallbackHumanId = terms.FallbackHumanId,
            RecordedByHumanId = humanId,
            WriterGeneration = writerGeneration,
            Revision = 1,
            RecordedAt = now,
            ValidUntil = validUntil,
            OriginalEffectId = originalEffectId,
            OriginalSnapshotHash = originalSnapshotHash,
            OriginalEffectBindingHash = originalEffectBindingHash,
            OriginalCustodyUntil = originalCustodyUntil,
            Reviewers = reviewers.Select((item, ordinal) => ModerationConstitutionReviewer.Capture(id, ordinal, item)).ToList()
        };
        source.ValidatePersistedState();
        return source;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, PolicyId, PublicationId, DefinitionId, ProfileId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        Terms.Validate();
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (OriginalEffectId is not null || OriginalSnapshotHash is not null
            || OriginalEffectBindingHash is not null || OriginalCustodyUntil is not null)
        {
            if (OriginalEffectId is not { } effect || OriginalSnapshotHash is null
                || OriginalEffectBindingHash is null || OriginalCustodyUntil is not { } custody
                || !Digest(OriginalSnapshotHash)
                || !Digest(OriginalEffectBindingHash)
                || ValidUntil > custody)
                throw new ArgumentException("Original constitution requires complete immutable purpose coordinates within original custody.");
            ModerationHostBinding.RequireUuidV7(effect, nameof(OriginalEffectId));
            ModerationQualificationPolicySource.RequireValidity(RecordedAt, custody);
        }
        if (Revision != 1 || PolicyRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || RecordedByHumanId == Guid.Empty || WriterGeneration is < 1 or long.MaxValue
            || !Digest(ProposedStateDigest)
            || Reviewers.Count < MinimumIndependentReviewers || Reviewers.Count > 32)
            throw new ArgumentException("Constitution requires exact native generations and sufficient independent reviewer coverage.");
        var controllers = new HashSet<Guid>();
        var accounts = new HashSet<Guid>();
        var availability = new HashSet<Guid>();
        for (var i = 0; i < Reviewers.Count; i++)
        {
            var reviewer = Reviewers[i];
            reviewer.ToTerms().Validate();
            if (reviewer.SourceId != Id || reviewer.Ordinal != i
                || !controllers.Add(reviewer.ControllerHumanId) || !accounts.Add(reviewer.ReviewerHumanId)
                || !availability.Add(reviewer.AvailabilityId) || reviewer.AvailabilityWriterGeneration >= WriterGeneration
                || reviewer.ReviewerHumanId == RecordedByHumanId || reviewer.ControllerHumanId == RecordedByHumanId)
                throw new ArgumentException("Reviewer qualification must be independently recorded for distinct controllers and exact availability.");
        }
        if (Reviewers.Any(a => Reviewers.Any(b => a.Ordinal != b.Ordinal
                && (a.ReviewerHumanId == b.ControllerHumanId || a.ControllerHumanId == b.ReviewerHumanId))))
            throw new ArgumentException("Aliases cannot supply independent reviewer capacity.");
    }

    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    public override string ToString() => nameof(ModerationConstitutionSource);
}

public sealed class ModerationConstitutionReviewer
{
    private ModerationConstitutionReviewer() { }
    public Guid SourceId { get; private set; }
    public int Ordinal { get; private set; }
    public Guid AvailabilityId { get; private set; }
    public long AvailabilityRevision { get; private set; }
    public long AvailabilityWriterGeneration { get; private set; }
    public Guid ReviewerHumanId { get; private set; }
    public Guid ControllerHumanId { get; private set; }
    public string IdentityBasis { get; private set; } = string.Empty;
    public string QualificationBasis { get; private set; } = string.Empty;
    public string IndependenceBasis { get; private set; } = string.Empty;
    internal static ModerationConstitutionReviewer Capture(Guid sourceId, int ordinal, ModerationConstitutionReviewerTerms terms)
    {
        terms.Validate();
        return new()
        {
            SourceId = sourceId,
            Ordinal = ordinal,
            AvailabilityId = terms.AvailabilityId,
            AvailabilityRevision = terms.AvailabilityRevision,
            AvailabilityWriterGeneration = terms.AvailabilityWriterGeneration,
            ReviewerHumanId = terms.ReviewerHumanId,
            ControllerHumanId = terms.ControllerHumanId,
            IdentityBasis = terms.IdentityBasis,
            QualificationBasis = terms.QualificationBasis,
            IndependenceBasis = terms.IndependenceBasis
        };
    }
    public ModerationConstitutionReviewerTerms ToTerms() => new(AvailabilityId, AvailabilityRevision, AvailabilityWriterGeneration,
        ReviewerHumanId, ControllerHumanId, IdentityBasis, QualificationBasis, IndependenceBasis);
    public override string ToString() => nameof(ModerationConstitutionReviewer);
}
