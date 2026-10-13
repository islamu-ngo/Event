using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Pages.Studio;

public sealed class StudioEventContextState : IDisposable
{
    private readonly IEventService _eventService;
    private readonly AuthenticationStateProvider? _authentication;
    private readonly object _sync = new();
    private Task<EventDto?>? _loadTask;
    private bool _disposed;

    public StudioEventContextState(IEventService eventService, AuthenticationStateProvider? authentication = null)
    {
        _eventService = eventService;
        _authentication = authentication;
        if (_authentication is not null)
            _authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    public event Action? Changed;

    public Guid? EventId { get; private set; }
    public EventDto? Event { get; private set; }
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }
    public long Generation { get; private set; }

    public Task<EventDto?> LoadAsync(Guid eventId) => StartLoadAsync(eventId, force: false);

    public Task<EventDto?> ReloadAsync(Guid eventId) => StartLoadAsync(eventId, force: true);

    private Task<EventDto?> StartLoadAsync(Guid eventId, bool force)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(eventId, Guid.Empty);

        Task<EventDto?> loadTask;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!force && EventId == eventId && _loadTask is not null
                && (!_loadTask.IsCompleted || Event is not null))
            {
                return _loadTask;
            }

            var generation = ++Generation;
            Event = null;
            EventId = eventId;
            ErrorMessage = null;
            IsLoading = true;
            loadTask = LoadCoreAsync(eventId, generation);
            _loadTask = loadTask;
        }

        Changed?.Invoke();
        return loadTask;
    }

    private async Task<EventDto?> LoadCoreAsync(Guid eventId, long generation)
    {
        EventDto? resource = null;
        string? errorMessage = null;

        try
        {
            resource = await _eventService.GetEventByIdAsync(eventId);
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
            if (_disposed || Generation != generation)
            {
                return null;
            }

            Event = resource;
            ErrorMessage = errorMessage;
            IsLoading = false;
        }

        Changed?.Invoke();
        lock (_sync)
        {
            return !_disposed && Generation == generation ? resource : null;
        }
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            if (_disposed) return;
            ++Generation;
            _loadTask = null;
            EventId = null;
            Event = null;
            IsLoading = false;
            ErrorMessage = null;
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            ++Generation;
            _loadTask = null;
            EventId = null;
            Event = null;
            IsLoading = false;
            ErrorMessage = null;
            Changed = null;
        }
        if (_authentication is not null)
            _authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> _) => Invalidate();
}
