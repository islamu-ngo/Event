using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services.Lookup;

public class EventLookupService(
    IEventTypeClient eventTypeClient,
    IEventFormatClient eventFormatClient,
    IEventStatusClient eventStatusClient,
    IEventSessionKindClient eventSessionKindClient,
    IRegistrationModeClient registrationModeClient,
    IVisibilityTypeClient visibilityTypeClient,
    ILogger<EventLookupService> logger) : IEventLookupService
{
    public async Task<ICollection<EventTypeListDto>> GetEventTypesAsync()
    {
        try
        {
            return await eventTypeClient.GetEventTypesAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[EventLookupService.GetEventTypesAsync] API error fetching event types. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<EventTypeListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EventLookupService.GetEventTypesAsync] Unexpected error fetching event types");
            return new List<EventTypeListDto>();
        }
    }

    public async Task<ICollection<EventFormatListDto>> GetEventFormatsAsync()
    {
        try
        {
            return await eventFormatClient.GetEventFormatOptionsAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[EventLookupService.GetEventFormatsAsync] API error fetching event formats. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<EventFormatListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EventLookupService.GetEventFormatsAsync] Unexpected error fetching event formats");
            return new List<EventFormatListDto>();
        }
    }

    public async Task<ICollection<EventStatusListDto>> GetEventStatusesAsync()
    {
        try
        {
            return await eventStatusClient.GetEventStatusesAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[EventLookupService.GetEventStatusesAsync] API error fetching event statuses. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<EventStatusListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EventLookupService.GetEventStatusesAsync] Unexpected error fetching event statuses");
            return new List<EventStatusListDto>();
        }
    }

    public async Task<ICollection<EventSessionKindListDto>> GetEventSessionKindsAsync()
    {
        try
        {
            return await eventSessionKindClient.GetEventSessionKindsAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[EventLookupService.GetEventSessionKindsAsync] API error fetching event session kinds. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<EventSessionKindListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EventLookupService.GetEventSessionKindsAsync] Unexpected error fetching event session kinds");
            return new List<EventSessionKindListDto>();
        }
    }

    public async Task<ICollection<RegistrationModeListDto>> GetRegistrationModesAsync()
    {
        try
        {
            return await registrationModeClient.GetRegistrationModesAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[EventLookupService.GetRegistrationModesAsync] API error fetching registration modes. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<RegistrationModeListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EventLookupService.GetRegistrationModesAsync] Unexpected error fetching registration modes");
            return new List<RegistrationModeListDto>();
        }
    }

    public async Task<ICollection<VisibilityTypeListDto>> GetVisibilityTypesAsync()
    {
        try
        {
            return await visibilityTypeClient.GetVisibilityTypesAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[EventLookupService.GetVisibilityTypesAsync] API error fetching visibility types. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<VisibilityTypeListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EventLookupService.GetVisibilityTypesAsync] Unexpected error fetching visibility types");
            return new List<VisibilityTypeListDto>();
        }
    }
}
