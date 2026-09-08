using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Pages.Studio;

public sealed class StudioEventContextState(IEventService eventService)
{
    private readonly object _sync = new();
    private Task<EventDto?>? _loadTask;

    public event Action? Changed;

    public Guid? EventId { get; private set; }
    public EventDto? Event { get; private set; }
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }

    public Task<EventDto?> LoadAsync(Guid eventId) => StartLoadAsync(eventId, force: false);

    public Task<EventDto?> ReloadAsync(Guid eventId) => StartLoadAsync(eventId, force: true);

    private Task<EventDto?> StartLoadAsync(Guid eventId, bool force)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(eventId, Guid.Empty);

        Task<EventDto?> loadTask;
        lock (_sync)
        {
            if (!force && EventId == eventId && _loadTask is not null)
            {
                return _loadTask;
            }

            if (EventId != eventId)
            {
                Event = null;
            }

            EventId = eventId;
            ErrorMessage = null;
            IsLoading = true;
            loadTask = LoadCoreAsync(eventId);
            _loadTask = loadTask;
        }

        Changed?.Invoke();
        return loadTask;
    }

    private async Task<EventDto?> LoadCoreAsync(Guid eventId)
    {
        EventDto? resource = null;
        string? errorMessage = null;

        try
        {
            resource = await eventService.GetEventByIdAsync(eventId);
            if (resource is null)
            {
                errorMessage = "This event could not be loaded.";
            }
        }
        catch
        {
            errorMessage = "This event could not be loaded.";
        }

        lock (_sync)
        {
            if (EventId != eventId)
            {
                return resource;
            }

            Event = resource;
            ErrorMessage = errorMessage;
            IsLoading = false;
        }

        Changed?.Invoke();
        return resource;
    }
}
