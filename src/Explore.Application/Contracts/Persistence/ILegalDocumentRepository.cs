namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

public interface ILegalDocumentRepository
{
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

    /// <summary>
    /// Persists the imported aggregate inside the active import transaction so
    /// fidelity can read it before commit. Does not publish or commit the draft.
    /// </summary>
    Task SaveImportedAsync(
        LegalDocument legalDocument,
        CancellationToken cancellationToken);
}
