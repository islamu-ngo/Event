// ABOUTME: Persists registration-order transitions only after the tracked aggregate accepts them.
// ABOUTME: Keeps tenant-qualified locking and storage orchestration outside Domain lifecycle authority.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Services.Registration;

public interface IRegistrationOrderTransitionCoordinator
{
    Task<bool> PersistAnonymousCancellationAsync(
        RegistrationOrder order, AnonymousCancellationEvidence evidence, DateTime timestamp,
        CancellationToken cancellationToken);

    Task<bool> PersistAsync(
        Guid orderId,
        Guid tenantId,
        RegistrationOrderStatusEnum expectedStatus,
        RegistrationOrderStatusEnum desiredStatus,
        DateTime timestamp,
        CancellationToken cancellationToken);
}

public sealed class RegistrationOrderTransitionCoordinator(IRegistrationInventoryRepository inventory)
    : IRegistrationOrderTransitionCoordinator
{
    public async Task<bool> PersistAnonymousCancellationAsync(
        RegistrationOrder order, AnonymousCancellationEvidence evidence, DateTime timestamp,
        CancellationToken cancellationToken)
    {
        if (!order.TryCancelConfirmedAnonymous(evidence, timestamp))
        {
            return false;
        }

        await inventory.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> PersistAsync(
        Guid orderId,
        Guid tenantId,
        RegistrationOrderStatusEnum expectedStatus,
        RegistrationOrderStatusEnum desiredStatus,
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetOrderForUpdateWithLinesAsync(
            orderId,
            tenantId,
            cancellationToken);
        if (order is null || !order.TryTransitionFrom(expectedStatus, desiredStatus, timestamp))
        {
            return false;
        }

        await inventory.SaveChangesAsync(cancellationToken);
        return true;
    }
}
