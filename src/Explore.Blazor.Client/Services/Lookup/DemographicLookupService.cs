using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services.Lookup;

public class DemographicLookupService(
    IAudienceGenderClient audienceGenderClient,
    IAudienceAgeClient audienceAgeClient,
    ILogger<DemographicLookupService> logger) : IDemographicLookupService
{
    public async Task<ICollection<AudienceGenderListDto>> GetAudienceGendersAsync()
    {
        try
        {
            return await audienceGenderClient.GetAudienceGenderOptionsAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[DemographicLookupService.GetAudienceGendersAsync] API error fetching audience genders. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<AudienceGenderListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DemographicLookupService.GetAudienceGendersAsync] Unexpected error fetching audience genders");
            return new List<AudienceGenderListDto>();
        }
    }

    public async Task<ICollection<AudienceAgeListDto>> GetAudienceAgesAsync()
    {
        try
        {
            return await audienceAgeClient.GetAudienceAgeOptionsAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[DemographicLookupService.GetAudienceAgesAsync] API error fetching audience ages. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<AudienceAgeListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[DemographicLookupService.GetAudienceAgesAsync] Unexpected error fetching audience ages");
            return new List<AudienceAgeListDto>();
        }
    }
}
