using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Revalidates persisted issuance authority and fences revocation within the caller-owned transaction.
/// </summary>
/// <remarks>
/// An earlier authorization check is not sufficient for either creation or receipt recovery.
/// The caller must acquire this authority inside its serializable issuance transaction before
/// accessing issuance state, and retain that transaction through commit.
/// </remarks>
public interface IExternalApiKeyIssuanceAuthority
{
    /// <summary>
    /// Fences the principal and applicable owner authority rows, then checks their current persisted state.
    /// </summary>
    /// <param name="principalId">The authenticated application user, not an identity supplied by the request body.</param>
    /// <param name="tenantId">
    /// The exact active, nonbypassed tenant scope; <see langword="null"/> only for instance-admin ownership.
    /// </param>
    /// <param name="ownerType">The ownership boundary whose membership and management authority must be checked.</param>
    /// <param name="ownerId">
    /// The principal for user or instance-admin ownership, the tenant for tenant ownership,
    /// or the organization or group whose placement and membership grant authority.
    /// </param>
    /// <param name="cancellationToken">Propagated to authority reads and fence acquisition; cancellation is not an authorization denial.</param>
    /// <returns>
    /// <see langword="true"/> when the exact principal, scope and owner have current persisted authority;
    /// <see langword="false"/> for invalid scope or absent, deleted, suspended or revoked authority.
    /// </returns>
    /// <remarks>
    /// This operation neither starts nor commits the transaction. Relational fences require an active
    /// transaction and remain effective until it ends. Recovery after an ambiguous commit must
    /// reacquire authority in a new transaction; an earlier successful result is not reusable.
    /// </remarks>
    Task<bool> IsAuthorizedForCommitAsync(
        Guid principalId,
        Guid? tenantId,
        ExternalApiKeyOwnerType ownerType,
        Guid ownerId,
        CancellationToken cancellationToken);
}
