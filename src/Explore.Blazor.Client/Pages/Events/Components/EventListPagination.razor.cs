using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Pages.Events.Components;

public partial class EventListPagination : ComponentBase
{
    [Parameter, EditorRequired]
    public int CurrentPage { get; set; } = 1;

    [Parameter, EditorRequired]
    public int TotalPages { get; set; }

    [Parameter, EditorRequired]
    public int PageSize { get; set; } = 20;

    [Parameter, EditorRequired]
    public int TotalCount { get; set; }

    [Parameter]
    public bool IsLoading { get; set; }

    [Parameter]
    public EventCallback<int> CurrentPageChanged { get; set; }

    [Parameter]
    public EventCallback<int> PageSizeChanged { get; set; }

    private int StartItem => TotalCount == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;

    private int EndItem => Math.Min(CurrentPage * PageSize, TotalCount);

    private static readonly int[] PageSizeOptions = [12, 20, 50];

    private async Task HandlePageChanged(int page)
    {
        if (page == CurrentPage || IsLoading) return;
        await CurrentPageChanged.InvokeAsync(page);
    }

    private async Task HandlePageSizeChanged(int size)
    {
        if (size == PageSize || IsLoading) return;
        await PageSizeChanged.InvokeAsync(size);
    }
}
