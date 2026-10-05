using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IEventDiscoveryIdentityService
{
    Task<HalResourceOfEventDiscoveryIdentityDto> GetAsync(
        Guid eventId, Guid? candidateEventId = null, CancellationToken cancellationToken = default);
    Task<HalResourceOfEventDuplicateCandidatesDto> GetCandidatesAsync(
        Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> ReviewAsync(
        Guid eventId, ReviewEventDiscoveryAliasDto decision, CancellationToken cancellationToken = default);
}
