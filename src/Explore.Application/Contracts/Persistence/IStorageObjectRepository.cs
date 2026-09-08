// ABOUTME: Repository contract for storage object metadata queries.
// ABOUTME: Returns domain entities with explicit detail-loading methods for handler-side DTO mapping.

using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IStorageObjectRepository : IGenericRepository<StorageObject, Guid>
{
    Task<StorageObject?> GetFileWithDetails(Guid id);
    Task<List<StorageObject>> GetFilesWithDetails();
    Task<(List<StorageObject> Items, int TotalCount)> GetFilesWithDetailsPaged(int pageNumber, int pageSize);
    Task<IReadOnlyList<StorageObject>> GetAllForInstanceStorageReportAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageObject>> ListActiveForReconciliationAsync(
        DateTime createdBeforeUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StorageObject>> ListDeleteEligibleForReconciliationAsync(
        DateTime deleteBeforeUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StorageObject>> ListDeleteRequestedForResourceAsync(
        Guid tenantId,
        string owningResourceKind,
        Guid owningResourceId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListKnownObjectKeysAsync(
        string provider,
        IReadOnlyCollection<string> objectKeys,
        CancellationToken cancellationToken);

    Task<StorageObject?> GetEvidenceDocumentAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> IsRetainedEvidenceAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> IsRegistrationAnswerFileQuarantinedAsync(Guid id, CancellationToken cancellationToken);
    Task<RegistrationAnswerFile?> GetRegistrationAnswerFileAsync(
        Guid storageObjectId, Guid tenantId, CancellationToken cancellationToken);
    Task<RegistrationOrder?> GetRegistrationContentOrderAsync(
        StorageObject storageObject, RegistrationAnswerFile? answerFile, CancellationToken cancellationToken);
}
