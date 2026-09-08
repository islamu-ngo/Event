namespace Explore.Application.Contracts.Services;

public interface ITicketPurchaseOrderResolver
{
    Task<TicketPurchaseOrderSnapshot?> ResolveAsync(
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        CancellationToken cancellationToken);
}

public sealed record TicketPurchaseOrderSnapshot(
    Guid OrderId,
    int Quantity);
