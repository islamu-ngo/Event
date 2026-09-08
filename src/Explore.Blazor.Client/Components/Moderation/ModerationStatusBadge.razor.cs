using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Components.Moderation;

public enum ModerationStatusBadgeKind
{
    ReportStatus,
    CaseStatus,
    Priority
}

public partial class ModerationStatusBadge : ComponentBase
{
    [Parameter] public ModerationStatusBadgeKind Kind { get; set; } = ModerationStatusBadgeKind.ReportStatus;
    [Parameter] public string? Code { get; set; }
    [Parameter] public string? Label { get; set; }

    private string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? "-" : Label;
    private string AriaLabel => $"{KindLabel}: {DisplayLabel}";

    private string KindLabel
        => Kind switch
        {
            ModerationStatusBadgeKind.CaseStatus => "Case status",
            ModerationStatusBadgeKind.Priority => "Priority",
            _ => "Report status"
        };

    private Variant BadgeVariant
        => Kind == ModerationStatusBadgeKind.Priority ? Variant.Filled : Variant.Outlined;

    private Color BadgeColor
        => Kind switch
        {
            ModerationStatusBadgeKind.Priority => GetPriorityColor(Code),
            ModerationStatusBadgeKind.CaseStatus => GetCaseStatusColor(Code),
            _ => GetReportStatusColor(Code)
        };

    private string BadgeIcon
        => Kind switch
        {
            ModerationStatusBadgeKind.Priority => GetPriorityIcon(Code),
            ModerationStatusBadgeKind.CaseStatus => GetCaseStatusIcon(Code),
            _ => GetReportStatusIcon(Code)
        };

    private static Color GetReportStatusColor(string? statusCode)
        => statusCode switch
        {
            "submitted" or "triaged" => Color.Info,
            "under_review" or "escalated" => Color.Warning,
            "actioned" => Color.Success,
            "dismissed" or "duplicate" or "closed" => Color.Secondary,
            _ => Color.Default
        };

    private static Color GetCaseStatusColor(string? statusCode)
        => statusCode switch
        {
            "open" => Color.Info,
            "assigned" or "decision_ready" => Color.Warning,
            "closed" => Color.Success,
            "waiting_external" or "waiting_reporter" => Color.Secondary,
            _ => Color.Default
        };

    private static Color GetPriorityColor(string? priorityCode)
        => priorityCode switch
        {
            "urgent" => Color.Error,
            "high" => Color.Warning,
            "normal" => Color.Info,
            "low" => Color.Default,
            _ => Color.Default
        };

    private static string GetReportStatusIcon(string? statusCode)
        => statusCode switch
        {
            "actioned" => Icons.Material.Filled.Done,
            "dismissed" or "closed" => Icons.Material.Filled.TaskAlt,
            "duplicate" => Icons.Material.Filled.ContentCopy,
            "escalated" => Icons.Material.Filled.PriorityHigh,
            _ => Icons.Material.Filled.Flag
        };

    private static string GetCaseStatusIcon(string? statusCode)
        => statusCode switch
        {
            "assigned" => Icons.Material.Filled.Person,
            "decision_ready" => Icons.Material.Filled.Rule,
            "closed" => Icons.Material.Filled.TaskAlt,
            "waiting_external" or "waiting_reporter" => Icons.Material.Filled.HourglassTop,
            _ => Icons.Material.Filled.Inbox
        };

    private static string GetPriorityIcon(string? priorityCode)
        => priorityCode switch
        {
            "urgent" => Icons.Material.Filled.Report,
            "high" => Icons.Material.Filled.PriorityHigh,
            _ => Icons.Material.Filled.LowPriority
        };
}
