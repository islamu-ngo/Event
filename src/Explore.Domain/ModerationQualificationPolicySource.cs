namespace Explore.Domain;

/// <summary>Current case-specific procedural limits. Publication alone is not adoption or deployment conformance.</summary>
public sealed class ModerationQualificationPolicySource
{
    private ModerationQualificationPolicySource() { }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public long Revision { get; private set; }
    public long WriterGeneration { get; private set; }
    public long MaximumReviewerLifetimeTicks { get; private set; }
    public long MaximumUrgencyLifetimeTicks { get; private set; }
    public TimeSpan MaximumReviewerLifetime => TimeSpan.FromTicks(MaximumReviewerLifetimeTicks);
    public TimeSpan MaximumUrgencyLifetime => TimeSpan.FromTicks(MaximumUrgencyLifetimeTicks);
    public Guid PublishedByHumanId { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public static ModerationQualificationPolicySource Publish(
        Guid id, Guid hostBindingId, Guid trustDomainId, Guid tenantId, Guid caseId,
        long caseRevision, long writerGeneration, TimeSpan maximumReviewerLifetime,
        TimeSpan maximumUrgencyLifetime, Guid publishedByHumanId, DateTimeOffset now, DateTimeOffset validUntil)
    {
        var source = new ModerationQualificationPolicySource
        {
            Id = id,
            HostBindingId = hostBindingId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            CaseId = caseId,
            CaseRevision = caseRevision,
            Revision = 1,
            WriterGeneration = writerGeneration,
            MaximumReviewerLifetimeTicks = maximumReviewerLifetime.Ticks,
            MaximumUrgencyLifetimeTicks = maximumUrgencyLifetime.Ticks,
            PublishedByHumanId = publishedByHumanId,
            PublishedAt = now.ToUniversalTime(),
            ValidUntil = validUntil.ToUniversalTime()
        };
        source.ValidatePersistedState();
        return source;
    }

    /// <summary>Changes the current revision; previously retained adoption snapshots are never rewritten.</summary>
    public void Replace(
        long expectedRevision, long caseRevision, long writerGeneration, TimeSpan maximumReviewerLifetime,
        TimeSpan maximumUrgencyLifetime, Guid publishedByHumanId, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision)
            throw new InvalidOperationException("Policy replacement requires the current source revision.");
        if (writerGeneration <= WriterGeneration || now < PublishedAt || caseRevision < CaseRevision)
            throw new ArgumentException("Replacement requires a newer writer generation and non-regressing case and time.");
        var next = Publish(Id, HostBindingId, TrustDomainId, TenantId, CaseId, caseRevision, writerGeneration,
            maximumReviewerLifetime, maximumUrgencyLifetime, publishedByHumanId, now, validUntil);
        var revision = checked(Revision + 1);
        CaseRevision = next.CaseRevision;
        WriterGeneration = next.WriterGeneration;
        MaximumReviewerLifetimeTicks = next.MaximumReviewerLifetimeTicks;
        MaximumUrgencyLifetimeTicks = next.MaximumUrgencyLifetimeTicks;
        PublishedByHumanId = next.PublishedByHumanId;
        PublishedAt = next.PublishedAt;
        ValidUntil = next.ValidUntil;
        Revision = revision;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, CaseId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (CaseRevision < 1 || Revision < 1 || WriterGeneration < 1 || PublishedByHumanId == Guid.Empty
            || MaximumReviewerLifetimeTicks <= 0 || MaximumReviewerLifetimeTicks == long.MaxValue
            || MaximumUrgencyLifetimeTicks <= 0 || MaximumUrgencyLifetimeTicks == long.MaxValue)
            throw new ArgumentException("A policy source requires exact generations, a native human and explicit finite positive limits.");
        RequireValidity(PublishedAt, ValidUntil);
    }

    internal static void RequireValidity(DateTimeOffset from, DateTimeOffset until)
    {
        if (from == DateTimeOffset.MinValue || from == DateTimeOffset.MaxValue || from.Offset != TimeSpan.Zero
            || until == DateTimeOffset.MaxValue || until.Offset != TimeSpan.Zero || until <= from)
            throw new ArgumentException("Procedural governance requires a recorded UTC instant and future finite expiry.");
    }

    public override string ToString() => nameof(ModerationQualificationPolicySource);
}
