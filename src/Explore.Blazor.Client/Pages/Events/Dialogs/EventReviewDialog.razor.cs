using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Events.Dialogs;

public partial class EventReviewDialog : ComponentBase
{
    public static Task<IDialogReference> ShowAsync(
        IDialogService dialogService,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
        => dialogService.ShowAsync<EventReviewDialog>(title, parameters ?? new DialogParameters(), options);
}
