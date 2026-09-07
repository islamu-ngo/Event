using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Admin.CustomProperties.Dialogs;

public partial class EditDefinitionFlagsDialog : ComponentBase
{
    public static Task<IDialogReference> ShowAsync(
        IDialogService dialogService,
        string title,
        DialogParameters parameters,
        DialogOptions options)
        => dialogService.ShowAsync<EditDefinitionFlagsDialog>(title, parameters, options);
}
