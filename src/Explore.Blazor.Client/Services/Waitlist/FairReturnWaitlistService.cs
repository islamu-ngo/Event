using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Waitlist;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Services.Waitlist;

public sealed class FairReturnWaitlistService(
    IBffClient bff) :
    IFairReturnWaitlistService
{
    public Task<HalResourceOfFairReturnWaitlistDto?>
        GetAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            string? capability,
            CancellationToken cancellationToken) =>
        bff.GetWithRegistrationOrderCapabilityAsync<
            HalResourceOfFairReturnWaitlistDto>(
                RootPath(
                    eventId,
                    registrationOrderId,
                    registrationOrderLineId),
                capability,
                cancellationToken);

    public Task<HalResourceOfFairReturnWaitlistDto?>
        JoinAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid operationId,
            CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Post,
            RootPath(
                eventId,
                registrationOrderId,
                registrationOrderLineId),
            operationId,
            cancellationToken);

    public Task<HalResourceOfFairReturnWaitlistDto?>
        LeaveAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid operationId,
            CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Delete,
            RootPath(
                eventId,
                registrationOrderId,
                registrationOrderLineId),
            operationId,
            cancellationToken);

    public Task<HalResourceOfFairReturnWaitlistDto?>
        AcceptOfferAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid offerId,
            Guid operationId,
            CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Post,
            $"{RootPath(eventId, registrationOrderId, registrationOrderLineId)}/offers/{offerId:D}/accept",
            operationId,
            cancellationToken);

    public Task<HalResourceOfFairReturnWaitlistDto?>
        WithdrawSupplyAsync(
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            Guid supplyId,
            Guid operationId,
            CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Delete,
            $"{RootPath(eventId, registrationOrderId, registrationOrderLineId)}/supply/{supplyId:D}",
            operationId,
            cancellationToken);

    private Task<
        HalResourceOfFairReturnWaitlistDto?>
        SendAsync(
            HttpMethod method,
            string path,
            Guid operationId,
            CancellationToken cancellationToken) =>
        bff.SendIdempotentAsync<
            HalResourceOfFairReturnWaitlistDto>(
                method,
                path,
                operationId,
                cancellationToken);

    private static string RootPath(
        Guid eventId,
        Guid registrationOrderId,
        Guid registrationOrderLineId) =>
        $"/bff/events/{eventId:D}/" +
        $"registration-orders/" +
        $"{registrationOrderId:D}/lines/" +
        $"{registrationOrderLineId:D}/waitlist";
}
