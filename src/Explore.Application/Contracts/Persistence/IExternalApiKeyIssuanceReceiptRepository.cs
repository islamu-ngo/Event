using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Persists digest-only issuance evidence with its key and supports authorized, metadata-only recovery.
/// </summary>
/// <remarks>
/// The caller owns the serializable transaction and must revalidate current issuance authority before
/// reading or writing issuance state. Receipts survive independently of key availability and contain
/// no recoverable credential. Neither a receipt nor a recovered key permits replay of the original secret.
/// </remarks>
public interface IExternalApiKeyIssuanceReceiptRepository
{
    /// <summary>
    /// Requires an unowned transaction scope and no pending tracked writes before issuance begins.
    /// </summary>
    /// <param name="tenantId">
    /// The exact active, nonbypassed tenant scope to require, or <see langword="null"/> for platform issuance.
    /// </param>
    /// <remarks>
    /// Call before opening the issuance transaction. Unchanged tracked entities are permitted;
    /// pending changes are not, because saving the key and receipt would flush them too.
    /// A null scope does not validate platform-admin authority; the issuance authority must do so.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// An ambient or context transaction exists, tracked changes are pending, or a supplied tenant
    /// is empty, bypassed or different from the active tenant.
    /// </exception>
    void RequireCleanWriteScope(Guid? tenantId);

    /// <summary>
    /// Finds durable evidence for an operation in its exact tenant or platform boundary.
    /// </summary>
    /// <param name="operationFingerprint">The digest binding principal, tenant or global scope, owner and operation key.</param>
    /// <param name="tenantId">The exact receipt tenant, or <see langword="null"/> for a platform receipt.</param>
    /// <param name="cancellationToken">Propagated to the receipt lookup.</param>
    /// <returns>The matching receipt, or <see langword="null"/> when no receipt is visible in that boundary.</returns>
    /// <remarks>
    /// The caller compares the receipt's input digest to detect conflicting operation-key reuse.
    /// A receipt can resolve an ambiguous commit without disclosing a secret, but its presence does not
    /// establish current owner authority or key usability. An absent receipt after a failed attempt
    /// is not, by itself, proof that the transaction rolled back.
    /// </remarks>
    Task<ExternalApiKeyIssuanceReceipt?> FindAsync(
        string operationFingerprint,
        Guid? tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rechecks a receipt's key for exact ownership, usable status and expiry under transaction-bound fences.
    /// </summary>
    /// <param name="keyId">The aggregate identity recorded by the issuance receipt, not the public key identifier.</param>
    /// <param name="tenantId">The exact key tenant, or <see langword="null"/> for a platform key.</param>
    /// <param name="ownerType">The ownership category authorized for recovery.</param>
    /// <param name="ownerId">The exact owner authorized for recovery.</param>
    /// <param name="cancellationToken">Propagated to key reads and key/status fence acquisition.</param>
    /// <returns>
    /// The currently usable, unexpired key in the exact boundary, or <see langword="null"/> if it
    /// is absent, belongs elsewhere, has an unusable status or has expired.
    /// </returns>
    /// <remarks>
    /// Requires the caller-owned issuance transaction and fresh owner authority. The returned entity
    /// is for metadata mapping only; persisted hashes do not recover the original credential.
    /// A surviving receipt does not guarantee that this method will return a key.
    /// </remarks>
    Task<ExternalApiKey?> GetIssuedKeyAsync(
        Guid keyId,
        Guid? tenantId,
        ExternalApiKeyOwnerType ownerType,
        Guid ownerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves the key and its digest-only receipt together within the caller-owned issuance transaction.
    /// </summary>
    /// <param name="receipt">Evidence whose tenant and key identity match the supplied key.</param>
    /// <param name="key">The new key containing a secret hash, never a replayable plaintext credential.</param>
    /// <param name="cancellationToken">Propagated to saving; cancellation is surfaced to the caller rather than converted to recovery success.</param>
    /// <remarks>
    /// The caller must first require a clean write scope, open its transaction and acquire current
    /// issuance authority. This method saves tracked changes but does not commit; successful completion
    /// is not a commit acknowledgement. A commit failure or cancellation does not establish rollback.
    /// Any recovery must use digest-only evidence under freshly acquired authority, never replay the secret.
    /// </remarks>
    Task CreateAsync(
        ExternalApiKeyIssuanceReceipt receipt,
        ExternalApiKey key,
        CancellationToken cancellationToken);
}
