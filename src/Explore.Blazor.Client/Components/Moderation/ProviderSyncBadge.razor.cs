using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Components.Moderation;

public partial class ProviderSyncBadge : ComponentBase
{
    [Parameter] public string? StateCode { get; set; }
    [Parameter] public string? StateName { get; set; }

    private string DisplayLabel => string.IsNullOrWhiteSpace(StateName) ? "-" : StateName;
    private string AriaLabel => $"Provider sync state: {DisplayLabel}";

    private Color BadgeColor
        => StateCode switch
        {
            "completed" or "synced" => Color.Success,
            "pending" or "queued" or "in_progress" => Color.Info,
            "retry_pending" or "failed_retryable" => Color.Warning,
            "failed" or "dead_lettered" => Color.Error,
            "disabled" or "skipped" => Color.Secondary,
            _ => Color.Default
        };

    private string BadgeIcon
        => StateCode switch
        {
            "completed" or "synced" => Icons.Material.Filled.Sync,
            "failed" or "dead_lettered" => Icons.Material.Filled.SyncProblem,
            "retry_pending" or "failed_retryable" => Icons.Material.Filled.Pending,
            "disabled" or "skipped" => Icons.Material.Filled.Block,
            _ => Icons.Material.Filled.Sync
        };
}
