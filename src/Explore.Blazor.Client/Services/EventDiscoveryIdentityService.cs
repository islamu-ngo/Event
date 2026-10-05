using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;

namespace Explore.Blazor.Client.Services;

public sealed class EventDiscoveryIdentityService(IEventDiscoveryIdentityClient client) : IEventDiscoveryIdentityService
{
    public Task<HalResourceOfEventDiscoveryIdentityDto> GetAsync(
        Guid eventId, Guid? candidateEventId = null, CancellationToken cancellationToken = default) =>
        client.GetEventDiscoveryIdentityAsync(eventId, candidateEventId, cancellationToken: cancellationToken);

    public Task<HalResourceOfEventDuplicateCandidatesDto> GetCandidatesAsync(
        Guid eventId, CancellationToken cancellationToken = default) =>
        client.GetEventDuplicateCandidatesAsync(eventId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> ReviewAsync(
        Guid eventId, ReviewEventDiscoveryAliasDto decision, CancellationToken cancellationToken = default) =>
        client.ReviewEventDiscoveryAliasAsync(eventId, decision, cancellationToken: cancellationToken);
}
