using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Lookup;

public interface IOrganizationLookupService
{
    Task<ICollection<OrganizationPositionListDto>> GetOrganizationPositionsAsync();
    Task<ICollection<ActorTypeListDto>> GetActorTypesAsync();
    Task<ICollection<StatusTypeListDto>> GetApprovalStatusesAsync();
}
