using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Explore.Blazor.Client.Helpers;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Components.EventReporting;

public partial class MyReportsPage : ComponentBase
{
    private const int PageSize = 10;
    private const string UpdateCommunicationConsentRelation = "update-communication-consent";

    [Inject] private IEventReportingService EventReportingService { get; set; } = default!;
    [Inject] private IAccessibilityAnnouncerService AnnouncerService { get; set; } = default!;
    [Inject] private IAccessibilityFocusService FocusService { get; set; } = default!;
    [Inject] private ILogger<MyReportsPage> Logger { get; set; } = default!;

    private IReadOnlyList<HalResourceOfMyEventReportDto> _reports = [];
    private readonly Dictionary<Guid, ConsentEditorState> _consentEditors = [];
    private bool _isLoading = true;
    private bool _hasPrevious;
    private bool _hasNext;
    private int _pageNumber = 1;
    private int _totalPages;
    private string? _errorMessage;

    protected override async Task OnInitializedAsync()
    {
        await LoadPageAsync(1);
    }

    private Task RefreshAsync() => LoadPageAsync(_pageNumber);

    private Task PreviousPageAsync()
        => _hasPrevious ? LoadPageAsync(_pageNumber - 1) : Task.CompletedTask;

    private Task NextPageAsync()
        => _hasNext ? LoadPageAsync(_pageNumber + 1) : Task.CompletedTask;

    private async Task LoadPageAsync(int pageNumber)
    {
        _isLoading = true;
        _errorMessage = null;

        try
        {
            var result = await EventReportingService.GetMyReportsAsync(pageNumber, PageSize);
            _reports = result.Reports;
            _pageNumber = result.PageNumber;
            _totalPages = result.TotalPages;
            _hasPrevious = result.HasPrevious;
            _hasNext = result.HasNext;
            _consentEditors.Clear();

            await AnnouncerService.AnnouncePoliteAsync(
                _reports.Count == 0 ? "No event reports found." : $"Loaded {_reports.Count} event reports.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _errorMessage = "Reports could not be loaded.";
            await AnnouncerService.AnnounceAssertiveAsync(_errorMessage);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static bool CanEditCommunicationConsent(HalResourceOfMyEventReportDto report)
        => report.Id is { } reportId
           && reportId != Guid.Empty
           && report.HasLink(UpdateCommunicationConsentRelation);

    private ConsentEditorState? GetConsentEditor(HalResourceOfMyEventReportDto report)
    {
        if (report.Id is not { } reportId || reportId == Guid.Empty)
        {
            return null;
        }

        if (!_consentEditors.TryGetValue(reportId, out var editor))
        {
            editor = new ConsentEditorState(report);
            _consentEditors.Add(reportId, editor);
        }

        return editor;
    }

    private void StartConsentEdit(HalResourceOfMyEventReportDto report)
    {
        if (!CanEditCommunicationConsent(report) || GetConsentEditor(report) is not { } editor)
        {
            return;
        }

        editor.Reset(report);
        editor.IsEditing = true;
    }

    private async Task CancelConsentEditAsync(HalResourceOfMyEventReportDto report)
    {
        if (GetConsentEditor(report) is not { } editor || editor.IsSaving)
        {
            return;
        }

        editor.Reset(report);
        await RestoreFocusAsync(report, GetConsentEditButtonId);
    }

    private async Task SaveConsentAsync(HalResourceOfMyEventReportDto report)
    {
        if (!CanEditCommunicationConsent(report)
            || GetConsentEditor(report) is not { } editor
            || editor.IsSaving
            || !editor.IsChanged(report))
        {
            return;
        }

        editor.IsSaving = true;
        editor.Error = null;

        try
        {
            var result = await EventReportingService.UpdateCommunicationConsentAsync(
                report,
                editor.ReportCaseUpdatesConsent,
                editor.ReportFollowUpContactConsent);

            if (!result.Success || result.Report is null)
            {
                editor.Error = result.Message;
                return;
            }

            _reports = _reports
                .Select(item => item.Id == report.Id ? result.Report : item)
                .ToArray();
            editor.Reset(result.Report);
            await AnnouncerService.AnnouncePoliteAsync(result.Message);
            await RestoreFocusAsync(result.Report, GetConsentSummaryId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            editor.IsSaving = false;
        }
    }

    private async Task RestoreFocusAsync(
        HalResourceOfMyEventReportDto report,
        Func<Guid, string> targetIdFactory)
    {
        if (report.Id is not { } reportId || reportId == Guid.Empty)
        {
            return;
        }

        await InvokeAsync(StateHasChanged);
        try
        {
            await FocusService.FocusByIdAsync(targetIdFactory(reportId), preventScroll: true);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not restore focus after an event-report consent action");
        }
    }

    private static string GetConsentEditButtonId(Guid reportId)
        => $"report-consent-edit-{reportId:N}";

    private static string GetConsentSummaryId(Guid reportId)
        => $"report-consent-summary-{reportId:N}";

    private static string GetCaseUpdatesDescriptionId(Guid reportId)
        => $"report-case-updates-description-{reportId:N}";

    private static string GetFollowUpDescriptionId(Guid reportId)
        => $"report-follow-up-description-{reportId:N}";

    private static string FormatConsent(bool consent)
        => consent ? "Enabled" : "Disabled";

    private static string GetEventHref(HalResourceOfMyEventReportDto report)
        => report.EventId is { } eventId && eventId != Guid.Empty
            ? $"/events/{eventId}"
            : "/events";

    private static string FormatEventId(Guid? eventId)
        => eventId is { } value && value != Guid.Empty
            ? $"Event {value.ToString("N")[..8]}"
            : "Event";

    private static string FormatDate(DateTimeOffset? timestamp)
        => timestamp?.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture) ?? "-";

    private static Color GetStatusColor(string? statusCode)
    {
        return statusCode switch
        {
            "submitted" or "triaged" => Color.Info,
            "under_review" or "escalated" => Color.Warning,
            "actioned" => Color.Success,
            "dismissed" or "duplicate" => Color.Default,
            "closed" => Color.Secondary,
            _ => Color.Default
        };
    }

    private sealed class ConsentEditorState(HalResourceOfMyEventReportDto report)
    {
        public bool IsEditing { get; set; }
        public bool IsSaving { get; set; }
        public bool ReportCaseUpdatesConsent { get; set; } = report.ReportCaseUpdatesConsent;
        public bool ReportFollowUpContactConsent { get; set; } = report.ReportFollowUpContactConsent;
        public string? Error { get; set; }

        public bool IsChanged(HalResourceOfMyEventReportDto authoritativeReport)
            => ReportCaseUpdatesConsent != authoritativeReport.ReportCaseUpdatesConsent
               || ReportFollowUpContactConsent != authoritativeReport.ReportFollowUpContactConsent;

        public void Reset(HalResourceOfMyEventReportDto authoritativeReport)
        {
            IsEditing = false;
            ReportCaseUpdatesConsent = authoritativeReport.ReportCaseUpdatesConsent;
            ReportFollowUpContactConsent = authoritativeReport.ReportFollowUpContactConsent;
            Error = null;
        }
    }
}
