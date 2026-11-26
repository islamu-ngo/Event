using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements;

public enum AnnouncementPublicationOutcome { Applied, Unchanged, SourceChanged, Locked }

public sealed record AnnouncementPublicationResult(
    AnnouncementPublicationOutcome Outcome, AnnouncementSourceSnapshot Source);
