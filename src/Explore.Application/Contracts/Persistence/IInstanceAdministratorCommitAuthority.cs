namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Rechecks the authenticated principal's persisted instance-administrator authority,
/// holding principal, provider identity and administrator grant/role rows through commit.
/// </summary>
public interface IInstanceAdministratorCommitAuthority
{
    /// <remarks>
    /// The principal is resolved by the trusted authentication owner, never from a request body.
    /// Invoke in the same serializable transaction as the consequential action and retain
    /// the fences through commit. A fresh retry must reacquire all authority.
    /// </remarks>
    Task<bool> IsInstanceAdministratorForCommitAsync(
        Guid principalId, CancellationToken cancellationToken);
}
