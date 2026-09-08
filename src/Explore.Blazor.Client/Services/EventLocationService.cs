using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Helpers;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public sealed class EventLocationService(
    IEventLocationClient client,
    ILogger<EventLocationService> logger) : IEventLocationService
{
    public async Task<IReadOnlyList<EventLocationPublicDto>> GetPublicAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ICollection<EventLocationPublicDto> result =
                await client.GetPublicEventLocationsAsync(eventId, cancellationToken: cancellationToken);
            return [.. result];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            // A withheld or unpublished event is indistinguishable from "no public venue" by design.
            LogFailure(exception, "public disclosures", eventId);
            return [];
        }
    }

    public async Task<IReadOnlyList<EventLocationAttendeeDto>> GetMyAccessAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ICollection<EventLocationAttendeeDto> result =
                await client.GetAttendeeEventLocationsAsync(eventId, cancellationToken: cancellationToken);
            return [.. result];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            LogFailure(exception, "attendee disclosures", eventId);
            return [];
        }
    }

    public async Task<HalResourceOfEventLocationManagementDto?> GetManagementAsync(
        Guid eventId,
        Guid eventLocationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.GetManagementEventLocationAsync(
                eventId,
                eventLocationId,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            LogFailure(exception, "management detail", eventId);
            return null;
        }
    }

    public async Task<IReadOnlyList<HalResourceOfEventLocationManagementDto>> GetManagementListAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            HalCollectionResourceOfEventLocationManagementDto result =
                await client.GetManagementEventLocationsAsync(eventId, cancellationToken: cancellationToken);
            return [.. result.GetItems()];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            LogFailure(exception, "management list", eventId);
            return [];
        }
    }

    public async Task<IReadOnlyList<HalResourceOfEventLocationManagementDto>> GetReviewQueueAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            HalCollectionResourceOfEventLocationManagementDto result =
                await client.GetEventLocationReviewQueueAsync(eventId, cancellationToken: cancellationToken);
            return [.. result.GetItems()];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            LogFailure(exception, "privacy review queue", eventId);
            return [];
        }
    }

    public async Task<BaseCommandResponseOfGuid> UpdateDisclosureAsync(
        Guid eventId,
        Guid eventLocationId,
        UpdateEventLocationDisclosureDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await client.UpdateEventLocationDisclosureAsync(
                eventId,
                eventLocationId,
                request,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            LogFailure(exception, "disclosure update", eventId);
            return ToFailure(exception);
        }
    }

    public async Task<BaseCommandResponseOfGuid> ConfirmRemediationAsync(
        Guid eventId,
        Guid eventLocationId,
        ConfirmEventLocationRemediationDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await client.ConfirmEventLocationRemediationAsync(
                eventId,
                eventLocationId,
                request,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            LogFailure(exception, "remediation confirmation", eventId);
            return ToFailure(exception);
        }
    }

    /// <summary>
    /// Surfaces a transport failure as a failed command instead of a thrown exception, so the caller
    /// keeps its stale concurrency tokens and must re-read before retrying.
    /// </summary>
    private static BaseCommandResponseOfGuid ToFailure(ApiException exception) => new()
    {
        Success = false,
        Message = $"API error: {exception.Message}",
        Errors = [exception.Response ?? exception.Message]
    };

    private void LogFailure(ApiException exception, string surface, Guid eventId) =>
        logger.LogError(
            exception,
            "[EVENT LOCATION SERVICE] API error fetching {Surface} for event {EventId}: {StatusCode}",
            surface,
            eventId,
            exception.StatusCode);
}
