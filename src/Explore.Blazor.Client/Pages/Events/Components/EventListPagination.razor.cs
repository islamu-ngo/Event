using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Pages.Events.Components;

public partial class EventListPagination : ComponentBase, IDisposable
{
    [Parameter, EditorRequired]
    public int LoadedCount { get; set; }

    [Parameter, EditorRequired]
    public int SnapshotCount { get; set; }

    [Parameter, EditorRequired]
    public int PageSize { get; set; } = 20;

    [Parameter, EditorRequired]
    public bool HasMore { get; set; }

    [Parameter]
    public bool IsLoading { get; set; }

    [Parameter]
    public EventCallback NextRequested { get; set; }

    [Parameter]
    public EventCallback<int> PageSizeChanged { get; set; }

    private static readonly int[] PageSizeOptions = [12, 20, 50, 100];
    private string T(string key, string fallback) => Translation.T(key, fallback);

    protected override void OnInitialized() => Translation.OnLanguageChanged += HandleLanguageChanged;

    private void HandleLanguageChanged(string languageCode) => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Translation.OnLanguageChanged -= HandleLanguageChanged;

    private async Task HandleNextRequested()
    {
        if (!HasMore || IsLoading) return;
        await NextRequested.InvokeAsync();
    }

    private async Task HandlePageSizeChanged(int size)
    {
        if (size == PageSize || IsLoading) return;
        await PageSizeChanged.InvokeAsync(size);
    }
}
