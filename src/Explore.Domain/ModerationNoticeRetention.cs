namespace Explore.Domain;

/// <summary>Fits the original calendar remedy floor inside independently approved finite notice retention.</summary>
public static class ModerationNoticeRetention
{
    public static bool CanNotifyAt(DateTimeOffset notifiedAt, int calendarMonths, DateTimeOffset retainUntil)
    {
        if (calendarMonths is < 0 or > 120 || notifiedAt.Offset != TimeSpan.Zero
            || retainUntil.Offset != TimeSpan.Zero || notifiedAt == DateTimeOffset.MinValue
            || retainUntil == DateTimeOffset.MaxValue || notifiedAt >= retainUntil)
            return false;
        try
        {
            return notifiedAt.AddMonths(calendarMonths) <= retainUntil;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
