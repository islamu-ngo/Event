using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed class TicketPurchaseOrderResolver(
    IRegistrationInventoryRepository inventory) :
    ITicketPurchaseOrderResolver
{
    public async Task<TicketPurchaseOrderSnapshot?> ResolveAsync(
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order =
            await inventory.GetOrderWithLinesAsync(
                orderId,
                tenantId,
                cancellationToken);
        if (order is null || order.EventId != eventId)
        {
            return null;
        }

        int quantity = 0;
        foreach (RegistrationOrderLine line in order.Lines)
        {
            quantity = checked(quantity + line.Quantity);
        }

        return quantity > 0
            ? new TicketPurchaseOrderSnapshot(order.Id, quantity)
            : null;
    }
}
