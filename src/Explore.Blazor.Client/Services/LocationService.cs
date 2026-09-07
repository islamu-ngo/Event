using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public interface ILocationService
{
    Task<ICollection<LocationListDto>> GetAllLocationsAsync();
    Task<PaginatedResult<LocationListDto>> GetLocationsPagedAsync(int pageNumber, int pageSize);
    Task<LocationDto?> GetLocationByIdAsync(Guid locationId);
    Task<BaseCommandResponseOfGuid?> CreateLocationAsync(CreateLocationDto dto);
    Task<BaseCommandResponseOfGuid?> UpdateLocationAsync(Guid id, Guid expectedConcurrencyStamp, UpdateLocationDto dto);
    Task<bool> DeleteLocationAsync(Guid locationId);
}

public class LocationService : ILocationService
{
    private readonly ILocationClient _apiClient;
    private readonly ILogger<LocationService> _logger;

    public LocationService(ILocationClient apiClient, ILogger<LocationService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ICollection<LocationListDto>> GetAllLocationsAsync()
    {
        try
        {
            var result = await _apiClient.GetLocationsAsync(pageNumber: ApiConstants.FirstPage, pageSize: ApiConstants.DefaultPageSize);
            return result?.GetItems() ?? new List<LocationListDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[LOCATION SERVICE] API error fetching locations: {StatusCode}", ex.StatusCode);
            return new List<LocationListDto>();
        }
    }

    public async Task<PaginatedResult<LocationListDto>> GetLocationsPagedAsync(int pageNumber, int pageSize)
    {
        try
        {
            var result = await _apiClient.GetLocationsAsync(pageNumber, pageSize);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCATION SERVICE] Error fetching paged locations (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<LocationListDto>.Empty(pageNumber, pageSize);
        }
    }

    public async Task<LocationDto?> GetLocationByIdAsync(Guid locationId)
    {
        try
        {
            var result = await _apiClient.GetLocationByIdAsync(locationId);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("[LOCATION SERVICE] Location not found: {LocationId}", locationId);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[LOCATION SERVICE] API error fetching location {LocationId}: {StatusCode}", locationId, ex.StatusCode);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CreateLocationAsync(CreateLocationDto dto)
    {
        try
        {
            return await _apiClient.CreateLocationAsync(dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[LOCATION SERVICE] API error creating location: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = $"API error: {ex.Message}", Errors = new List<string> { ex.Response ?? ex.Message } };
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateLocationAsync(Guid id, Guid expectedConcurrencyStamp, UpdateLocationDto dto)
    {
        try
        {
            if (id == Guid.Empty || expectedConcurrencyStamp == Guid.Empty)
            {
                return new BaseCommandResponseOfGuid
                {
                    Success = false,
                    Message = "Location route ID and concurrency stamp are required."
                };
            }

            return await _apiClient.UpdateLocationAsync(id, dto, $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[LOCATION SERVICE] API error updating location: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = $"API error: {ex.Message}", Errors = new List<string> { ex.Response ?? ex.Message } };
        }
    }

    public async Task<bool> DeleteLocationAsync(Guid locationId)
    {
        try
        {
            await _apiClient.DeleteLocationAsync(locationId);
            return true;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[LOCATION SERVICE] API error deleting location: {StatusCode}", ex.StatusCode);
            return false;
        }
    }
}
