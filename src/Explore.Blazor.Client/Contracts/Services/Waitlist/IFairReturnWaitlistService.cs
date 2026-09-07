using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Waitlist;

public interface IFairReturnWaitlistService
{
    Task<HalResourceOfFairReturnWaitlistDto?>
        GetAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            string? capability,
            CancellationToken cancellationToken);

    Task<HalResourceOfFairReturnWaitlistDto?>
        JoinAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid operationId,
            CancellationToken cancellationToken);

    Task<HalResourceOfFairReturnWaitlistDto?>
        LeaveAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid operationId,
            CancellationToken cancellationToken);

    Task<HalResourceOfFairReturnWaitlistDto?>
        AcceptOfferAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid offerId,
            Guid operationId,
            CancellationToken cancellationToken);

    Task<HalResourceOfFairReturnWaitlistDto?>
        WithdrawSupplyAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid supplyId,
            Guid operationId,
            CancellationToken cancellationToken);
}
