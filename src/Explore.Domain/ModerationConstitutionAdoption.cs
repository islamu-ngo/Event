namespace Explore.Domain;

/// <summary>Independent finite adoption of the exact retained substantive constitution; withdrawal is terminal.</summary>
public sealed class ModerationConstitutionAdoption
{
    private ModerationConstitutionAdoption() { }
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

    public static ModerationConstitutionAdoption Adopt(Guid id, ModerationConstitutionSource source,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        source.ValidatePersistedState();
        if (humanId == source.RecordedByHumanId || now < source.RecordedAt || validUntil > source.ValidUntil
            || writerGeneration <= source.WriterGeneration
            || source.Reviewers.Any(row => row.ReviewerHumanId == humanId || row.ControllerHumanId == humanId))
            throw new ArgumentException("Constitution adoption requires independent current authority and exact finite source coverage.");
        var adoption = new ModerationConstitutionAdoption
        {
            Id = id, HostBindingId = source.HostBindingId, TrustDomainId = source.TrustDomainId, TenantId = source.TenantId,
            SourceId = source.Id, SourceRevision = source.Revision, SourceWriterGeneration = source.WriterGeneration,
            AdoptedByHumanId = humanId, WriterGeneration = writerGeneration, Revision = 1, AdoptedAt = now, ValidUntil = validUntil
        };
        adoption.ValidatePersistedState();
        return adoption;
    }

    public bool MatchesCurrent(ModerationConstitutionSource source, DateTimeOffset now) =>
        WithdrawnAt is null && Revision == 1 && SourceId == source.Id && SourceRevision == source.Revision
        && SourceWriterGeneration == source.WriterGeneration && HostBindingId == source.HostBindingId
        && TrustDomainId == source.TrustDomainId && TenantId == source.TenantId && AdoptedByHumanId != source.RecordedByHumanId
        && !source.Reviewers.Any(row => row.ReviewerHumanId == AdoptedByHumanId || row.ControllerHumanId == AdoptedByHumanId)
        && source.RecordedAt <= AdoptedAt && now >= AdoptedAt && now < ValidUntil && ValidUntil <= source.ValidUntil;

    public void Withdraw(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || WithdrawnAt is not null)
            throw new InvalidOperationException("Constitution withdrawal requires the exact live adoption revision.");
        if (humanId == Guid.Empty || now.Offset != TimeSpan.Zero || now < AdoptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Withdrawal requires accountable non-regressing native authority.");
        Revision = checked(Revision + 1);
        WithdrawnByHumanId = humanId;
        WithdrawnAt = now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, SourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (Id == SourceId || SourceRevision != 1 || SourceWriterGeneration is < 1 or long.MaxValue
            || WriterGeneration <= SourceWriterGeneration || WriterGeneration == long.MaxValue || AdoptedByHumanId == Guid.Empty)
            throw new ArgumentException("Adoption requires a distinct identity and exact original source generation.");
        ModerationQualificationPolicySource.RequireValidity(AdoptedAt, ValidUntil);
        if (!(Revision == 1 && WithdrawnAt is null && WithdrawnByHumanId is null
            || Revision == 2 && WithdrawnAt is { } withdrawn && WithdrawnByHumanId is { } human && human != Guid.Empty
                && withdrawn >= AdoptedAt && withdrawn.Offset == TimeSpan.Zero && withdrawn != DateTimeOffset.MaxValue))
            throw new ArgumentException("Constitution adoption withdrawal state is inconsistent.");
    }
    public override string ToString() => nameof(ModerationConstitutionAdoption);
}
