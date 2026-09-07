using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Helpers;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public class EventDayService : IEventDayService
{
    private readonly IEventDayClient _client;
    private readonly ILogger<EventDayService> _logger;

    public EventDayService(IEventDayClient client, ILogger<EventDayService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ICollection<EventDayListDto>> GetDaysByEventAsync(
        Guid eventId,
        bool includeManaged = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = includeManaged
                ? await _client.GetManagedEventDaysByEventAsync(eventId, cancellationToken: cancellationToken)
                : await _client.GetEventDaysByEventAsync(eventId, cancellationToken: cancellationToken);
            return result?.GetItems() ?? new List<EventDayListDto>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching days for event {EventId}", eventId);
            return new List<EventDayListDto>();
        }
    }

    public async Task<EventDayDto?> GetDayByIdAsync(Guid dayId)
    {
        try
        {
            var result = await _client.GetEventDayByIdAsync(dayId);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching event day {DayId}", dayId);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CreateDayAsync(CreateEventDayDto dto)
    {
        try
        {
            return await _client.CreateEventDayAsync(dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Error creating event day: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = ex.Message };
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateDayAsync(Guid id, Guid expectedConcurrencyStamp, UpdateEventDayDto dto)
    {
        try
        {
            return await _client.UpdateEventDayAsync(id, dto, $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Error updating event day {DayId}: {StatusCode}", id, ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> DeleteDayAsync(Guid dayId)
    {
        try
        {
            await _client.DeleteEventDayAsync(dayId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event day {DayId}", dayId);
            return false;
        }
    }
}
