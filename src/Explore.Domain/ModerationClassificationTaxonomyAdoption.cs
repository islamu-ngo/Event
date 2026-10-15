namespace Explore.Domain;

/// <summary>Actual independent taxonomy acceptance; terminal withdrawal preserves immutable birth ownership.</summary>
public sealed class ModerationClassificationTaxonomyAdoption
{
    private ModerationClassificationTaxonomyAdoption() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long SourceWriterGeneration { get; private set; }
    public Guid AdoptedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset AdoptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public static ModerationClassificationTaxonomyAdoption Adopt(Guid id, ModerationClassificationTaxonomySource source,
        Guid human, long generation, DateTimeOffset now, DateTimeOffset until)
    {
        source.ValidatePersistedState();
        if (human != source.ReviewerHumanId || human == source.OwnerHumanId || now < source.RecordedAt
            || until > source.ValidUntil || generation <= source.WriterGeneration)
            throw new ArgumentException("Only the nominated independent reviewer can accept the exact finite taxonomy.");
        var result = new ModerationClassificationTaxonomyAdoption
        {
            Id = id, HostBindingId = source.HostBindingId, TrustDomainId = source.TrustDomainId, TenantId = source.TenantId,
            SourceId = source.Id, SourceRevision = source.Revision, SourceWriterGeneration = source.WriterGeneration,
            AdoptedByHumanId = human, WriterGeneration = generation, Revision = 1, AdoptedAt = now, ValidUntil = until
        };
        result.ValidatePersistedState();
        return result;
    }

    public bool MatchesCurrent(ModerationClassificationTaxonomySource source, DateTimeOffset now) =>
        Revision == 1 && WithdrawnAt is null && SourceId == source.Id && SourceRevision == source.Revision
        && SourceWriterGeneration == source.WriterGeneration && HostBindingId == source.HostBindingId
        && TrustDomainId == source.TrustDomainId && TenantId == source.TenantId
        && AdoptedByHumanId == source.ReviewerHumanId && AdoptedByHumanId != source.OwnerHumanId
        && now.Offset == TimeSpan.Zero && source.RecordedAt <= AdoptedAt && now >= AdoptedAt && now < ValidUntil
        && ValidUntil <= source.ValidUntil;

    public void Withdraw(long expectedRevision, Guid human, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || WithdrawnAt is not null)
            throw new InvalidOperationException("Taxonomy withdrawal requires the exact unwithdrawn adoption.");
        if (human == Guid.Empty || now.Offset != TimeSpan.Zero || now < AdoptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Withdrawal requires accountable non-regressing current authority.");
        Revision = checked(Revision + 1);
        WithdrawnByHumanId = human;
        WithdrawnAt = now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, SourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(AdoptedAt, ValidUntil);
        if (Id == SourceId || SourceRevision != 1 || SourceWriterGeneration is < 1 or long.MaxValue
            || WriterGeneration <= SourceWriterGeneration || WriterGeneration == long.MaxValue || AdoptedByHumanId == Guid.Empty
            || !(Revision == 1 && WithdrawnAt is null && WithdrawnByHumanId is null
                || Revision == 2 && WithdrawnAt is { } at && WithdrawnByHumanId is { } human && human != Guid.Empty
                    && at >= AdoptedAt && at.Offset == TimeSpan.Zero && at != DateTimeOffset.MaxValue))
            throw new ArgumentException("Taxonomy adoption requires immutable provenance and consistent terminal withdrawal.");
    }

    public override string ToString() => nameof(ModerationClassificationTaxonomyAdoption);
}
