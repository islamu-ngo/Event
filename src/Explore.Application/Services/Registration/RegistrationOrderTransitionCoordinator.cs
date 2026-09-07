using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public interface IRegistrationOrderTransitionCoordinator
{
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
