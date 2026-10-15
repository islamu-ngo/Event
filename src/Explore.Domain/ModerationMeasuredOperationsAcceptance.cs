namespace Explore.Domain;

/// <summary>Independent acceptance of exact measured report contents, including gaps; never readiness.</summary>
public sealed class ModerationMeasuredOperationsAcceptance
{
    private ModerationMeasuredOperationsAcceptance() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long SourceWriterGeneration { get; private set; }
    public Guid AcceptedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? WithdrawnByHumanId { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public static ModerationMeasuredOperationsAcceptance Accept(Guid id, ModerationMeasuredOperationsSource source,
        Guid human, long generation, DateTimeOffset now, DateTimeOffset validUntil)
    {
        source.ValidatePersistedState();
        if (human != source.ReviewerHumanId || human == source.RecordedByHumanId
            || now < source.RecordedAt || validUntil > source.ValidUntil || generation <= source.WriterGeneration)
            throw new ArgumentException("Acceptance requires the independent nominated reviewer and original finite evidence.");
        var acceptance = new ModerationMeasuredOperationsAcceptance
        {
            Id = id, HostBindingId = source.HostBindingId, TrustDomainId = source.TrustDomainId, TenantId = source.TenantId,
            SourceId = source.Id, SourceRevision = source.Revision, SourceWriterGeneration = source.WriterGeneration,
            AcceptedByHumanId = human, WriterGeneration = generation, Revision = 1, AcceptedAt = now, ValidUntil = validUntil
        };
        acceptance.ValidatePersistedState();
        return acceptance;
    }

    public bool MatchesCurrent(ModerationMeasuredOperationsSource source, DateTimeOffset now) =>
        Revision == 1 && WithdrawnAt is null && SourceId == source.Id && SourceRevision == source.Revision
        && SourceWriterGeneration == source.WriterGeneration && HostBindingId == source.HostBindingId
        && TrustDomainId == source.TrustDomainId && TenantId == source.TenantId
        && AcceptedByHumanId == source.ReviewerHumanId && AcceptedByHumanId != source.RecordedByHumanId
        && source.RecordedAt <= AcceptedAt && now >= AcceptedAt && now < ValidUntil && ValidUntil <= source.ValidUntil;

    public void Withdraw(long expectedRevision, Guid human, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || WithdrawnAt is not null)
            throw new InvalidOperationException("Withdrawal requires the exact unwithdrawn acceptance.");
        if (human == Guid.Empty || now.Offset != TimeSpan.Zero || now < AcceptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Withdrawal requires current accountable non-regressing authority.");
        Revision = checked(Revision + 1);
        WithdrawnByHumanId = human;
        WithdrawnAt = now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, SourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(AcceptedAt, ValidUntil);
        if (Id == SourceId || SourceRevision != 1 || SourceWriterGeneration is < 1 or long.MaxValue
            || WriterGeneration <= SourceWriterGeneration || WriterGeneration == long.MaxValue || AcceptedByHumanId == Guid.Empty
            || !(Revision == 1 && WithdrawnAt is null && WithdrawnByHumanId is null
                || Revision == 2 && WithdrawnAt is { } withdrawn && WithdrawnByHumanId is { } human && human != Guid.Empty
                    && withdrawn >= AcceptedAt && withdrawn.Offset == TimeSpan.Zero && withdrawn != DateTimeOffset.MaxValue))
            throw new ArgumentException("Acceptance must retain original source provenance and terminal withdrawal.");
    }

    public override string ToString() => nameof(ModerationMeasuredOperationsAcceptance);
}
