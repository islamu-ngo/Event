using Explore.Application.Contracts.Operations;
using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements.Requests.Commands;

/// <summary>The actor comes from the authenticated adapter, never from a request body.</summary>
public sealed record DismissAnnouncementCommand(
    Guid ActorUserId, AnnouncementObservation Observed, AnnouncementDismissalMarker? ExpectedMarker)
    : ICommand<AnnouncementDismissalResult>;
