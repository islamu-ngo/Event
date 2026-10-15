namespace Explore.Domain;

/// <summary>
/// A separately issued, revocable case/material-specific read purpose. It is not a submit
/// receipt, a resource disclosure capability, a finding, or an assertion about the target.
/// </summary>
public sealed class ModerationCaseReadGrant
{
    private ModerationCaseReadGrant() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid CallerUserId { get; private set; }
    public Guid IssuerUserId { get; private set; }
    public Guid IssuerRoleGrantId { get; private set; }
    public bool IssuerPlatformGrant { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public long Revision { get; private set; }

    public static ModerationCaseReadGrant Grant(Guid id, Guid tenantId, Guid trustDomainId,
        Guid caseId, long caseRevision, Guid sourceId, Guid callerUserId, Guid issuerUserId, Guid issuerRoleGrantId,
        bool issuerPlatformGrant, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        if (tenantId == Guid.Empty || trustDomainId == Guid.Empty || caseId == Guid.Empty
            || caseRevision < 1 || sourceId == Guid.Empty || callerUserId == Guid.Empty || issuerUserId == Guid.Empty
            || issuerRoleGrantId == Guid.Empty || now == default || expiresAt <= now
            || expiresAt == DateTimeOffset.MaxValue)
            throw new ArgumentException("A read grant requires actual case, source, caller, issuer and finite validity.");
        return new()
        {
            Id = id, TenantId = tenantId, TrustDomainId = trustDomainId, CaseId = caseId, CaseRevision = caseRevision,
            SourceId = sourceId, CallerUserId = callerUserId, IssuerUserId = issuerUserId,
            IssuerRoleGrantId = issuerRoleGrantId, IssuerPlatformGrant = issuerPlatformGrant,
            IssuedAt = now, ExpiresAt = expiresAt, Revision = 1
        };
    }

    public bool IsCurrentAt(DateTimeOffset now) => RevokedAt is null && now >= IssuedAt && now < ExpiresAt;

    public void Revoke(long expectedRevision, DateTimeOffset now)
    {
        if (Revision != expectedRevision || now < IssuedAt || RevokedAt is not null)
            throw new InvalidOperationException("The read grant is no longer current.");
        var revision = checked(Revision + 1);
        RevokedAt = now;
        Revision = revision;
    }
}
