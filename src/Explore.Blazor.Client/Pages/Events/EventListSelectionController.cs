using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Events;

internal sealed class EventListSelectionController
{
    private readonly List<EventListDto> _loadedEvents = [];

    public void TrackLoadedEvents(IEnumerable<EventListDto> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var evt in events)
        {
            if (evt.Id.HasValue && !_loadedEvents.Any(loaded => loaded.Id == evt.Id))
            {
                _loadedEvents.Add(evt);
            }
        }
    }

    public void ClearLoadedEvents()
    {
        _loadedEvents.Clear();
    }

    public bool CanNavigatePrevious(EventListDto? selectedEvent)
    {
        return FindSelectedEventIndex(selectedEvent) > 0;
    }

    public bool CanNavigateNext(EventListDto? selectedEvent)
    {
        var selectedIndex = FindSelectedEventIndex(selectedEvent);
        return selectedIndex >= 0 && selectedIndex < _loadedEvents.Count - 1;
    }

    public EventListDto? GetPreviousEvent(EventListDto? selectedEvent)
    {
        var selectedIndex = FindSelectedEventIndex(selectedEvent);
        return selectedIndex > 0 ? _loadedEvents[selectedIndex - 1] : null;
    }

    public EventListDto? GetNextEvent(EventListDto? selectedEvent)
    {
        var selectedIndex = FindSelectedEventIndex(selectedEvent);
        return selectedIndex >= 0 && selectedIndex < _loadedEvents.Count - 1
            ? _loadedEvents[selectedIndex + 1]
            : null;
    }

    private int FindSelectedEventIndex(EventListDto? selectedEvent)
    {
        return selectedEvent?.Id is Guid selectedEventId
            ? _loadedEvents.FindIndex(evt => evt.Id == selectedEventId)
            : -1;
    }
}
