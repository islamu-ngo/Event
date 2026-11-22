using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements;

public sealed record AnnouncementUpdate(AnnouncementChange Change, AnnouncementSourceSnapshot Expected);
