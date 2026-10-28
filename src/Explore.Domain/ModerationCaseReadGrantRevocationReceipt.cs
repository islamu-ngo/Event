namespace Explore.Domain;

/// <summary>Immutable administrative operation ownership; historical revocation is not read permission.</summary>
public sealed class ModerationCaseReadGrantRevocationReceipt
{
    private ModerationCaseReadGrantRevocationReceipt() { }
    public Guid Id { get; private set; }
    public Guid GrantId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public long ExpectedRevision { get; private set; }
    public Guid AdministratorUserId { get; private set; }
    public Guid AdministratorRoleGrantId { get; private set; }
    public bool AdministratorPlatformGrant { get; private set; }
    public DateTimeOffset RevokedAt { get; private set; }

    public static ModerationCaseReadGrantRevocationReceipt Record(
        Guid operationId, ModerationCaseReadGrant revoked, long expectedRevision,
        Guid administratorUserId, Guid administratorRoleGrantId, bool administratorPlatformGrant)
    {
        ModerationHostBinding.RequireUuidV7(operationId, nameof(operationId));
        ArgumentNullException.ThrowIfNull(revoked);
        if (expectedRevision != 1 || revoked.Revision != 2 || revoked.RevokedAt is not { } revokedAt
            || administratorUserId == Guid.Empty || administratorRoleGrantId == Guid.Empty)
            throw new ArgumentException("Revocation ownership requires the exact completed native transition and administrator.");
        return new()
        {
            Id = operationId,
            GrantId = revoked.Id,
            TenantId = revoked.TenantId,
            TrustDomainId = revoked.TrustDomainId,
            ExpectedRevision = expectedRevision,
            AdministratorUserId = administratorUserId,
            AdministratorRoleGrantId = administratorRoleGrantId,
            AdministratorPlatformGrant = administratorPlatformGrant,
            RevokedAt = revokedAt
        };
    }

    public override string ToString() => nameof(ModerationCaseReadGrantRevocationReceipt);
}
