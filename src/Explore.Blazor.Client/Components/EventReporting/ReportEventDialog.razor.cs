using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Components.EventReporting;

public partial class ReportEventDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    [Inject] private IEventReportingService EventReportingService { get; set; } = default!;
    [Inject] private IAccessibilityAnnouncerService AnnouncerService { get; set; } = default!;

    [Parameter] public Guid EventId { get; set; }
    [Parameter] public string? EventTitle { get; set; }
    [Parameter] public string? FixedSubcategoryCode { get; set; }

    private HalResourceOfEventReportOptionsDto? _options;
    private string? _selectedReasonCode;
    private string? _reporterText;
    private string? _errorMessage;
    private bool _reportCaseUpdatesConsent;
    private bool _reportFollowUpContactConsent;
    private bool _isLoadingOptions = true;
    private bool _isSubmitting;

    private IReadOnlyList<ReasonOptions2> ReasonOptions => _options?.ReasonOptions?.ToArray() ?? [];

    private ReasonOptions2? SelectedReason =>
        ReasonOptions.FirstOrDefault(option => string.Equals(option.ReasonCode, _selectedReasonCode, StringComparison.Ordinal));

    private string? FixedIntentLabel => FixedSubcategoryCode switch
    {
        "event_correction_suggestion" => "Suggest a correction",
        "unsafe_external_link" => "Report unsafe link",
        _ => null
    };

    private int MaxReporterTextLength => Math.Max(1, _options?.MaxReporterTextLength ?? 2_000);
    private int ReporterTextLength => _reporterText?.Length ?? 0;
    private bool HasReporterText => !string.IsNullOrWhiteSpace(_reporterText);
    private string CaseUpdatesDescriptionId => $"report-case-updates-description-{EventId:N}";
    private string FollowUpContactDescriptionId => $"report-follow-up-description-{EventId:N}";

    private Color CounterColor => ReporterTextLength > MaxReporterTextLength ? Color.Error : Color.Secondary;

    private bool CanSubmit =>
        _options?.IsReportable == true
        && !_isLoadingOptions
        && !_isSubmitting
        && !string.IsNullOrWhiteSpace(_selectedReasonCode)
        && HasReporterText
        && ReporterTextLength <= MaxReporterTextLength;

    protected override async Task OnInitializedAsync()
    {
        _options = await EventReportingService.GetOptionsAsync(EventId);
        _selectedReasonCode = string.IsNullOrWhiteSpace(FixedSubcategoryCode)
            ? ReasonOptions.FirstOrDefault()?.ReasonCode
            : ReasonOptions.FirstOrDefault(option => string.Equals(option.ReasonCode, "other", StringComparison.Ordinal))?.ReasonCode;
        _isLoadingOptions = false;
    }

    public static Task<IDialogReference> ShowAsync(
        IDialogService dialogService,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
        => dialogService.ShowAsync<ReportEventDialog>(title, parameters ?? new DialogParameters(), options);

    private void Cancel() => MudDialog.Cancel();

    private Task OnReporterTextChanged(string? value)
    {
        _reporterText = value;
        _errorMessage = null;
        return Task.CompletedTask;
    }

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
        {
            var errorMessage = ReporterTextLength > MaxReporterTextLength
                ? $"Details must be {MaxReporterTextLength} characters or fewer."
                : !HasReporterText
                    ? "Add details before submitting."
                    : "Choose a reason before submitting.";
            _errorMessage = errorMessage;
            return;
        }

        _isSubmitting = true;
        _errorMessage = null;

        var request = new SubmitEventReportDto
        {
            EventId = EventId,
            ReasonCode = _selectedReasonCode,
            SubcategoryCode = FixedSubcategoryCode,
            ReporterText = _reporterText!.Trim(),
            ReportCaseUpdatesConsent = _reportCaseUpdatesConsent,
            ReportFollowUpContactConsent = _reportFollowUpContactConsent,
            ReporterLocale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
        };

        try
        {
            var result = await EventReportingService.SubmitAsync(request);
            if (result.Success)
            {
                await AnnouncerService.AnnouncePoliteAsync("Event report submitted.");
                MudDialog.Close(DialogResult.Ok(result));
                return;
            }

            _errorMessage = result.Message;
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    private string GetUnavailableMessage()
        => _options?.UnavailableReasonMessage
           ?? "Reporting is not available for this event.";
}
