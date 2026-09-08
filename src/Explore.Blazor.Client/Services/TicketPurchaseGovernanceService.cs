using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Services;

public sealed class TicketPurchaseGovernanceService(
    IBffClient bff) : ITicketPurchaseGovernanceService
{
    public async Task<TicketPurchaseGovernanceSubmission>
        ReserveAsync(
            Guid eventId,
            Guid orderId,
            int accessMode,
            Guid? requestedPurchaserActorId,
            string? guestCapability,
            bool authenticated,
            CancellationToken cancellationToken)
    {
        string path =
            $"/bff/events/{eventId:D}/registration-orders/" +
            (authenticated
                ? $"{orderId:D}/purchase-authority"
                : $"guest/{orderId:D}/purchase-authority");
        var request = new ReserveTicketPurchaseRequest
        {
            AccessMode = accessMode,
            RequestedPurchaserActorId =
                requestedPurchaserActorId,
        };
        HalResourceOfTicketPurchaseGovernanceResource? resource =
            authenticated
                ? await bff.SendAsync<
                    ReserveTicketPurchaseRequest,
                    HalResourceOfTicketPurchaseGovernanceResource>(
                    HttpMethod.Post,
                    path,
                    request,
                    cancellationToken)
                : await bff.SendWithRegistrationOrderCapabilityAsync<
                    ReserveTicketPurchaseRequest,
                    HalResourceOfTicketPurchaseGovernanceResource>(
                    HttpMethod.Post,
                    path,
                    request,
                    guestCapability,
                    cancellationToken);
        return resource is null
            ? new TicketPurchaseGovernanceSubmission(
                false,
                false,
                "unavailable")
            : new TicketPurchaseGovernanceSubmission(
                true,
                resource.SupportsHardCrossOrderCeiling,
                resource.EnforcementScopeCode);
    }
}
