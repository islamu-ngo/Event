using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Lookup;

public interface IDemographicLookupService
{
    Task<ICollection<AudienceGenderListDto>> GetAudienceGendersAsync();
    Task<ICollection<AudienceAgeListDto>> GetAudienceAgesAsync();
}
