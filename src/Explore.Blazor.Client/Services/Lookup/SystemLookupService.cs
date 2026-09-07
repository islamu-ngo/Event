using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services.Lookup;

public class SystemLookupService(
    IFileTypeClient fileTypeClient,
    IDidCustodyTypeClient didCustodyTypeClient,
    ILogger<SystemLookupService> logger) : ISystemLookupService
{
    public async Task<ICollection<FileTypeListDto>> GetFileTypesAsync()
    {
        try
        {
            return await fileTypeClient.GetFileTypesAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[SystemLookupService.GetFileTypesAsync] API error fetching file types. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<FileTypeListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SystemLookupService.GetFileTypesAsync] Unexpected error fetching file types");
            return new List<FileTypeListDto>();
        }
    }

    public async Task<ICollection<DidCustodyTypeListDto>> GetDidCustodyTypesAsync()
    {
        try
        {
            return await didCustodyTypeClient.GetDidCustodyTypeOptionsAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[SystemLookupService.GetDidCustodyTypesAsync] API error fetching DID custody types. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<DidCustodyTypeListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SystemLookupService.GetDidCustodyTypesAsync] Unexpected error fetching DID custody types");
            return new List<DidCustodyTypeListDto>();
        }
    }
}
