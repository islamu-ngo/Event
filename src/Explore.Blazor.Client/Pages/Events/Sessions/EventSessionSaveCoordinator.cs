using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Pages.Events.Sessions;

internal static class EventSessionSaveCoordinator
{
    public static async Task<EventSessionSaveResult> SaveCreateSessionAsync(
        IEventSessionService sessionService,
        CreateEventSessionDto session,
        Guid eventId,
        Guid? selectedSessionGroupId,
        Guid? savedSessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionService);
        ArgumentNullException.ThrowIfNull(session);

        var sessionId = savedSessionId;
        if (!sessionId.HasValue || sessionId.Value == Guid.Empty)
        {
            var result = await sessionService.CreateSessionAsync(session);
            if (result.Success != true || result.Id == Guid.Empty)
            {
                return EventSessionSaveResult.Failed(
                    result.Message ?? "Program item could not be saved. Check the details and try again.");
            }

            sessionId = result.Id;
        }

        return await AssignSelectedProgramSectionAsync(
            sessionService,
            eventId,
            selectedSessionGroupId,
            sessionId.Value,
            "Session was saved, but it could not be assigned to the selected program section.");
    }

    public static async Task<EventSessionSaveResult> SaveUpdateSessionAsync(
        IEventSessionService sessionService,
        UpdateEventSessionDto session,
        Guid eventId,
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        Guid? selectedSessionGroupId,
        Guid? initialSessionGroupId)
    {
        ArgumentNullException.ThrowIfNull(sessionService);
        ArgumentNullException.ThrowIfNull(session);

        var result = await sessionService.UpdateSessionAsync(
            sessionId,
            expectedConcurrencyStamp,
            session);
        if (result.Success != true)
        {
            return EventSessionSaveResult.Failed(
                result.Message ?? "Program item could not be saved. Check the details and try again.");
        }

        if (!selectedSessionGroupId.HasValue || selectedSessionGroupId.Value == Guid.Empty)
        {
            if (!initialSessionGroupId.HasValue || initialSessionGroupId.Value == Guid.Empty)
                return EventSessionSaveResult.Succeeded(sessionId);

            var unassignResult = await sessionService.UnassignSessionFromGroupAsync(
                eventId,
                initialSessionGroupId.Value,
                sessionId);

            return unassignResult.Success == true
                ? EventSessionSaveResult.Succeeded(sessionId)
                : EventSessionSaveResult.Failed(
                    unassignResult.Message ?? "Session was saved, but it could not be removed from the previous program section.");
        }

        if (selectedSessionGroupId == initialSessionGroupId)
            return EventSessionSaveResult.Succeeded(sessionId);

        return await AssignSelectedProgramSectionAsync(
            sessionService,
            eventId,
            selectedSessionGroupId,
            sessionId,
            "Session was saved, but it could not be assigned to the selected program section.");
    }

    private static async Task<EventSessionSaveResult> AssignSelectedProgramSectionAsync(
        IEventSessionService sessionService,
        Guid eventId,
        Guid? selectedSessionGroupId,
        Guid sessionId,
        string fallbackErrorMessage)
    {
        if (!selectedSessionGroupId.HasValue || selectedSessionGroupId.Value == Guid.Empty)
            return EventSessionSaveResult.Succeeded(sessionId);

        var result = await sessionService.AssignSessionToGroupAsync(
            eventId,
            selectedSessionGroupId.Value,
            sessionId);

        return result.Success == true
            ? EventSessionSaveResult.Succeeded(sessionId)
            : EventSessionSaveResult.Failed(result.Message ?? fallbackErrorMessage, sessionId);
    }
}

internal sealed record EventSessionSaveResult(Guid? SessionId, string? ErrorMessage)
{
    public bool Success => ErrorMessage is null;

    public static EventSessionSaveResult Succeeded(Guid sessionId) => new(sessionId, null);

    public static EventSessionSaveResult Failed(string errorMessage, Guid? sessionId = null) => new(sessionId, errorMessage);
}
