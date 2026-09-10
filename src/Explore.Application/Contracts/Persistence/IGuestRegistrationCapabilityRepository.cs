using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Contracts.Persistence;

public interface IGuestRegistrationCapabilityRepository
{
    // Callers authenticate the protected original request before this read-only recovery seam.
    Task<RegistrationOrder?> GetExactGuestOrderAsync(
        Guid orderId,
        Guid tenantId,
        Guid eventId,
        CapabilityTokenHash guestAccessTokenHash,
        CancellationToken cancellationToken);

    // The caller owns the serializable transaction and takes the event fence after this order fence.
    Task<RegistrationOrder?> GetGuestStatusOrderForUpdateAsync(
        Guid orderId, Guid tenantId, CancellationToken cancellationToken);

    // The caller freshly authorizes the existing promise; this CAS only persists its extension.
    Task<bool> TryExtendGuestStatusAccessAsync(
        RegistrationOrder expected, DateTime deadlineUtc, CancellationToken cancellationToken);
}
