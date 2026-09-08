using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services.Lookup;

public class CultureLookupService(
    ILanguageClient languageClient,
    IMadhabClient madhabClient,
    ILogger<CultureLookupService> logger) : ICultureLookupService
{
    public async Task<ICollection<LanguageListDto>> GetLanguagesAsync()
    {
        try
        {
            return await languageClient.GetLanguagesAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[CultureLookupService.GetLanguagesAsync] API error fetching languages. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<LanguageListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[CultureLookupService.GetLanguagesAsync] Unexpected error fetching languages");
            return new List<LanguageListDto>();
        }
    }

    public async Task<ICollection<MadhabListDto>> GetMadhabsAsync()
    {
        try
        {
            return await madhabClient.GetMadhabsAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[CultureLookupService.GetMadhabsAsync] API error fetching madhabs. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<MadhabListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[CultureLookupService.GetMadhabsAsync] Unexpected error fetching madhabs");
            return new List<MadhabListDto>();
        }
    }
}
