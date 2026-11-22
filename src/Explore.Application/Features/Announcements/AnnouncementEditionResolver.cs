using Explore.Application.Contracts.Persistence;
using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements;

public sealed class AnnouncementEditionResolver(IAnnouncementSettingsReader reader)
{
    public async Task<AnnouncementResolution> ResolvePublicAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await reader.ReadPublicAsync(tenantId, cancellationToken);
        return AnnouncementPublicationPolicy.Resolve(tenantId,
            new(AnnouncementOwner.Instance, AnnouncementSettingValueCodec.DeserializeGroup(rows.Instance?.Value),
                rows.Instance?.IsLocked ?? false),
            new(AnnouncementOwner.ForTenant(tenantId), AnnouncementSettingValueCodec.DeserializeGroup(rows.Tenant?.Value),
                rows.Tenant?.IsLocked ?? false));
    }

    public async Task<AnnouncementDismissalState> ResolveSelfAsync(CancellationToken cancellationToken)
    {
        var rows = await reader.ReadSelfAsync(cancellationToken);
        var resolution = AnnouncementPublicationPolicy.Resolve(rows.TenantId,
            new(AnnouncementOwner.Instance, AnnouncementSettingValueCodec.DeserializeGroup(rows.Instance?.Value),
                rows.Instance?.IsLocked ?? false),
            new(AnnouncementOwner.ForTenant(rows.TenantId), AnnouncementSettingValueCodec.DeserializeGroup(rows.Tenant?.Value),
                rows.Tenant?.IsLocked ?? false));
        var marker = AnnouncementSettingValueCodec.DeserializeMarker(rows.Marker?.Value);
        if (marker is not null && marker.Observation.TenantId != rows.TenantId)
            throw new ArgumentException("The stored announcement marker belongs to another tenant.");
        return new(rows.TenantId, resolution.IsVisible ? resolution.Observation : null, marker);
    }
}
