namespace Explore.Blazor.Client.Pages.Events.Components;

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

public partial class AgendaMillerColumns
{
    [Parameter] public Guid EventId { get; set; }
    [Parameter] public List<EventDayListDto>? Days { get; set; }
    [Parameter] public List<EventAgendaItemListDto>? AgendaItems { get; set; }
    [Parameter] public bool CanManage { get; set; }
    [Parameter] public EventCallback OnDataChanged { get; set; }

    [Inject] private IEventAgendaItemService AgendaItemService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ILogger<AgendaMillerColumns> Logger { get; set; } = default!;

    private EventDayListDto? SelectedDay { get; set; }
    private EventAgendaItemListDto? SelectedItem { get; set; }

    private List<EventDayListDto> _days = new();
    private List<EventAgendaItemListDto> _items = new();
    private List<EventAgendaItemListDto> _filteredItems = new();

    protected override void OnParametersSet()
    {
        _days = Days ?? new();
        _items = AgendaItems ?? new();
        UpdateFilteredItems();
    }

    private void SelectDay(EventDayListDto day)
    {
        SelectedDay = day;
        SelectedItem = null;
        UpdateFilteredItems();
    }

    private void SelectItem(EventAgendaItemListDto item)
    {
        SelectedItem = item;
    }

    private void UpdateFilteredItems()
    {
        if (SelectedDay is null)
        {
            _filteredItems = new();
            return;
        }

        _filteredItems = _items
            .Where(i => i.LocalStartDate == SelectedDay.LocalDate)
            .OrderBy(i => i.LocalStartTime)
            .ToList();
    }

    private async Task OpenAddDayDialog()
    {
        var parameters = new DialogParameters<EventDayEditorDialog>
        {
            { d => d.EventId, EventId },
            { d => d.IsNew, true },
        };
        var dialog = await DialogService.ShowAsync<EventDayEditorDialog>("Add Day", parameters, DialogOptionsFactory.Small());
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            await OnDataChanged.InvokeAsync();
        }
    }

    private async Task OpenAddAgendaItemDialog()
    {
        var parameters = new DialogParameters<EventAgendaItemEditorDialog>
        {
            { d => d.EventId, EventId },
            { d => d.IsNew, true },
            { d => d.Days, _days },
            { d => d.PreselectedDayId, SelectedDay?.Id },
        };
        var dialog = await DialogService.ShowAsync<EventAgendaItemEditorDialog>("Add Agenda Item", parameters, DialogOptionsFactory.Small());
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            SelectedItem = null;
            await OnDataChanged.InvokeAsync();
        }
    }

    private async Task OpenEditAgendaItemDialog()
    {
        if (SelectedItem is null) return;

        var parameters = new DialogParameters<EventAgendaItemEditorDialog>
        {
            { d => d.EventId, EventId },
            { d => d.ItemId, SelectedItem.Id },
            { d => d.IsNew, false },
            { d => d.Days, _days },
        };
        var dialog = await DialogService.ShowAsync<EventAgendaItemEditorDialog>("Edit Agenda Item", parameters, DialogOptionsFactory.Small());
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            SelectedItem = null;
            await OnDataChanged.InvokeAsync();
        }
    }

    private async Task ConfirmDeleteItem(EventAgendaItemListDto? item)
    {
        if (item is null) return;

        bool? result = await DialogService.ShowMessageBoxAsync(
            "Delete Agenda Item",
            $"Are you sure you want to delete \"{item.Title}\"?",
            yesText: "Delete",
            cancelText: "Cancel");

        if (result != true) return;

        try
        {
            await AgendaItemService.DeleteAgendaItemAsync(item.Id ?? Guid.Empty);
            SelectedItem = null;
            await OnDataChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete agenda item {Id}", item.Id);
        }
    }
}
