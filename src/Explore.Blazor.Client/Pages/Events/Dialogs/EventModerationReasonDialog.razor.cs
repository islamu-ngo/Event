using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Events.Dialogs;

public sealed record EventModerationReasonOption(string Code, string Label);

public sealed record EventModerationDialogResult(string ReasonCode);

public partial class EventModerationReasonDialog : ComponentBase
{
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter]
    public string DialogTitle { get; set; } = "Moderate Event";

    [Parameter]
    public string Message { get; set; } = string.Empty;

    [Parameter]
    public string ConfirmText { get; set; } = "Confirm";

    [Parameter]
    public string CancelText { get; set; } = "Cancel";

    [Parameter]
    public string ConfirmIcon { get; set; } = Icons.Material.Filled.Check;

    [Parameter]
    public string TitleIcon { get; set; } = Icons.Material.Filled.AdminPanelSettings;

    [Parameter]
    public Color ConfirmColor { get; set; } = Color.Primary;

    [Parameter]
    public Severity AlertSeverity { get; set; } = Severity.Info;

    [Parameter]
    public bool RequiresIrreversibleConfirmation { get; set; }

    [Parameter]
    public IReadOnlyList<EventModerationReasonOption> ReasonOptions { get; set; } = [];

    private string? _selectedReasonCode;
    private bool _confirmedIrreversible;

    private bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_selectedReasonCode)
        && (!RequiresIrreversibleConfirmation || _confirmedIrreversible);

    protected override void OnInitialized()
    {
        _selectedReasonCode = ReasonOptions.FirstOrDefault()?.Code;
    }

    public static Task<IDialogReference> ShowAsync(
        IDialogService dialogService,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
        => dialogService.ShowAsync<EventModerationReasonDialog>(title, parameters ?? new DialogParameters(), options);

    private void Cancel() => MudDialog.Cancel();

    private void Submit()
    {
        if (!CanSubmit)
        {
            return;
        }

        MudDialog.Close(DialogResult.Ok(new EventModerationDialogResult(_selectedReasonCode!)));
    }
}
