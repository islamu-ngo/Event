namespace Explore.Application.Contracts.Persistence;

using Explore.Domain.Settings.Documents;

public interface ITenantSettingsDocumentRepository : IGenericRepository<TenantSettingsDocument, Guid>
{
    /// <summary>
    /// Applies only catalog branding in the caller's import transaction. PostgreSQL
    /// instance imports use fixed native create/CAS authority; other callers retain
    /// the established tracked repository mutation and SaveChanges behavior.
    /// </summary>
    Task<TenantSettingsDocument> ApplyBrandingImportAsync(
        Guid tenantId,
        int schemaVersion,
        string defaultsVersion,
        string payloadJson,
        Guid actorUserId,
        DateTime occurredAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a missing tenant/key document or returns the concurrent winner without updating it.
    /// Participates in the caller's transaction; successful conflict recovery rolls back only
    /// this insert attempt. Failed cleanup preserves the original failure for the owner.
    /// </summary>
    Task<TenantSettingsDocument> CreateIfMissingAsync(
        TenantSettingsDocument document,
        CancellationToken cancellationToken = default);

    Task<TenantSettingsDocument?> GetByTenantAndDocumentKey(
        Guid tenantId,
        string documentKey,
        CancellationToken cancellationToken = default);

    Task<TenantSettingsDocument?> GetTrackedByTenantAndDocumentKey(
        Guid tenantId,
        string documentKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantSettingsDocument>> GetManyForTenant(
        Guid tenantId,
        IEnumerable<string> documentKeys,
        CancellationToken cancellationToken = default);
}
