namespace Explore.Domain;

/// <summary>Separately retained approval of one exact procedural source generation, not permission to impose restrictions.</summary>
public sealed class ModerationQualificationPolicyAdoption
{
    private ModerationQualificationPolicyAdoption() { }

    public Guid Id { get; private set; }
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long WriterGeneration { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public long MaximumReviewerLifetimeTicks { get; private set; }
    public long MaximumUrgencyLifetimeTicks { get; private set; }
    public TimeSpan MaximumReviewerLifetime => TimeSpan.FromTicks(MaximumReviewerLifetimeTicks);
    public TimeSpan MaximumUrgencyLifetime => TimeSpan.FromTicks(MaximumUrgencyLifetimeTicks);
    public Guid AdoptedByHumanId { get; private set; }
    public DateTimeOffset AdoptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public long Revision { get; private set; }
    public Guid? RevokedByHumanId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static ModerationQualificationPolicyAdoption Adopt(
        Guid id, ModerationQualificationPolicySource source, Guid adoptedByHumanId,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.ValidatePersistedState();
        if (now < source.PublishedAt || now >= source.ValidUntil || validUntil > source.ValidUntil)
            throw new ArgumentException("Adoption requires the current finite source and cannot outlive it.");
        var adoption = new ModerationQualificationPolicyAdoption
        {
            Id = id, SourceId = source.Id, SourceRevision = source.Revision, WriterGeneration = source.WriterGeneration,
            HostBindingId = source.HostBindingId, TrustDomainId = source.TrustDomainId, TenantId = source.TenantId,
            CaseId = source.CaseId, CaseRevision = source.CaseRevision,
            MaximumReviewerLifetimeTicks = source.MaximumReviewerLifetimeTicks,
            MaximumUrgencyLifetimeTicks = source.MaximumUrgencyLifetimeTicks,
            AdoptedByHumanId = adoptedByHumanId, AdoptedAt = now.ToUniversalTime(),
            ValidUntil = validUntil.ToUniversalTime(), Revision = 1
        };
        adoption.ValidatePersistedState();
        return adoption;
    }

    public void Revoke(long expectedRevision, Guid revokedByHumanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || RevokedAt is not null)
            throw new InvalidOperationException("Revocation requires an unrevoked adoption at its current revision.");
        now = now.ToUniversalTime();
        if (revokedByHumanId == Guid.Empty || now < AdoptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Revocation requires a native human and non-regressing finite instant.");
        RevokedByHumanId = revokedByHumanId;
        RevokedAt = now;
        Revision = checked(Revision + 1);
    }

    /// <summary>Pure scalar comparison; current human authority and transaction-owned fences remain native reader responsibilities.</summary>
    public bool MatchesCurrent(ModerationQualificationPolicySource source, long caseRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidatePersistedState();
        source.ValidatePersistedState();
        return RevokedAt is null && now >= AdoptedAt && now < ValidUntil
            && now >= source.PublishedAt && now < source.ValidUntil
            && SourceId == source.Id && SourceRevision == source.Revision && WriterGeneration == source.WriterGeneration
            && HostBindingId == source.HostBindingId && TrustDomainId == source.TrustDomainId
            && TenantId == source.TenantId && CaseId == source.CaseId
            && CaseRevision == source.CaseRevision && CaseRevision == caseRevision
            && MaximumReviewerLifetimeTicks == source.MaximumReviewerLifetimeTicks
            && MaximumUrgencyLifetimeTicks == source.MaximumUrgencyLifetimeTicks;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, SourceId, HostBindingId, TrustDomainId, TenantId, CaseId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (Id == SourceId || SourceRevision < 1 || WriterGeneration < 1 || CaseRevision < 1
            || AdoptedByHumanId == Guid.Empty
            || MaximumReviewerLifetimeTicks <= 0 || MaximumReviewerLifetimeTicks == long.MaxValue
            || MaximumUrgencyLifetimeTicks <= 0 || MaximumUrgencyLifetimeTicks == long.MaxValue)
            throw new ArgumentException("Adoption requires distinct identity and the exact explicit source generation and limits.");
        ModerationQualificationPolicySource.RequireValidity(AdoptedAt, ValidUntil);
        if (!(Revision == 1 && RevokedAt is null && RevokedByHumanId is null
            || Revision == 2 && RevokedAt is { } revoked && RevokedByHumanId is { } human && human != Guid.Empty
                && revoked >= AdoptedAt && revoked != DateTimeOffset.MaxValue && revoked.Offset == TimeSpan.Zero))
            throw new ArgumentException("Adoption revocation state is inconsistent.");
    }

    public override string ToString() => nameof(ModerationQualificationPolicyAdoption);
}
