namespace Event.Domain.UnitTests.Services.Scheduling;

using Explore.Domain.Services.Scheduling;

public class EventScheduleProjectionCalculatorTests
{
    private readonly EventScheduleProjectionCalculator _calculator = new();

    [Test]
    public async Task Project_WithEuropeBrussels_ConvertsToLocalTime()
    {
        // 2026-06-15 10:00 UTC = 2026-06-15 12:00 CEST (UTC+2)
        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "Europe/Brussels");

        await Assert.That(result.LocalStartDate).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(result.LocalEndDate).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(12, 0));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(14, 0));
    }

    [Test]
    public async Task Project_MinuteOfDay_CalculatedCorrectly()
    {
        // 2026-06-15 10:30 UTC = 2026-06-15 12:30 CEST
        var start = new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 14, 45, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "Europe/Brussels");

        // 12:30 = 12*60 + 30 = 750
        await Assert.That(result.LocalStartMinuteOfDay).IsEqualTo(750);
        // 16:45 = 16*60 + 45 = 1005
        await Assert.That(result.LocalEndMinuteOfDay).IsEqualTo(1005);
    }

    [Test]
    public async Task Project_CrossMidnight_SpansTwoDays()
    {
        // 2026-06-15 22:00 UTC = 2026-06-16 00:00 CEST (midnight)
        var start = new DateTimeOffset(2026, 6, 15, 22, 0, 0, TimeSpan.Zero);
        // 2026-06-16 01:00 UTC = 2026-06-16 03:00 CEST
        var end = new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "Europe/Brussels");

        await Assert.That(result.LocalStartDate).IsEqualTo(new DateOnly(2026, 6, 16));
        await Assert.That(result.LocalEndDate).IsEqualTo(new DateOnly(2026, 6, 16));
        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(0, 0));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(3, 0));
    }

    [Test]
    public async Task Project_CrossMidnightWithDateChange_HasDifferentDates()
    {
        // 2026-06-15 21:00 UTC = 2026-06-15 23:00 CEST
        var start = new DateTimeOffset(2026, 6, 15, 21, 0, 0, TimeSpan.Zero);
        // 2026-06-16 01:00 UTC = 2026-06-16 03:00 CEST
        var end = new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "Europe/Brussels");

        await Assert.That(result.LocalStartDate).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(result.LocalEndDate).IsEqualTo(new DateOnly(2026, 6, 16));
    }

    [Test]
    public async Task Project_DstSpringForward_SkipsClockHour()
    {
        // 2026 DST spring-forward in Brussels: March 29, 2026, clocks go from 02:00 → 03:00 CET → CEST
        // 2026-03-29 00:30 UTC = 2026-03-29 01:30 CET (before spring forward)
        var start = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);
        // 2026-03-29 02:00 UTC = 2026-03-29 04:00 CEST (after spring forward, UTC+2)
        var end = new DateTimeOffset(2026, 3, 29, 2, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "Europe/Brussels");

        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(1, 30));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(4, 0));
    }

    [Test]
    public async Task Project_DstFallBack_RepeatsClockHour()
    {
        // 2026 DST fall-back in Brussels: October 25, 2026, clocks go from 03:00 → 02:00 CEST → CET
        // 2026-10-25 00:00 UTC = 2026-10-25 02:00 CEST (before fall back)
        var start = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        // 2026-10-25 03:00 UTC = 2026-10-25 04:00 CET (after fall back, UTC+1)
        var end = new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "Europe/Brussels");

        await Assert.That(result.LocalStartDate).IsEqualTo(new DateOnly(2026, 10, 25));
        await Assert.That(result.LocalEndDate).IsEqualTo(new DateOnly(2026, 10, 25));
        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(2, 0));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(4, 0));
    }

    [Test]
    public async Task Project_NullTimezone_FallsBackToUtc()
    {
        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, null);

        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(10, 0));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(12, 0));
    }

    [Test]
    public async Task Project_EmptyTimezone_FallsBackToUtc()
    {
        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "");

        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(10, 0));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(12, 0));
    }

    [Test]
    public async Task Project_InvalidTimezone_ThrowsArgumentException()
    {
        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        await Assert.That(() => _calculator.Project(start, end, "Invalid/NotATimezone"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Project_EndBeforeStart_ThrowsArgumentException()
    {
        var start = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        await Assert.That(() => _calculator.Project(start, end, "Europe/Brussels"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Project_EqualStartAndEnd_ThrowsArgumentException()
    {
        var time = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

        await Assert.That(() => _calculator.Project(time, time, "Europe/Brussels"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Project_AmericaNewYork_ConvertsCorrectly()
    {
        // 2026-06-15 16:00 UTC = 2026-06-15 12:00 EDT (UTC-4)
        var start = new DateTimeOffset(2026, 6, 15, 16, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 18, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "America/New_York");

        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(12, 0));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(14, 0));
    }

    [Test]
    public async Task Project_AsiaTokyo_ConvertsCorrectly()
    {
        // 2026-06-15 03:00 UTC = 2026-06-15 12:00 JST (UTC+9)
        var start = new DateTimeOffset(2026, 6, 15, 3, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 5, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "Asia/Tokyo");

        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(12, 0));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(14, 0));
    }

    [Test]
    public async Task Project_UtcTimezone_ReturnsUtcTimes()
    {
        var start = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "UTC");

        await Assert.That(result.LocalStartTime).IsEqualTo(new TimeOnly(10, 0));
        await Assert.That(result.LocalEndTime).IsEqualTo(new TimeOnly(12, 0));
    }

    [Test]
    public async Task Project_MidnightStartMinuteOfDay_IsZero()
    {
        // 2026-06-15 22:00 UTC = 2026-06-16 00:00 CEST (midnight in Brussels)
        var start = new DateTimeOffset(2026, 6, 15, 22, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 15, 23, 0, 0, TimeSpan.Zero);

        var result = _calculator.Project(start, end, "Europe/Brussels");

        await Assert.That(result.LocalStartMinuteOfDay).IsEqualTo(0);
        await Assert.That(result.LocalEndMinuteOfDay).IsEqualTo(60);
    }
}
