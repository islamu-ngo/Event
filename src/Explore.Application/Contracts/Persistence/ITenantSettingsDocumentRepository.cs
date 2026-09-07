namespace Explore.Application.Contracts.Persistence;

using Explore.Domain.Settings.Documents;

public interface ITenantSettingsDocumentRepository : IGenericRepository<TenantSettingsDocument, Guid>
{
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
