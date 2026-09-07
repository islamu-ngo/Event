using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Components.Moderation;

public partial class ModerationReportQueuePage : ComponentBase
{
    private const int PageSize = 20;
    private const string AllFilterValue = "";

    private static readonly IReadOnlyList<FilterOption> StatusOptions =
    [
        new(AllFilterValue, "All statuses"),
        new("submitted", "Submitted"),
        new("triaged", "Triaged"),
        new("under_review", "Under review"),
        new("actioned", "Actioned"),
        new("dismissed", "Dismissed"),
        new("duplicate", "Duplicate"),
        new("escalated", "Escalated"),
        new("closed", "Closed")
    ];

    private static readonly IReadOnlyList<FilterOption> CaseStatusOptions =
    [
        new(AllFilterValue, "All case states"),
        new("open", "Open"),
        new("assigned", "Assigned"),
        new("waiting_external", "Waiting external"),
        new("waiting_reporter", "Waiting reporter"),
        new("decision_ready", "Decision ready"),
        new("closed", "Closed")
    ];

    private static readonly IReadOnlyList<FilterOption> PriorityOptions =
    [
        new(AllFilterValue, "All priorities"),
        new("low", "Low"),
        new("normal", "Normal"),
        new("high", "High"),
        new("urgent", "Urgent")
    ];

    private static readonly IReadOnlyList<FilterOption> SortOptions =
    [
        new("created_at", "Created"),
        new("updated_at", "Updated"),
        new("priority", "Priority"),
        new("status", "Status"),
        new("reason_code", "Reason")
    ];

    [Parameter] public Guid EventId { get; set; }

    [Inject] private IEventReportModerationService ModerationService { get; set; } = default!;
    [Inject] private IAccessibilityAnnouncerService AnnouncerService { get; set; } = default!;
    [Inject] private IAccessibilityFocusService FocusService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private IReadOnlyList<HalResourceOfModerationReportQueueItemDto> _reports = [];
    private HalResourceOfModerationReportQueueItemDto? _selectedQueueItem;
    private HalResourceOfModerationReportDetailDto? _selectedDetail;
    private Guid _loadedEventId;
    private bool _isLoading = true;
    private bool _isDetailLoading;
    private bool _isActionRunning;
    private bool _hasPrevious;
    private bool _hasNext;
    private int _pageNumber = 1;
    private int _totalPages;
    private int _totalCount;
    private string? _errorMessage;
    private string? _detailErrorMessage;
    private string _statusCode = AllFilterValue;
    private string _caseStatusCode = AllFilterValue;
    private string _priorityCode = AllFilterValue;
    private string _queueCode = string.Empty;
    private string _reasonCode = string.Empty;
    private string _sortBy = "created_at";
    private bool _sortDescending = true;
    private bool _openOnly = true;
    private bool _unassignedOnly;

    protected override async Task OnParametersSetAsync()
    {
        if (EventId == Guid.Empty)
        {
            _isLoading = false;
            _errorMessage = "Event id is required.";
            return;
        }

        if (_loadedEventId == EventId)
        {
            return;
        }

        _loadedEventId = EventId;
        _selectedQueueItem = null;
        _selectedDetail = null;
        await LoadPageAsync(1);
    }

    private Task RefreshAsync() => LoadPageAsync(_pageNumber);

    private Task ApplyFiltersAsync() => LoadPageAsync(1);

    private async Task ClearFiltersAsync()
    {
        _statusCode = AllFilterValue;
        _caseStatusCode = AllFilterValue;
        _priorityCode = AllFilterValue;
        _queueCode = string.Empty;
        _reasonCode = string.Empty;
        _sortBy = "created_at";
        _sortDescending = true;
        _openOnly = true;
        _unassignedOnly = false;
        await LoadPageAsync(1);
    }

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
            var result = await ModerationService.GetQueueAsync(EventId, BuildQuery(pageNumber));
            _reports = result.Reports;
            _pageNumber = result.PageNumber;
            _totalPages = result.TotalPages;
            _totalCount = result.TotalCount;
            _hasPrevious = result.HasPrevious;
            _hasNext = result.HasNext;

            if (_selectedQueueItem?.Id is Guid selectedReportId &&
                !_reports.Any(report => report.Id == selectedReportId))
            {
                _selectedQueueItem = null;
                _selectedDetail = null;
            }

            await AnnouncerService.AnnouncePoliteAsync(
                _reports.Count == 0 ? "No moderation reports found." : $"Loaded {_reports.Count} moderation reports.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _errorMessage = "Moderation reports could not be loaded.";
            await AnnouncerService.AnnounceAssertiveAsync(_errorMessage);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private ModerationReportQueueQueryState BuildQuery(int pageNumber)
        => new()
        {
            StatusCode = NullIfAll(_statusCode),
            CaseStatusCode = NullIfAll(_caseStatusCode),
            PriorityCode = NullIfAll(_priorityCode),
            QueueCode = _queueCode,
            ReasonCode = _reasonCode,
            SortBy = _sortBy,
            SortDescending = _sortDescending,
            OpenOnly = _openOnly,
            UnassignedOnly = _unassignedOnly,
            PageNumber = pageNumber,
            PageSize = PageSize
        };

    private async Task OpenReportAsync(HalResourceOfModerationReportQueueItemDto report)
    {
        if (report.Id is not Guid reportId || report.EventId is not Guid eventId)
        {
            return;
        }

        _selectedQueueItem = report;
        await LoadDetailAsync(eventId, reportId);
    }

    private async Task RefreshSelectedDetailAsync()
    {
        if (_selectedQueueItem?.Id is not Guid reportId)
        {
            return;
        }

        await LoadDetailAsync(EventId, reportId);
    }

    private async Task OpenActionDialogAsync(ModerationReportActionKind actionKind)
    {
        if (_selectedDetail is null || _isActionRunning || !HasActionLink(actionKind))
        {
            return;
        }

        var parameters = new DialogParameters<ModerationReportActionDialog>
        {
            { dialog => dialog.ActionKind, actionKind },
            { dialog => dialog.ReportId, _selectedDetail.Id },
            { dialog => dialog.CurrentCaseStatusName, _selectedDetail.CurrentCase?.StatusName },
            { dialog => dialog.CurrentQueueCode, _selectedDetail.CurrentCase?.QueueCode },
            { dialog => dialog.CurrentPriorityCode, _selectedDetail.CurrentCase?.PriorityCode },
            { dialog => dialog.CurrentAssigneeUserId, _selectedDetail.CurrentCase?.AssignedModeratorUserId },
            { dialog => dialog.DefaultReasonCode, _selectedDetail.ReasonCode },
            { dialog => dialog.LatestDecisionId, GetLatestDecisionId() }
        };

        await FocusService.SaveFocusAsync();
        DialogResult? result;
        try
        {
            var dialog = await ModerationReportActionDialog.ShowAsync(
                DialogService,
                GetActionTitle(actionKind),
                parameters,
                DialogOptionsFactory.Medium());
            result = await dialog.Result;
        }
        finally
        {
            await FocusService.RestoreFocusAsync();
        }

        if (result is { Canceled: false, Data: ModerationReportActionDialogResult actionResult })
        {
            await ExecuteActionAsync(actionResult);
        }
    }

    private async Task ExecuteActionAsync(ModerationReportActionDialogResult actionResult)
    {
        if (!HasActionLink(actionResult.ActionKind))
        {
            _detailErrorMessage = "This report action is no longer available. Refresh the report and try again.";
            await AnnouncerService.AnnounceAssertiveAsync(_detailErrorMessage);
            return;
        }

        var actionValidationMessage = ValidateActionResult(actionResult);
        if (!string.IsNullOrWhiteSpace(actionValidationMessage))
        {
            _detailErrorMessage = actionValidationMessage;
            await AnnouncerService.AnnounceAssertiveAsync(_detailErrorMessage);
            return;
        }

        if (!TryGetActionContext(out var eventId, out var reportId, out var caseId, out var expectedStamp))
        {
            _detailErrorMessage = "Refresh the report before applying this action.";
            await AnnouncerService.AnnounceAssertiveAsync(_detailErrorMessage);
            return;
        }

        _isActionRunning = true;
        _detailErrorMessage = null;

        try
        {
            var result = actionResult.ActionKind switch
            {
                ModerationReportActionKind.Triage => await ModerationService.TriageAsync(
                    eventId,
                    reportId,
                    new TriageModerationReportRequestDto
                    {
                        CaseId = caseId,
                        ExpectedCaseConcurrencyStamp = expectedStamp,
                        QueueCode = actionResult.QueueCode ?? string.Empty,
                        Priority = actionResult.Priority ?? EventReportPriority.Normal
                    }),
                ModerationReportActionKind.Assign => await ModerationService.AssignAsync(
                    eventId,
                    reportId,
                    new AssignModerationReportRequestDto
                    {
                        CaseId = caseId,
                        ExpectedCaseConcurrencyStamp = expectedStamp,
                        AssigneeUserId = actionResult.AssigneeUserId!.Value
                    }),
                ModerationReportActionKind.Decide => await ModerationService.DecideAsync(
                    eventId,
                    reportId,
                    new DecideModerationReportRequestDto
                    {
                        CaseId = caseId,
                        ExpectedCaseConcurrencyStamp = expectedStamp,
                        DecisionKind = actionResult.DecisionKind!.Value,
                        ReasonCode = actionResult.ReasonCode!,
                        SafeNote = actionResult.SafeNote,
                        DuplicateGroupId = actionResult.DuplicateGroupId
                    }),
                ModerationReportActionKind.Execute => await ModerationService.ExecuteDecisionAsync(
                    eventId,
                    reportId,
                    new ExecuteModerationReportDecisionRequestDto
                    {
                        CaseId = caseId,
                        DecisionId = actionResult.DecisionId!.Value,
                        ExpectedCaseConcurrencyStamp = expectedStamp,
                        CorrelationId = actionResult.CorrelationId
                    }),
                _ => ModerationReportActionResult.Failed("Unsupported moderation action.")
            };

            if (!result.Success)
            {
                _detailErrorMessage = result.Message;
                Snackbar.Add(result.Message, Severity.Error);
                await AnnouncerService.AnnounceAssertiveAsync(result.Message);
                return;
            }

            Snackbar.Add(GetActionSuccessMessage(actionResult.ActionKind), Severity.Success);
            await AnnouncerService.AnnouncePoliteAsync(GetActionSuccessMessage(actionResult.ActionKind));
            await LoadDetailAsync(eventId, reportId);
            await LoadPageAsync(_pageNumber);
        }
        finally
        {
            _isActionRunning = false;
        }
    }

    private async Task LoadDetailAsync(Guid eventId, Guid reportId)
    {
        _isDetailLoading = true;
        _detailErrorMessage = null;

        try
        {
            _selectedDetail = await ModerationService.GetDetailAsync(eventId, reportId);
            if (_selectedDetail is null)
            {
                _detailErrorMessage = "Moderation report detail was not found.";
                await AnnouncerService.AnnounceAssertiveAsync(_detailErrorMessage);
                return;
            }

            await AnnouncerService.AnnouncePoliteAsync("Moderation report detail loaded.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _detailErrorMessage = "Moderation report detail could not be loaded.";
            await AnnouncerService.AnnounceAssertiveAsync(_detailErrorMessage);
        }
        finally
        {
            _isDetailLoading = false;
        }
    }

    private string GetQueueSummary()
    {
        if (_totalCount <= 0)
        {
            return "No matching reports";
        }

        var start = ((_pageNumber - 1) * PageSize) + 1;
        var end = Math.Min(_totalCount, start + _reports.Count - 1);
        return $"{start}-{end} of {_totalCount} reports";
    }

    private static string? NullIfAll(string? value)
        => string.IsNullOrWhiteSpace(value) || value == AllFilterValue ? null : value.Trim();

    private static string FormatReportId(Guid? reportId)
        => reportId is { } value && value != Guid.Empty
            ? $"Report {value.ToString("N")[..8]}"
            : "Report";

    private static string FormatDate(DateTimeOffset? timestamp)
        => timestamp?.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture) ?? "-";

    private bool HasActionLink(ModerationReportActionKind actionKind)
        => _selectedDetail?.HasLink(GetActionRelation(actionKind)) == true;

    private static string? ValidateActionResult(ModerationReportActionDialogResult actionResult)
        => actionResult.ActionKind switch
        {
            ModerationReportActionKind.Triage when string.IsNullOrWhiteSpace(actionResult.QueueCode) => "Queue is required.",
            ModerationReportActionKind.Assign when actionResult.AssigneeUserId is null => "Assignee user id is required.",
            ModerationReportActionKind.Decide when actionResult.DecisionKind is null => "Decision is required.",
            ModerationReportActionKind.Decide when string.IsNullOrWhiteSpace(actionResult.ReasonCode) => "Reason code is required.",
            ModerationReportActionKind.Decide
                when actionResult.DecisionKind == EventReportDecisionKind.Duplicate && actionResult.DuplicateGroupId is null
                => "Duplicate group id is required.",
            ModerationReportActionKind.Execute when actionResult.DecisionId is null => "Decision id is required.",
            _ => null
        };

    private Guid? GetLatestDecisionId()
        => _selectedDetail?.Decisions?
            .Where(decision => decision.Id is not null)
            .OrderByDescending(decision => decision.CreatedAtUtc)
            .FirstOrDefault()
            ?.Id;

    private bool TryGetActionContext(
        out Guid eventId,
        out Guid reportId,
        out Guid caseId,
        out Guid expectedStamp)
    {
        eventId = _selectedDetail?.EventId ?? EventId;
        reportId = _selectedDetail?.Id ?? Guid.Empty;
        caseId = _selectedDetail?.CurrentCase?.Id ?? Guid.Empty;
        expectedStamp = _selectedDetail?.CurrentCase?.ConcurrencyStamp ?? Guid.Empty;

        return eventId != Guid.Empty
               && reportId != Guid.Empty
               && caseId != Guid.Empty
               && expectedStamp != Guid.Empty;
    }

    private static string GetActionRelation(ModerationReportActionKind actionKind)
        => actionKind switch
        {
            ModerationReportActionKind.Triage => "triage-report",
            ModerationReportActionKind.Assign => "assign-report",
            ModerationReportActionKind.Decide => "decide-report",
            ModerationReportActionKind.Execute => "execute-report-decision",
            _ => string.Empty
        };

    private static string GetActionTitle(ModerationReportActionKind actionKind)
        => actionKind switch
        {
            ModerationReportActionKind.Triage => "Triage Report",
            ModerationReportActionKind.Assign => "Assign Report",
            ModerationReportActionKind.Decide => "Decide Report",
            ModerationReportActionKind.Execute => "Execute Decision",
            _ => "Moderation Action"
        };

    private static string GetActionSuccessMessage(ModerationReportActionKind actionKind)
        => actionKind switch
        {
            ModerationReportActionKind.Triage => "Report triaged.",
            ModerationReportActionKind.Assign => "Report assigned.",
            ModerationReportActionKind.Decide => "Decision recorded.",
            ModerationReportActionKind.Execute => "Decision executed.",
            _ => "Moderation report updated."
        };

    private sealed record FilterOption(string Value, string Label);
}
