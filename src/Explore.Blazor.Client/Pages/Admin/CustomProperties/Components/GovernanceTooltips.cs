using Explore.Blazor.Client.Clients;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Admin.CustomProperties.Components;

internal static class GovernanceTooltips
{
    public const string ExposureLevel =
        "Controls who may see this property. Internal = admins only; OrganizerOnly = event owners; " +
        "TenantAdminOnly = tenant staff; Public = surfaced to every authenticated user.";

    public const string Searchable =
        "Include this property in full-text search over the NormalizedValue column. " +
        "Turn on for user-facing discovery terms; off to avoid polluting search indexes.";

    public const string Filterable =
        "Expose this property as a faceted filter in discovery queries. Requires the tenant " +
        "feature flag custom_properties.projection_discovery_enabled to produce filter UX.";

    public const string Exportable =
        "Include this property in analytics/CSV exports and data egress APIs. Leave off for " +
        "operationally sensitive values that should not leave the admin surface.";

    public const string Moderation =
        "Flag changes to this property for moderation review. Enable for free-text or URL " +
        "properties where tenant policy requires human sign-off.";

    public const string Analytics =
        "Include this property in tenant-scoped analytics aggregations (instance counts, " +
        "promotion recommendations, usage heatmaps).";

    public static Color ExposureColor(ExposureLevel level) => level switch
    {
        Clients.ExposureLevel.Internal => Color.Default,
        Clients.ExposureLevel.OrganizerOnly => Color.Info,
        Clients.ExposureLevel.TenantAdminOnly => Color.Warning,
        Clients.ExposureLevel.Public => Color.Success,
        _ => Color.Default
    };
}
