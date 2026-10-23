namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;
using Explore.Application.Features.ConfigurationManifest.Importing;

public interface ILegalDocumentRepository
{
    /// <summary>
    /// Captures the actual current serializable transaction coordinate for the authorized import
    /// coordinator. This identifier proves no permission and never starts a transaction.
    /// </summary>
    Guid CaptureConfigurationImportTransaction();

    /// <summary>
    /// Reads the complete authoring graph, including the current unpublished version, without tracking.
    /// The caller must authorize the exact scope before entering its serializable export transaction.
    /// Tenant scope must match the request tenant; this method does not grant instance authority.
    /// </summary>
    Task<IReadOnlyList<LegalDocument>> GetForConfigurationExportAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Explicit cross-tenant definition read for an independently root-authorized instance export.
    /// The caller supplies only its already authorized active tenant set, in the same serializable
    /// transaction. The repository replaces no principal, request tenant or provider tenant session.
    /// </summary>
    Task<IReadOnlyList<LegalDocument>> GetForInstanceConfigurationExportAsync(
        IReadOnlyCollection<Guid> authorizedActiveTenantIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Flushes only the exact tracked legal graphs supplied by the authorized import owner.
    /// Instance imports explicitly name their authorized destination tenants; tenant imports pass
    /// an empty instance tenant set. The original serializable transaction is never committed here
    /// and is rolled back on any rejection, including foreign pending work or graph reparenting.
    /// </summary>
    Task FlushConfigurationImportInCurrentTransactionAsync(
        Guid transactionId,
        ConfigurationImportTarget target,
        IReadOnlyCollection<Guid> authorizedTenantIds,
        IReadOnlyList<LegalDocument> ownedLegalDocuments,
        CancellationToken cancellationToken);

    Task<LegalDocument?> GetForUpdateAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        CancellationToken cancellationToken);

    Task<LegalDocument?> GetByIdForUpdateAsync(
        Guid legalDocumentId,
        LegalDocumentScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken);

    Task<LegalDocument?> GetPublishedAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        LegalDocumentKind kind,
        CancellationToken cancellationToken);

    Task AddAsync(
        LegalDocument legalDocument,
        CancellationToken cancellationToken);
}
