namespace Explore.Application.Services;

public static class EventLifecycleAutomationUseCases
{
    public const string RegistrationConfirmation = "registration_confirmation";
    public const string RegistrationApproved = "registration_approved";
    public const string RegistrationRejected = "registration_rejected";
    public const string WaitlistPromoted = "waitlist_promoted";
    public const string RegistrationCancelled = "registration_cancelled";
    public const string RegistrationRevoked = "registration_revoked";
    public const string EventReminder = "event_reminder";
    public const string EventCancelled = "event_cancelled";
    public const string OrganizerNotification = "organizer_notification";

    public static readonly IReadOnlySet<string> ImmediateOutboxUseCases = new HashSet<string>(StringComparer.Ordinal)
    {
        RegistrationConfirmation,
        RegistrationApproved,
        RegistrationRejected,
        WaitlistPromoted,
        RegistrationCancelled,
        RegistrationRevoked,
        EventCancelled,
        OrganizerNotification
    };

    public static readonly IReadOnlySet<string> DelayedRuntimeCandidates = new HashSet<string>(StringComparer.Ordinal)
    {
        EventReminder
    };
}
