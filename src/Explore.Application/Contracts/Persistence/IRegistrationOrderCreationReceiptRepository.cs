using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Exact tenant-owned creation evidence; the starter owns the creation transaction.</summary>
public interface IRegistrationOrderCreationReceiptRepository
{
    Task<RegistrationOrderCreationReceipt?> FindAsync(
        Guid tenantId, string operationFingerprint, CancellationToken cancellationToken);

    Task<RegistrationOrderCreationReceipt?> FindFreshAsync(
        Guid tenantId, string operationFingerprint, CancellationToken cancellationToken);

    Task<bool> FenceCurrentAccountAsync(Guid tenantId, Guid accountUserId, CancellationToken cancellationToken);

    Task<RegistrationOrder?> GetRecoverableOrderAsync(
        Guid tenantId, Guid orderId, Guid accountUserId, DateTime utcNow,
        bool fresh, CancellationToken cancellationToken);

    Task AddAsync(RegistrationOrderCreationReceipt receipt, CancellationToken cancellationToken);
}
