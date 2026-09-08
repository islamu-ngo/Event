using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Components.Moderation;

public partial class ModerationReportDetailPanel : ComponentBase
{
    private static readonly IReadOnlyList<ActionAffordance> ActionAffordances =
    [
        new("triage-report", "Triage", Icons.Material.Filled.LowPriority, ModerationReportActionKind.Triage),
        new("assign-report", "Assign", Icons.Material.Filled.PersonAdd, ModerationReportActionKind.Assign),
        new("decide-report", "Decide", Icons.Material.Filled.Rule, ModerationReportActionKind.Decide),
        new("execute-report-decision", "Execute", Icons.Material.Filled.DoneAll, ModerationReportActionKind.Execute)
    ];

    [Parameter] public HalResourceOfModerationReportDetailDto? Report { get; set; }
    [Parameter] public bool IsLoading { get; set; }
    [Parameter] public bool IsActionRunning { get; set; }
    [Parameter] public string? ErrorMessage { get; set; }
    [Parameter] public EventCallback OnRefreshRequested { get; set; }
    [Parameter] public EventCallback<ModerationReportActionKind> OnActionRequested { get; set; }

    private IReadOnlyList<Targets> TargetItems => Report?.Targets?.ToArray() ?? [];
    private IReadOnlyList<EvidenceItems> EvidenceItems => Report?.EvidenceItems?.ToArray() ?? [];
    private IReadOnlyList<Signals> SignalItems => Report?.Signals?.ToArray() ?? [];
    private IReadOnlyList<Decisions> DecisionItems => Report?.Decisions?.ToArray() ?? [];
    private IReadOnlyList<ExternalLinks> ExternalLinkItems => Report?.ExternalLinks?.ToArray() ?? [];

    private int AvailableActionCount
        => Report is null ? 0 : ActionAffordances.Count(action => Report.HasLink(action.Relation));

    private IEnumerable<ActionAffordance> GetAvailableActionAffordances()
        => Report is null
            ? Enumerable.Empty<ActionAffordance>()
            : ActionAffordances.Where(action => Report.HasLink(action.Relation));

    private Task RequestActionAsync(ModerationReportActionKind actionKind)
        => OnActionRequested.HasDelegate ? OnActionRequested.InvokeAsync(actionKind) : Task.CompletedTask;

    private string GetSubtitle()
        => Report?.Id is { } reportId && reportId != Guid.Empty
            ? $"Report {reportId.ToString("N")[..8]}"
            : "No report selected";

    private static string FormatDate(DateTimeOffset? timestamp)
        => timestamp?.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture) ?? "-";

    private static string FormatText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string FormatGuid(Guid? value)
        => value is { } guid && guid != Guid.Empty ? guid.ToString("N")[..8] : "-";

    private static string FormatScore(double? score)
        => score.HasValue ? score.Value.ToString("0.###", CultureInfo.CurrentCulture) : "-";

    private sealed record ActionAffordance(
        string Relation,
        string Label,
        string Icon,
        ModerationReportActionKind ActionKind);
}
