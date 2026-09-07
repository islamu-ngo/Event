using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Events.Dialogs;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Studio;

public abstract class StudioEventPageBase : ComponentBase, IDisposable
{
    private CancellationTokenSource? _loadCancellation;
    private Task<EventCreationEligibility>? _eligibilityTask;
    private Guid? _loadedActorId;
    private int _loadVersion;
    private bool _disposed;

    [Inject] protected IEventService EventService { get; set; } = null!;
    [Inject] protected IEventCreationEligibilityService EligibilityService { get; set; } = null!;
    [Inject] protected IUiShellContextService ShellContextService { get; set; } = null!;
    [Inject] protected UiShellState ShellState { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected IDialogService DialogService { get; set; } = null!;
    [Inject] protected IAccessibilityAnnouncerService Announcer { get; set; } = null!;
    [Inject] protected IAccessibilityFocusService FocusService { get; set; } = null!;
    [Inject] protected ILogger<StudioEventPageBase> Logger { get; set; } = null!;

    protected PaginatedResult<EventListDto> Result { get; private set; } =
        PaginatedResult<EventListDto>.Empty(pageSize: 100);

    protected bool IsLoading { get; private set; } = true;
    protected string? ErrorMessage { get; private set; }
    protected string ActiveActorName => ShellState.ActiveActor?.DisplayName ?? "Personal Studio";
    protected bool CanCreate { get; private set; }
    protected int UpcomingCount => Result.Items.Count(item => item.IsPast != true);
    protected int EditableCount => Result.Items.Count(item => item.HasHalLink("edit"));

    protected override async Task OnInitializedAsync()
    {
        var context = await ShellContextService.GetCachedContextAsync();
        ShellState.ReconcileActiveActors(context?.ManagedActors, context?.PinnedActorId);
        ShellState.Changed += OnShellStateChanged;
        await ReloadAsync();
    }

    protected async Task ReloadAsync()
    {
        var version = Interlocked.Increment(ref _loadVersion);
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _loadCancellation, cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        var actorId = ShellState.ActiveActorId;
        _loadedActorId = actorId;
        IsLoading = true;
        ErrorMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            _eligibilityTask ??= EligibilityService.GetEligibilityAsync();
            var result = actorId.HasValue
                ? await EventService.GetManagedEventsByActorAsync(actorId.Value, 1, 100, cancellation.Token)
                : await EventService.GetMyEventsPagedAsync(1, 100, cancellation.Token);
            var eligibility = await _eligibilityTask;

            if (_disposed || version != _loadVersion)
            {
                return;
            }

            Result = result;
            CanCreate = eligibility.CanCreate && result.HasHalLink("create");
            await Announcer.AnnouncePoliteAsync($"{result.TotalCount} Studio events loaded");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_disposed || version != _loadVersion)
            {
                return;
            }

            Logger.LogError(exception, "Failed to load Studio events for actor {ActorId}", actorId);
            ErrorMessage = "Studio events could not be loaded. Try again.";
            await Announcer.AnnounceAssertiveAsync(ErrorMessage);
        }
        finally
        {
            if (!_disposed && version == _loadVersion)
            {
                IsLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    protected bool CanEdit(EventListDto item) => item.Id.HasValue && item.HasHalLink("edit");

    protected bool CanDelete(EventListDto item) => item.Id.HasValue && item.HasHalLink("delete");

    protected string EditRoute(EventListDto item) => $"/events/{item.Id}/edit";

    protected async Task OpenDeleteDialogAsync(EventListDto item)
    {
        if (!CanDelete(item))
        {
            return;
        }

        var parameters = new DialogParameters
        {
            ["EventId"] = item.Id,
            ["EventTitle"] = item.Title
        };

        await FocusService.SaveFocusAsync();
        var dialog = await DeleteEventDialog.ShowAsync(
            DialogService,
            "Delete Event",
            parameters,
            DialogOptionsFactory.Small());
        var result = await dialog.Result;
        await FocusService.RestoreFocusAsync();

        if (result is { Canceled: false })
        {
            await ReloadAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShellState.Changed -= OnShellStateChanged;
        Interlocked.Increment(ref _loadVersion);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnShellStateChanged()
    {
        if (_disposed || _loadedActorId == ShellState.ActiveActorId)
        {
            return;
        }

        _ = InvokeAsync(ReloadAsync);
    }
}
