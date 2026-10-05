using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IExternalApiKeyIssuanceReceiptRepository
{
    /// <summary>Rejects an ambient scope with pending writes unrelated to the issuance transaction.</summary>
    void RequireCleanWriteScope(Guid? tenantId);

    Task<ExternalApiKeyIssuanceReceipt?> FindAsync(
        string operationFingerprint,
        Guid? tenantId,
        CancellationToken cancellationToken);

    Task<ExternalApiKey?> GetIssuedKeyAsync(
        Guid keyId,
        Guid? tenantId,
        ExternalApiKeyOwnerType ownerType,
        Guid ownerId,
        CancellationToken cancellationToken);

    Task CreateAsync(
        ExternalApiKeyIssuanceReceipt receipt,
        ExternalApiKey key,
        CancellationToken cancellationToken);
}
