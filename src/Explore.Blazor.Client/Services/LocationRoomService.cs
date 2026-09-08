using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Helpers;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public class LocationRoomService : ILocationRoomService
{
    private readonly ILocationRoomClient _client;
    private readonly ILogger<LocationRoomService> _logger;

    public LocationRoomService(ILocationRoomClient client, ILogger<LocationRoomService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<ICollection<LocationRoomListDto>> GetRoomsByLocationAsync(Guid locationId)
    {
        try
        {
            var result = await _client.GetLocationRoomsByLocationAsync(locationId);
            return result?.GetItems() ?? new List<LocationRoomListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching rooms for location {LocationId}", locationId);
            return new List<LocationRoomListDto>();
        }
    }

    public async Task<LocationRoomDto?> GetRoomByIdAsync(Guid roomId)
    {
        try
        {
            var result = await _client.GetLocationRoomByIdAsync(roomId);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching room {RoomId}", roomId);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CreateRoomAsync(CreateLocationRoomDto dto)
    {
        try
        {
            return await _client.CreateLocationRoomAsync(dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Error creating room: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = ex.Message };
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateRoomAsync(Guid id, Guid expectedConcurrencyStamp, UpdateLocationRoomDto dto)
    {
        try
        {
            if (id == Guid.Empty || expectedConcurrencyStamp == Guid.Empty)
            {
                return new BaseCommandResponseOfGuid
                {
                    Success = false,
                    Message = "Room route ID and concurrency stamp are required."
                };
            }

            return await _client.UpdateLocationRoomAsync(id, dto, $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Error updating room {Id}: {StatusCode}", id, ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> DeleteRoomAsync(Guid roomId)
    {
        try
        {
            await _client.DeleteLocationRoomAsync(roomId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting room {RoomId}", roomId);
            return false;
        }
    }
}
