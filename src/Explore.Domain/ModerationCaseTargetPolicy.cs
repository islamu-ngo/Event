using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Explicit tenant adoption of finite, type-specific case-purpose receipt lifetimes.
/// The native administrator writer owns changes; no deployment default grants intake authority.
/// </summary>
public sealed class ModerationCaseTargetPolicy
{
    private ModerationCaseTargetPolicy() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public ModerationCaseTargetKind TargetKind { get; private set; }
    public long SubmissionLifetimeTicks { get; private set; }
    public long ReadLifetimeTicks { get; private set; }
    public TimeSpan SubmissionLifetime => TimeSpan.FromTicks(SubmissionLifetimeTicks);
    public TimeSpan ReadLifetime => TimeSpan.FromTicks(ReadLifetimeTicks);
    public bool Enabled { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public long Revision { get; private set; }

    public static ModerationCaseTargetPolicy Adopt(Guid id, Guid tenantId, Guid trustDomainId,
        ModerationCaseTargetKind kind, TimeSpan submissionLifetime, TimeSpan readLifetime, Guid changedBy)
    {
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        if (tenantId == Guid.Empty || trustDomainId == Guid.Empty || !Enum.IsDefined(kind))
            throw new ArgumentException("Native tenant ancestry and target kind are required.");
        var policy = new ModerationCaseTargetPolicy
        {
            Id = id,
            TenantId = tenantId,
            TrustDomainId = trustDomainId,
            TargetKind = kind
        };
        policy.Change(0, submissionLifetime, readLifetime, true, changedBy);
        return policy;
    }

    public void Change(long expectedRevision, TimeSpan submissionLifetime, TimeSpan readLifetime,
        bool enabled, Guid changedBy)
    {
        if (Revision != expectedRevision)
            throw new InvalidOperationException("The case-purpose lifetime policy changed.");
        if (submissionLifetime <= TimeSpan.Zero || readLifetime <= TimeSpan.Zero || changedBy == Guid.Empty
            || submissionLifetime > TimeSpan.FromDays(1) || readLifetime > TimeSpan.FromDays(1))
            throw new ArgumentException("Case-purpose tokens require positive lifetimes of at most one day.");
        var revision = checked(Revision + 1);
        SubmissionLifetimeTicks = submissionLifetime.Ticks;
        ReadLifetimeTicks = readLifetime.Ticks;
        Enabled = enabled;
        ChangedByUserId = changedBy;
        Revision = revision;
    }
}
