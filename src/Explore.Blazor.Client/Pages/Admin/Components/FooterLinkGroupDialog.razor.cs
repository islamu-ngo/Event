using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Admin.Components;

public partial class FooterLinkGroupDialog : ComponentBase
{
    public static Task<IDialogReference> ShowAsync(
        IDialogService dialogService,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
        => dialogService.ShowAsync<FooterLinkGroupDialog>(title, parameters ?? new DialogParameters(), options);
}
