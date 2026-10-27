namespace Explore.Domain;

public enum ModerationClassificationMaterialEnd { Withdrawn = 1, Corrected = 2 }

/// <summary>Independent finite acceptance with accountable terminal ending, including after expiry.</summary>
public sealed class ModerationClassificationMaterialReview
{
    private ModerationClassificationMaterialReview() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long SourceWriterGeneration { get; private set; }
    public Guid ReviewerHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public ModerationClassificationMaterialEnd? EndKind { get; private set; }
    public Guid? EndedByHumanId { get; private set; }
    public long? EndWriterGeneration { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? EndGrounds { get; private set; }

    public static ModerationClassificationMaterialReview Accept(Guid id, ModerationClassificationMaterialSource source,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        source.ValidatePersistedState();
        if (humanId != source.ReviewerHumanId || humanId == source.OwnerHumanId || now < source.RecordedAt
            || validUntil > source.ValidUntil || writerGeneration <= source.WriterGeneration)
            throw new ArgumentException("Only the nominated independent reviewer can accept this exact finite material proposal.");
        var review = new ModerationClassificationMaterialReview
        {
            Id = id,
            HostBindingId = source.HostBindingId,
            TrustDomainId = source.TrustDomainId,
            TenantId = source.TenantId,
            SourceId = source.Id,
            SourceRevision = source.Revision,
            SourceWriterGeneration = source.WriterGeneration,
            ReviewerHumanId = humanId,
            WriterGeneration = writerGeneration,
            Revision = 1,
            AcceptedAt = now,
            ValidUntil = validUntil
        };
        review.ValidatePersistedState();
        return review;
    }

    public bool MatchesCurrent(ModerationClassificationMaterialSource source, DateTimeOffset now) =>
        Revision == 1 && EndKind is null && SourceId == source.Id && SourceRevision == source.Revision
        && SourceWriterGeneration == source.WriterGeneration && HostBindingId == source.HostBindingId
        && TrustDomainId == source.TrustDomainId && TenantId == source.TenantId
        && ReviewerHumanId == source.ReviewerHumanId && ReviewerHumanId != source.OwnerHumanId
        && now.Offset == TimeSpan.Zero && AcceptedAt >= source.RecordedAt && now >= AcceptedAt
        && now < ValidUntil && ValidUntil <= source.ValidUntil;

    public void End(long expectedRevision, ModerationClassificationMaterialEnd ending, Guid humanId,
        long writerGeneration, string grounds, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || EndKind is not null)
            throw new InvalidOperationException("Ending requires the exact unended material review.");
        if (!Enum.IsDefined(ending) || humanId == Guid.Empty || writerGeneration <= WriterGeneration
            || writerGeneration == long.MaxValue || !ModerationChildAgePolicyTerms.Grounds(grounds)
            || now.Offset != TimeSpan.Zero || now < AcceptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Ending requires accountable current authority, substantive grounds and non-regressing time.");
        Revision = checked(Revision + 1);
        EndKind = ending;
        EndedByHumanId = humanId;
        EndWriterGeneration = writerGeneration;
        EndGrounds = grounds;
        EndedAt = now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, SourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(AcceptedAt, ValidUntil);
        if (Id == SourceId || SourceRevision != 1 || SourceWriterGeneration < 1
            || WriterGeneration <= SourceWriterGeneration || WriterGeneration == long.MaxValue || ReviewerHumanId == Guid.Empty
            || !(Revision == 1 && EndKind is null && EndedByHumanId is null && EndWriterGeneration is null && EndedAt is null && EndGrounds is null
                || Revision == 2 && EndKind is { } kind && Enum.IsDefined(kind)
                    && EndedByHumanId is { } human && human != Guid.Empty
                    && EndWriterGeneration > WriterGeneration && EndWriterGeneration != long.MaxValue
                    && EndedAt is { } at && at.Offset == TimeSpan.Zero && at >= AcceptedAt && at != DateTimeOffset.MaxValue
                    && ModerationChildAgePolicyTerms.Grounds(EndGrounds!)))
            throw new ArgumentException("Material acceptance requires immutable birth and consistent terminal provenance.");
    }

    public override string ToString() => nameof(ModerationClassificationMaterialReview);
}
