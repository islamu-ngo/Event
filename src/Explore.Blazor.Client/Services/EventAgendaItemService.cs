using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Helpers;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public class EventAgendaItemService : IEventAgendaItemService
{
    private readonly IEventAgendaItemClient _client;
    private readonly ILogger<EventAgendaItemService> _logger;

    public EventAgendaItemService(IEventAgendaItemClient client, ILogger<EventAgendaItemService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ICollection<EventAgendaItemListDto>> GetAgendaItemsByEventAsync(Guid eventId)
    {
        try
        {
            var result = await _client.GetEventAgendaItemsByEventAsync(eventId);
            return result?.GetItems() ?? new List<EventAgendaItemListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching agenda items for event {EventId}", eventId);
            return new List<EventAgendaItemListDto>();
        }
    }

    public async Task<ICollection<EventAgendaItemListDto>> GetManagedAgendaItemsByEventAsync(Guid eventId)
    {
        try
        {
            var result = await _client.GetManagedEventAgendaItemsByEventAsync(eventId);
            return result?.GetItems() ?? new List<EventAgendaItemListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching managed agenda items for event {EventId}", eventId);
            return new List<EventAgendaItemListDto>();
        }
    }

    public async Task<EventAgendaItemDto?> GetAgendaItemByIdAsync(Guid agendaItemId)
    {
        try
        {
            var result = await _client.GetEventAgendaItemByIdAsync(agendaItemId);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching agenda item {AgendaItemId}", agendaItemId);
            return null;
        }
    }

    public async Task<EventAgendaItemDto?> GetManagedAgendaItemByIdAsync(Guid eventId, Guid agendaItemId)
    {
        try
        {
            var result = await _client.GetManagedEventAgendaItemByIdAsync(eventId, agendaItemId);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode is 401 or 403 or 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error fetching managed agenda item {AgendaItemId} for event {EventId}",
                agendaItemId,
                eventId);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CreateAgendaItemAsync(CreateEventAgendaItemDto dto)
    {
        try
        {
            return await _client.CreateEventAgendaItemAsync(dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Error creating agenda item: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = ex.Message };
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateAgendaItemAsync(Guid id, Guid expectedConcurrencyStamp, UpdateEventAgendaItemDto dto)
    {
        try
        {
            return await _client.UpdateEventAgendaItemAsync(id, dto, $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Error updating agenda item {Id}: {StatusCode}", id, ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> DeleteAgendaItemAsync(Guid agendaItemId)
    {
        try
        {
            await _client.DeleteEventAgendaItemAsync(agendaItemId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agenda item {AgendaItemId}", agendaItemId);
            return false;
        }
    }
}
