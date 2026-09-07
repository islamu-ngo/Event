namespace Explore.Domain.Enums;

public enum AdmissionCheckInResultCodeEnum
{
    CheckedIn = 1,
    ReEntered = 2,
    AlreadyCheckedIn = 3,
    Undone = 4,
    NotCheckedIn = 5,
    NotEntitled = 6,
    TooEarly = 7,
    TooLate = 8,
    ReEntryNotAllowed = 9,
    CheckInNotFound = 10,
    AdmissionStopped = 11
}
