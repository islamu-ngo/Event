using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements;

public sealed record AnnouncementDismissalResult(
    AnnouncementDismissalDecision Decision, AnnouncementDismissalMarker? Marker);
