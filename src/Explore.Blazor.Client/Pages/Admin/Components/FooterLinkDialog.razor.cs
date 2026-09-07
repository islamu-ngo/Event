using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Admin.Components;

public partial class FooterLinkDialog : ComponentBase
{
    public static Task<IDialogReference> ShowAsync(
        IDialogService dialogService,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
        => dialogService.ShowAsync<FooterLinkDialog>(title, parameters ?? new DialogParameters(), options);
}
