using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Explore.Blazor.Client.Layout;

public partial class NotificationItem
{
    [Parameter, EditorRequired]
    public NotificationListDto Notification { get; set; } = null!;

    [Parameter]
    public EventCallback<NotificationListDto> OnClick { get; set; }

    [Parameter]
    public EventCallback<NotificationListDto> OnDelete { get; set; }

    [Parameter]
    public EventCallback<NotificationListDto> OnArchive { get; set; }

    [Parameter]
    public EventCallback<NotificationListDto> OnSnooze { get; set; }

    private bool IsUnread => Notification.IsRead != true;

    private bool IsArchived => Notification.IsArchived == true;

    private bool IsSnoozed => Notification.SnoozedUntil is not null
                              && Notification.SnoozedUntil > DateTimeOffset.UtcNow;

    private string GetTypeIcon()
    {
        return Notification.NotificationTypeName?.ToLowerInvariant() switch
        {
            "info" => Icons.Material.Outlined.Info,
            "warning" => Icons.Material.Outlined.Warning,
            "success" => Icons.Material.Outlined.CheckCircle,
            "error" => Icons.Material.Outlined.Error,
            "eventupdate" => Icons.Material.Outlined.Event,
            "registration" => Icons.Material.Outlined.HowToReg,
            "announcement" => Icons.Material.Outlined.Campaign,
            "reminder" => Icons.Material.Outlined.Alarm,
            _ => Icons.Material.Outlined.Notifications
        };
    }

    private Color GetScopeColor()
    {
        return Notification.NotificationScopeName?.ToLowerInvariant() switch
        {
            "user" or "personal" => Color.Info,
            "organization" => Color.Warning,
            "group" => Color.Secondary,
            "system" => Color.Default,
            _ => Color.Default
        };
    }

    private string GetReasonLabel()
    {
        return IsSubscriptionNotification
            ? "Subscription"
            : Notification.NotificationReasonName ?? string.Empty;
    }

    private string? GetContextLabel()
    {
        if (string.IsNullOrWhiteSpace(Notification.RecipientContextActorName))
        {
            return null;
        }

        return string.Equals(Notification.RecipientContextActorName, Notification.SourceActorName, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"via {Notification.RecipientContextActorName}";
    }

    private string GetAccessibleLabel()
    {
        var parts = new List<string> { Notification.Title };

        if (!string.IsNullOrWhiteSpace(Notification.NotificationReasonName))
        {
            parts.Add($"Reason: {GetReasonLabel()}");
        }

        if (!string.IsNullOrWhiteSpace(Notification.SourceActorName))
        {
            parts.Add($"From: {Notification.SourceActorName}");
        }

        var context = GetContextLabel();
        if (!string.IsNullOrWhiteSpace(context))
        {
            parts.Add(context);
        }

        if (!string.IsNullOrWhiteSpace(Notification.NotificationScopeName))
        {
            parts.Add($"Scope: {Notification.NotificationScopeName}");
        }

        return string.Join(". ", parts);
    }

    private bool IsSubscriptionNotification => string.Equals(
        Notification.NotificationReasonName,
        "Subscription",
        StringComparison.OrdinalIgnoreCase);

    private static string TruncateBody(string body, int maxLength = 120)
    {
        if (body.Length <= maxLength) return body;
        return string.Concat(body.AsSpan(0, maxLength), "…");
    }

    private static string FormatRelativeTime(DateTimeOffset? dateTime)
    {
        if (dateTime is null) return "";

        var diff = DateTimeOffset.UtcNow - dateTime.Value;

        return diff.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)diff.TotalMinutes}m ago",
            < 1440 => $"{(int)diff.TotalHours}h ago",
            < 10080 => $"{(int)diff.TotalDays}d ago",
            _ => dateTime.Value.ToString("MMM d")
        };
    }

    private static string FormatSnoozeTime(DateTimeOffset? snoozedUntil)
    {
        if (snoozedUntil is null) return "";

        var target = snoozedUntil.Value;
        var now = DateTimeOffset.UtcNow;

        if (target.Date == now.Date)
            return target.ToString("h:mm tt");

        if (target.Date == now.Date.AddDays(1))
            return $"tomorrow {target.ToString("h:mm tt")}";

        return target.ToString("MMM d, h:mm tt");
    }

    private async Task HandleDelete()
    {
        await OnDelete.InvokeAsync(Notification);
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            await OnClick.InvokeAsync(Notification);
        }
    }

    private async Task HandleArchive()
    {
        await OnArchive.InvokeAsync(Notification);
    }

    private async Task HandleUnarchive()
    {
        await OnArchive.InvokeAsync(Notification);
    }

    private async Task HandleSnooze()
    {
        await OnSnooze.InvokeAsync(Notification);
    }
}
