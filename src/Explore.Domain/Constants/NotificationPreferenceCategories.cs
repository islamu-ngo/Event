namespace Explore.Domain.Constants;

public static class NotificationPreferenceCategories
{
    public const string RegistrationConfirmations = "registration-confirmations";
    public const string OrganizerAnnouncements = "organizer-announcements";
    public const string EventReminders = "event-reminders";
    public const string EventUpdates = "event-updates";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RegistrationConfirmations,
        OrganizerAnnouncements,
        EventReminders,
        EventUpdates
    };

    public static bool IsKnown(string category)
    {
        return !string.IsNullOrWhiteSpace(category) && All.Contains(category.Trim());
    }

    public static string Normalize(string category)
    {
        return category.Trim().ToLowerInvariant();
    }
}
