using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;

namespace Explore.Blazor.Client.Services;

public class EventSessionService(
    IEventSessionClient sessionClient,
    IEventSessionGroupClient sessionGroupClient,
    IEventManagementReadClient managementReadClient,
    ILogger<EventSessionService> logger) : IEventSessionService
{
    public async Task<PaginatedResult<EventSessionListDto>> GetSessionsPagedAsync(int pageNumber, int pageSize)
    {
        try
        {
            var result = await sessionClient.GetEventSessionsListAsync(pageNumber, pageSize);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching paged sessions (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<EventSessionListDto>.Empty(pageNumber, pageSize);
        }
    }

    public async Task<ICollection<EventSessionListDto>> GetAllSessionsAsync()
    {
        try
        {
            var result = await sessionClient.GetEventSessionsListAsync(1, 100);
            return result?.GetItems() ?? new List<EventSessionListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching all sessions");
            return new List<EventSessionListDto>();
        }
    }

    public async Task<ICollection<EventSessionListDto>> GetSessionsByEventAsync(
        Guid eventId,
        bool includeManagedSessions = false,
        CancellationToken cancellationToken = default)
    {
        if (includeManagedSessions)
        {
            try
            {
                var managedSessions = await sessionClient.GetManagedEventSessionsByEventAsync(
                    eventId,
                    cancellationToken: cancellationToken);
                return managedSessions?.GetItems() ?? new List<EventSessionListDto>();
            }
            catch (ApiException ex) when (ex.StatusCode is 401 or 403 or 404)
            {
                logger.LogDebug(
                    "Managed event sessions unavailable for event {EventId}; status {StatusCode}.",
                    eventId,
                    ex.StatusCode);
                return new List<EventSessionListDto>();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error fetching managed sessions for event {EventId}", eventId);
                return new List<EventSessionListDto>();
            }
        }

        try
        {
            var publicSessions = await sessionClient.GetEventSessionsAsync(
                eventId,
                cancellationToken: cancellationToken);
            return publicSessions?.GetItems() ?? new List<EventSessionListDto>();
        }
        catch (ApiException ex) when (ex.StatusCode is 401 or 403 or 404)
        {
            logger.LogDebug(
                "Public event sessions unavailable for event {EventId}; status {StatusCode}.",
                eventId,
                ex.StatusCode);
            return new List<EventSessionListDto>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching public sessions for event {EventId}", eventId);
            return new List<EventSessionListDto>();
        }
    }

    public async Task<EventSessionDto?> GetSessionByIdAsync(Guid sessionId)
    {
        try
        {
            var result = await sessionClient.GetEventSessionByIdAsync(sessionId);
            return result?.ToDto();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<EventSessionDto?> GetManagedSessionByIdAsync(Guid eventId, Guid sessionId)
    {
        try
        {
            var result = await sessionClient.GetManagedEventSessionByIdAsync(eventId, sessionId);
            return result?.ToDto();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching managed session {SessionId} for event {EventId}", sessionId, eventId);
            return null;
        }
    }

    public Task<BaseCommandResponseOfGuid> CreateSessionAsync(CreateEventSessionDto session)
        => sessionClient.CreateEventSessionAsync(session);

    public Task<BaseCommandResponseOfGuid> UpdateSessionAsync(
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        UpdateEventSessionDto session)
        => sessionClient.UpdateEventSessionAsync(
            sessionId,
            session,
            $"\"{expectedConcurrencyStamp:D}\"");

    public Task<BaseCommandResponseOfGuid?> PublishEventSessionAsync(
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default) =>
        ExecuteSessionLifecycleActionAsync(
            sessionId,
            "publish",
            () => sessionClient.PublishEventSessionAsync(
                sessionId,
                new PublishEventSessionRequestDto { ExpectedConcurrencyStamp = expectedConcurrencyStamp },
                cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfGuid?> ArchiveEventSessionAsync(
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default) =>
        ExecuteSessionLifecycleActionAsync(
            sessionId,
            "archive",
            () => sessionClient.ArchiveEventSessionAsync(
                sessionId,
                new EventSessionLifecycleRequestDto { ExpectedConcurrencyStamp = expectedConcurrencyStamp },
                cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfGuid?> CancelEventSessionAsync(
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default) =>
        ExecuteSessionLifecycleActionAsync(
            sessionId,
            "cancel",
            () => sessionClient.CancelEventSessionAsync(
                sessionId,
                new EventSessionLifecycleRequestDto { ExpectedConcurrencyStamp = expectedConcurrencyStamp },
                cancellationToken: cancellationToken));

    public Task<BaseCommandResponseOfGuid?> CompleteEventSessionAsync(
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default) =>
        ExecuteSessionLifecycleActionAsync(
            sessionId,
            "complete",
            () => sessionClient.CompleteEventSessionAsync(
                sessionId,
                new EventSessionLifecycleRequestDto { ExpectedConcurrencyStamp = expectedConcurrencyStamp },
                cancellationToken: cancellationToken));

    private async Task<BaseCommandResponseOfGuid?> ExecuteSessionLifecycleActionAsync(
        Guid sessionId,
        string actionName,
        Func<Task<BaseCommandResponseOfGuid>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            logger.LogWarning(ex, "Event session {ActionName} rejected for session {SessionId}", actionName, sessionId);
            return ex.Result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing event session {ActionName} for session {SessionId}", actionName, sessionId);
            return null;
        }
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId)
    {
        try
        {
            await sessionClient.DeleteEventSessionAsync(sessionId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ICollection<HalResourceOfEventSessionGroupListDto>> GetSessionGroupsByEventAsync(Guid eventId)
    {
        try
        {
            var result = await sessionGroupClient.GetEventSessionGroupsByEventAsync(eventId);
            return result?.GetItems() ?? new List<HalResourceOfEventSessionGroupListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching session groups for event {EventId}", eventId);
            return new List<HalResourceOfEventSessionGroupListDto>();
        }
    }

    public async Task<ICollection<HalResourceOfEventSessionGroupListDto>> GetManagedSessionGroupsByEventAsync(Guid eventId)
    {
        try
        {
            var result = await sessionGroupClient.GetManagedEventSessionGroupsByEventAsync(eventId);
            return result?.GetItems() ?? new List<HalResourceOfEventSessionGroupListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching managed session groups for event {EventId}", eventId);
            return new List<HalResourceOfEventSessionGroupListDto>();
        }
    }

    public async Task<BaseCommandResponseOfGuid> CreateSessionGroupAsync(CreateEventSessionGroupRequestDto group)
    {
        try
        {
            return await sessionGroupClient.CreateEventSessionGroupAsync(group);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating program section for event {EventId}", group.EventId);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Program section could not be created."
            };
        }
    }

    public async Task<BaseCommandResponseOfGuid> UpdateSessionGroupAsync(
        Guid sessionGroupId,
        Guid expectedConcurrencyStamp,
        UpdateEventSessionGroupRequestDto group)
    {
        try
        {
            return await sessionGroupClient.UpdateEventSessionGroupAsync(
                sessionGroupId,
                group,
                $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error updating program section {SessionGroupId}",
                sessionGroupId);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Program section could not be updated."
            };
        }
    }

    public async Task<bool> DeleteSessionGroupAsync(Guid eventId, Guid sessionGroupId)
    {
        try
        {
            await sessionGroupClient.DeleteEventSessionGroupAsync(sessionGroupId, eventId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error deleting program section {SessionGroupId} for event {EventId}",
                sessionGroupId,
                eventId);
            return false;
        }
    }

    public async Task<BaseCommandResponseOfGuid> AssignSessionToGroupAsync(
        Guid eventId,
        Guid eventSessionGroupId,
        Guid eventSessionId,
        bool isPrimary = true,
        int sortOrder = 0)
    {
        try
        {
            return await sessionGroupClient.AssignEventSessionToGroupAsync(
                eventSessionGroupId,
                new AssignSessionToGroupRequestDto
                {
                    EventId = eventId,
                    EventSessionGroupId = eventSessionGroupId,
                    EventSessionId = eventSessionId,
                    IsPrimary = isPrimary,
                    SortOrder = sortOrder
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error assigning session {SessionId} to program section {SessionGroupId} for event {EventId}",
                eventSessionId,
                eventSessionGroupId,
                eventId);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Session could not be assigned to the selected program section."
            };
        }
    }

    public async Task<BaseCommandResponseOfGuid> UnassignSessionFromGroupAsync(
        Guid eventId,
        Guid eventSessionGroupId,
        Guid eventSessionId)
    {
        try
        {
            await sessionGroupClient.UnassignEventSessionFromGroupAsync(
                eventSessionGroupId,
                eventSessionId,
                eventId);
            return new BaseCommandResponseOfGuid
            {
                Success = true,
                Id = eventSessionId,
                Message = "Session was removed from the selected program section."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error removing session {SessionId} from program section {SessionGroupId} for event {EventId}",
                eventSessionId,
                eventSessionGroupId,
                eventId);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Session could not be removed from the selected program section."
            };
        }
    }

    public async Task<EventSessionCreateContextDto?> GetEventSessionCreateContextAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await managementReadClient.GetEventSessionCreateContextAsync(eventId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching event session create context {EventId}", eventId);
            return null;
        }
    }
}
