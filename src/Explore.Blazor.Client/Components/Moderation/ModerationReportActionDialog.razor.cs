using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Components.Moderation;

public enum ModerationReportActionKind
{
    Triage,
    Assign,
    Decide,
    Execute
}

public sealed record ModerationReportActionDialogResult(
    ModerationReportActionKind ActionKind,
    string? QueueCode,
    EventReportPriority? Priority,
    Guid? AssigneeUserId,
    EventReportDecisionKind? DecisionKind,
    string? ReasonCode,
    string? SafeNote,
    Guid? DuplicateGroupId,
    Guid? DecisionId,
    string? CorrelationId);

public partial class ModerationReportActionDialog : ComponentBase
{
    private const int MaxQueueCodeLength = 50;
    private const int MaxReasonCodeLength = 100;
    private const int MaxSafeNoteLength = 1000;
    private const int MaxCorrelationIdLength = 128;

    private static readonly IReadOnlyList<PriorityOption> PriorityOptions =
    [
        new(EventReportPriority.Low, "Low"),
        new(EventReportPriority.Normal, "Normal"),
        new(EventReportPriority.High, "High"),
        new(EventReportPriority.Urgent, "Urgent")
    ];

    private static readonly IReadOnlyList<DecisionOption> DecisionOptions =
    [
        new(EventReportDecisionKind.NoViolation, "No violation"),
        new(EventReportDecisionKind.Duplicate, "Duplicate"),
        new(EventReportDecisionKind.NeedsMoreInfo, "Needs more info"),
        new(EventReportDecisionKind.Escalate, "Escalate"),
        new(EventReportDecisionKind.LightModerate, "Light moderate"),
        new(EventReportDecisionKind.HeavyRedact, "Heavy redact"),
        new(EventReportDecisionKind.WarnOrganizer, "Warn organizer")
    ];

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter] public ModerationReportActionKind ActionKind { get; set; }
    [Parameter] public Guid? ReportId { get; set; }
    [Parameter] public string? CurrentCaseStatusName { get; set; }
    [Parameter] public string? CurrentQueueCode { get; set; }
    [Parameter] public string? CurrentPriorityCode { get; set; }
    [Parameter] public Guid? CurrentAssigneeUserId { get; set; }
    [Parameter] public string? DefaultReasonCode { get; set; }
    [Parameter] public Guid? LatestDecisionId { get; set; }

    private string? _queueCode;
    private EventReportPriority _priority = EventReportPriority.Normal;
    private string? _assigneeUserId;
    private EventReportDecisionKind _decisionKind = EventReportDecisionKind.NoViolation;
    private string? _reasonCode;
    private string? _safeNote;
    private string? _duplicateGroupId;
    private string? _decisionId;
    private string? _correlationId;
    private string? _errorMessage;
    private bool _confirmedIrreversible;

    private string ReportLabel
        => ReportId is { } reportId && reportId != Guid.Empty
            ? $"Report {reportId.ToString("N")[..8]}"
            : "Report";

    private string CurrentCaseLabel
        => string.IsNullOrWhiteSpace(CurrentCaseStatusName)
            ? "No current case"
            : $"{CurrentCaseStatusName} · {NormalizeDisplay(CurrentQueueCode)}";

    private string ActionTitle
        => ActionKind switch
        {
            ModerationReportActionKind.Triage => "Triage report",
            ModerationReportActionKind.Assign => "Assign report",
            ModerationReportActionKind.Decide => "Decide report",
            ModerationReportActionKind.Execute => "Execute decision",
            _ => "Moderation action"
        };

    private string ConfirmText
        => ActionKind switch
        {
            ModerationReportActionKind.Triage => "Triage",
            ModerationReportActionKind.Assign => "Assign",
            ModerationReportActionKind.Decide => "Record decision",
            ModerationReportActionKind.Execute => "Execute",
            _ => "Confirm"
        };

    private string ActionIcon
        => ActionKind switch
        {
            ModerationReportActionKind.Triage => Icons.Material.Filled.LowPriority,
            ModerationReportActionKind.Assign => Icons.Material.Filled.PersonAdd,
            ModerationReportActionKind.Decide => Icons.Material.Filled.Rule,
            ModerationReportActionKind.Execute => Icons.Material.Filled.DoneAll,
            _ => Icons.Material.Filled.Check
        };

    private Color ActionColor
        => ActionKind switch
        {
            ModerationReportActionKind.Execute => Color.Warning,
            ModerationReportActionKind.Decide when _decisionKind == EventReportDecisionKind.HeavyRedact => Color.Error,
            _ => Color.Primary
        };

    protected override void OnInitialized()
    {
        _queueCode = string.IsNullOrWhiteSpace(CurrentQueueCode) ? "safety" : CurrentQueueCode.Trim();
        _priority = ParsePriority(CurrentPriorityCode);
        _assigneeUserId = CurrentAssigneeUserId?.ToString("D");
        _reasonCode = string.IsNullOrWhiteSpace(DefaultReasonCode) ? null : DefaultReasonCode.Trim();
        _decisionId = LatestDecisionId?.ToString("D");
    }

    public static Task<IDialogReference> ShowAsync(
        IDialogService dialogService,
        string title,
        DialogParameters? parameters = null,
        DialogOptions? options = null)
        => dialogService.ShowAsync<ModerationReportActionDialog>(
            title,
            parameters ?? new DialogParameters(),
            options);

    private void Cancel() => MudDialog.Cancel();

    private void Submit()
    {
        _errorMessage = Validate();
        if (!string.IsNullOrWhiteSpace(_errorMessage))
        {
            return;
        }

        MudDialog.Close(DialogResult.Ok(CreateResult()));
    }

    private ModerationReportActionDialogResult CreateResult()
        => new(
            ActionKind,
            NormalizeValue(_queueCode),
            ActionKind == ModerationReportActionKind.Triage ? _priority : null,
            ParseOptionalGuid(_assigneeUserId),
            ActionKind == ModerationReportActionKind.Decide ? _decisionKind : null,
            NormalizeValue(_reasonCode),
            NormalizeValue(_safeNote),
            ParseOptionalGuid(_duplicateGroupId),
            ParseOptionalGuid(_decisionId),
            NormalizeValue(_correlationId));

    private string? Validate()
        => ActionKind switch
        {
            ModerationReportActionKind.Triage => ValidateTriage(),
            ModerationReportActionKind.Assign => ValidateAssign(),
            ModerationReportActionKind.Decide => ValidateDecision(),
            ModerationReportActionKind.Execute => ValidateExecution(),
            _ => "Unsupported moderation action."
        };

    private string? ValidateTriage()
    {
        var queueCode = NormalizeValue(_queueCode);
        if (string.IsNullOrWhiteSpace(queueCode))
        {
            return "Queue is required.";
        }

        return queueCode.Length > MaxQueueCodeLength
            ? $"Queue must be {MaxQueueCodeLength} characters or fewer."
            : null;
    }

    private string? ValidateAssign()
        => ParseOptionalGuid(_assigneeUserId) is null
            ? "Assignee user id is required."
            : null;

    private string? ValidateDecision()
    {
        var reasonCode = NormalizeValue(_reasonCode);
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return "Reason code is required.";
        }

        if (reasonCode.Length > MaxReasonCodeLength)
        {
            return $"Reason code must be {MaxReasonCodeLength} characters or fewer.";
        }

        if (NormalizeValue(_safeNote)?.Length > MaxSafeNoteLength)
        {
            return $"Safe note must be {MaxSafeNoteLength} characters or fewer.";
        }

        if (_decisionKind == EventReportDecisionKind.Duplicate && ParseOptionalGuid(_duplicateGroupId) is null)
        {
            return "Duplicate group id is required.";
        }

        return _decisionKind == EventReportDecisionKind.HeavyRedact && !_confirmedIrreversible
            ? "Irreversible confirmation is required."
            : null;
    }

    private string? ValidateExecution()
    {
        if (ParseOptionalGuid(_decisionId) is null)
        {
            return "Decision id is required.";
        }

        return NormalizeValue(_correlationId)?.Length > MaxCorrelationIdLength
            ? $"Correlation id must be {MaxCorrelationIdLength} characters or fewer."
            : null;
    }

    private static EventReportPriority ParsePriority(string? code)
        => code switch
        {
            "low" => EventReportPriority.Low,
            "high" => EventReportPriority.High,
            "urgent" => EventReportPriority.Urgent,
            _ => EventReportPriority.Normal
        };

    private static Guid? ParseOptionalGuid(string? value)
        => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed : null;

    private static string? NormalizeValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeDisplay(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private sealed record PriorityOption(EventReportPriority Value, string Label);

    private sealed record DecisionOption(EventReportDecisionKind Value, string Label);
}
