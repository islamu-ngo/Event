namespace Explore.Application.Contracts.Persistence;

using Explore.Domain.Settings.Documents;

public interface ITenantSettingsDocumentRepository : IGenericRepository<TenantSettingsDocument, Guid>
{
    /// <summary>
    /// Inserts a missing tenant/key document or returns the concurrent winner without updating it.
    /// Participates in the caller's transaction; a conflict rolls back only this insert attempt.
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
