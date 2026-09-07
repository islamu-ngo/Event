using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Lookup;

public interface IActorService
{
    Task<ICollection<ActorListDto>> GetActorsAsync();
    Task<ActorDto?> GetActorByIdAsync(Guid id);
    Task<ActorDto?> GetActorByTenantAsync(Guid tenantId, Guid actorId);
}
