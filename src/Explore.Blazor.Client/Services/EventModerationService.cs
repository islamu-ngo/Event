using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public class EventModerationService(
    IEventModerationClient moderationClient,
    ILogger<EventModerationService> logger) : IEventModerationService
{
    public async Task<BaseCommandResponseOfGuid?> ModerateEventLightAsync(
        Guid eventId,
        CancellationToken cancellationToken = default,
        string? reasonCode = null,
        string? correlationId = null)
    {
        try
        {
            return await moderationClient.ModerateEventLightAsync(
                eventId,
                CreateModerationRequest(reasonCode, correlationId),
                cancellationToken: cancellationToken);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            logger.LogWarning(ex, "Event moderation rejected for event {EventId}", eventId);
            return ex.Result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error moderating event {EventId}", eventId);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Event could not be moderated."
            };
        }
    }

    public async Task<BaseCommandResponseOfGuid?> ModerateEventHeavyAsync(
        Guid eventId,
        CancellationToken cancellationToken = default,
        string? reasonCode = null,
        string? correlationId = null)
    {
        try
        {
            return await moderationClient.ModerateEventHeavyAsync(
                eventId,
                CreateModerationRequest(reasonCode, correlationId),
                cancellationToken: cancellationToken);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            logger.LogWarning(ex, "Event heavy moderation rejected for event {EventId}", eventId);
            return ex.Result;
        }
        catch (ApiException<ProblemDetails> ex)
        {
            logger.LogWarning(ex, "Event heavy moderation returned problem details for event {EventId}", eventId);
            return ProblemToCommandResponse(eventId, ex.Result, "Event could not be heavy moderated.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error heavy moderating event {EventId}", eventId);
            return new BaseCommandResponseOfGuid
            {
                Id = eventId,
                Success = false,
                Message = "Event could not be heavy moderated."
            };
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UnmoderateEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default,
        string? reasonCode = null,
        string? correlationId = null)
    {
        try
        {
            return await moderationClient.UnmoderateEventAsync(
                eventId,
                CreateModerationRequest(reasonCode, correlationId),
                cancellationToken: cancellationToken);
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex)
        {
            logger.LogWarning(ex, "Event unmoderation rejected for event {EventId}", eventId);
            return ex.Result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unmoderating event {EventId}", eventId);
            return new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Event could not be unmoderated."
            };
        }
    }

    private static EventModerationRequestDto CreateModerationRequest(string? reasonCode, string? correlationId)
    {
        var request = new EventModerationRequestDto();
        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            request.ReasonCode = reasonCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.CorrelationId = correlationId.Trim();
        }

        return request;
    }

    private static BaseCommandResponseOfGuid ProblemToCommandResponse(Guid eventId, ProblemDetails problem, string defaultMessage)
    {
        var message = !string.IsNullOrWhiteSpace(problem.Detail)
            ? problem.Detail
            : !string.IsNullOrWhiteSpace(problem.Title)
                ? problem.Title
                : defaultMessage;

        return new BaseCommandResponseOfGuid
        {
            Id = eventId,
            Success = false,
            Message = message
        };
    }
}
