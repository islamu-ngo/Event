using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Retirement-only predicates over persisted ownership and retention authority.
/// The caller must authorize the object, acquire its storage-row fence, and flush
/// reference/hold mutations in the same transaction before scanning. All writers
/// of that authority must participate in the fence until retirement commits.
/// </summary>
public interface IStorageObjectReferenceRepository
{
    /// <summary>
    /// Declares and fences the complete persisted object set before multi-save or
    /// bulk mutations. Acquires tenant/object order in the caller's transaction.
    /// Returned entities include the refreshed fence stamps; this grants no deletion authority.
    /// </summary>
    Task<IReadOnlyList<StorageObject>> FenceAsync(
        IReadOnlyCollection<Guid> storageObjectIds, CancellationToken cancellationToken);

    /// <summary>
    /// Includes hidden, soft-deleted and other-tenant owners without disclosing them.
    /// Upload sessions and producer operations are custody, not readable references;
    /// the caller must transfer their exact-target settlement authority separately.
    /// </summary>
    Task<bool> HasBlockingReferencesAsync(Guid storageObjectId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks indirect registration CSV retention and unresolved delivery authority
    /// using a server-owned UTC instant. A false result is not deletion authorization.
    /// </summary>
    Task<bool> HasBlockingHoldsAsync(
        Guid storageObjectId, DateTime serverNowUtc, CancellationToken cancellationToken);
}
