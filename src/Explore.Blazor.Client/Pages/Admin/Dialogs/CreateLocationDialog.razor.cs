using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Admin.Dialogs;

public partial class CreateLocationDialog : ComponentBase
{
    public static Task<IDialogReference> ShowAsync(
        IDialogService dialogService,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
        => dialogService.ShowAsync<CreateLocationDialog>(title, parameters ?? new DialogParameters(), options);
}
