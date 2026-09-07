namespace Explore.Domain.Constants;

public static class ConsentPurposeCodes
{
    public const string OrganizerFutureCommunications = "ORGANIZER_FUTURE_COMMUNICATIONS";
    public const string EventUpdates = "EVENT_UPDATES";

    public static bool IsMarketing(string purposeCode) => purposeCode is OrganizerFutureCommunications or EventUpdates;
}
