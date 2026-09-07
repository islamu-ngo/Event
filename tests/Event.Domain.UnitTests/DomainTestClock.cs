namespace Event.Domain.UnitTests;

internal static class DomainTestClock
{
    internal static readonly DateTime UtcNow =
        new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    internal static readonly DateTimeOffset UtcNowOffset =
        new(UtcNow);
}
