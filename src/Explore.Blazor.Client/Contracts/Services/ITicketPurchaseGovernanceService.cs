namespace Explore.Blazor.Client.Contracts.Services;

public interface ITicketPurchaseGovernanceService
{
    Task<TicketPurchaseGovernanceSubmission> ReserveAsync(
        Guid eventId,
        Guid orderId,
        int accessMode,
        Guid? requestedPurchaserActorId,
        string? guestCapability,
        bool authenticated,
        CancellationToken cancellationToken);
}
