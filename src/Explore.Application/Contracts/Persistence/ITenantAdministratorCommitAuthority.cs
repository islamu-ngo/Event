namespace Explore.Application.Contracts.Persistence;

/// <summary>Holds current identity, active tenant membership and tenant-administrator grants through commit.</summary>
public interface ITenantAdministratorCommitAuthority
{
    /// <remarks>
    /// Use the authenticated canonical principal and the exact execution tenant.
    /// The selected scope must already carry the immutable HumanCommitSubject resolved at
    /// the trusted account boundary; the supplied principal cannot replace that subject.
    /// The caller owns the Serializable transaction and must reacquire authority on retry.
    /// </remarks>
    Task<bool> IsTenantAdministratorForCommitAsync(
        Guid principalId, Guid tenantId, CancellationToken cancellationToken);
}
