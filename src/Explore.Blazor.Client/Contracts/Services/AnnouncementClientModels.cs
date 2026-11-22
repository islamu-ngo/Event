using Explore.Blazor.Client.Clients;
using Wire = ISLAMU.Wire.Contracts.Announcements;

namespace Explore.Blazor.Client.Contracts.Services;

public sealed record AnnouncementDisplayState(
    Guid TenantId, Wire.AnnouncementObservationDto? Observation,
    Wire.AnnouncementMaterialDto? Material, bool IsVisible, bool IsDismissed, bool CanDismiss);

public enum AnnouncementEditScope { Instance, Tenant, ManagedTenant }

public sealed record AnnouncementPublicationClientResult(
    bool Success, bool RequiresReconciliation, string? Message,
    HalResourceOfAnnouncementSourceResource? Source = null);

/// <summary>Editable material is separate from the retained, server-owned original source.</summary>
public sealed class AnnouncementDraft
{
    public HalResourceOfAnnouncementSourceResource? Original { get; internal set; }
    public bool Enabled { get; set; }
    public string Message { get; set; } = string.Empty;
    public string LinkText { get; set; } = string.Empty;
    public string LinkUrl { get; set; } = string.Empty;
}
