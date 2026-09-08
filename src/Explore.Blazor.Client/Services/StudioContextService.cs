using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed class StudioContextService(
    IStudioClient studioClient,
    IRegistrationOrderClient orderClient,
    IAuthenticatedRegistrationOrderClient authenticatedClient) : IStudioContextService
{
    public async Task<HalResourceOfStudioContextDto?> GetContextAsync(
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await studioClient.GetStudioContextAsync(actorId, cancellationToken: cancellationToken);
        }
        catch (ApiException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<HalResourceOfRegistrationOrderDto>> GetEventOrdersAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var resource = await orderClient.GetEventRegistrationOrdersAsync(eventId, cancellationToken: cancellationToken);
        return resource._embedded?.Items?.ToArray() ?? [];
    }

    public async Task<IReadOnlyList<StudioAttendeeOrder>> GetEventAttendeesAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HalResourceOfRegistrationOrderDto> orders = await GetEventOrdersAsync(eventId, cancellationToken);
        HalResourceOfRegistrationOrderDto[] visibleOrders = orders
            .Where(order => order.Id is not null && order._links?.ContainsKey("view-participants") == true)
            .ToArray();
        HalResourceOfRegistrationOrderParticipantsDto[] participants = await Task.WhenAll(
            visibleOrders.Select(order => authenticatedClient.GetAuthenticatedRegistrationOrderParticipantsAsync(
                eventId, order.Id!.Value, cancellationToken: cancellationToken)));
        return visibleOrders.Zip(participants, static (order, collection) => new StudioAttendeeOrder(order, collection)).ToArray();
    }
}
