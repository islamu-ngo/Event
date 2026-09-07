// Wraps NSwag-generated client methods with application-specific error handling.

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Contracts.Services.Organizations;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Service implementation for managing Event Aspects (Islamic and Tech characteristics).
/// Uses the NSwag-generated event client for type-safe API communication.
/// </summary>
public class EventAspectService : IEventAspectService
{
    private readonly IEventClient _apiClient;
    private readonly ILogger<EventAspectService> _logger;

    public EventAspectService(IEventClient apiClient, ILogger<EventAspectService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EventIslamicAspectDto?> GetIslamicAspectAsync(Guid eventId, bool includeManaged = false)
    {
        try
        {
            return includeManaged
                ? await _apiClient.GetManagedEventIslamicAspectAsync(eventId)
                : await _apiClient.GetEventIslamicAspectAsync(eventId);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Islamic aspect not found for event {EventId}", eventId);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error fetching Islamic aspect for event {EventId}. Status: {StatusCode}",
                eventId, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Islamic aspect for event {EventId}", eventId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<EventTechAspectDto?> GetTechAspectAsync(Guid eventId, bool includeManaged = false)
    {
        try
        {
            return includeManaged
                ? await _apiClient.GetManagedEventTechAspectAsync(eventId)
                : await _apiClient.GetEventTechAspectAsync(eventId);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Tech aspect not found for event {EventId}", eventId);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error fetching Tech aspect for event {EventId}. Status: {StatusCode}",
                eventId, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Tech aspect for event {EventId}", eventId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> CreateIslamicAspectAsync(Guid eventId, CreateUpdateIslamicAspectDto dto)
    {
        try
        {
            return await _apiClient.CreateEventIslamicAspectAsync(eventId, dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error creating Islamic aspect for event {EventId}. Status: {StatusCode}",
                eventId, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Islamic aspect for event {EventId}", eventId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> UpdateIslamicAspectAsync(Guid eventId, UpdateEventIslamicAspectDto dto)
    {
        try
        {
            return await _apiClient.UpdateEventIslamicAspectAsync(eventId, dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error updating Islamic aspect for event {EventId}. Status: {StatusCode}",
                eventId, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Islamic aspect for event {EventId}", eventId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> CreateTechAspectAsync(Guid eventId, CreateUpdateTechAspectDto dto)
    {
        try
        {
            return await _apiClient.CreateEventTechAspectAsync(eventId, dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error creating Tech aspect for event {EventId}. Status: {StatusCode}",
                eventId, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Tech aspect for event {EventId}", eventId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> UpdateTechAspectAsync(Guid eventId, UpdateEventTechAspectDto dto)
    {
        try
        {
            return await _apiClient.UpdateEventTechAspectAsync(eventId, dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error updating Tech aspect for event {EventId}. Status: {StatusCode}",
                eventId, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Tech aspect for event {EventId}", eventId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteIslamicAspectAsync(Guid eventId)
    {
        try
        {
            await _apiClient.DeleteEventIslamicAspectAsync(eventId);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Islamic aspect already deleted or not found for event {EventId}", eventId);
            return true; // Consider not found as successful deletion
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error deleting Islamic aspect for event {EventId}. Status: {StatusCode}",
                eventId, ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Islamic aspect for event {EventId}", eventId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTechAspectAsync(Guid eventId)
    {
        try
        {
            await _apiClient.DeleteEventTechAspectAsync(eventId);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Tech aspect already deleted or not found for event {EventId}", eventId);
            return true; // Consider not found as successful deletion
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error deleting Tech aspect for event {EventId}. Status: {StatusCode}",
                eventId, ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Tech aspect for event {EventId}", eventId);
            return false;
        }
    }
}
