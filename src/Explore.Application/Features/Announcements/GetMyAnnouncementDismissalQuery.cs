using Explore.Application.Contracts.Operations;
using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements;

public sealed record GetMyAnnouncementDismissalQuery : IQuery<AnnouncementDismissalState>;

public sealed record AnnouncementDismissalState(
    Guid TenantId, AnnouncementObservation? Current, AnnouncementDismissalMarker? Marker);
