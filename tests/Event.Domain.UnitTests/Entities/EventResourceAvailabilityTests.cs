using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventResourceAvailabilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly EventResourceScheduleFacts Schedule = new(Start, Start.AddHours(4), Start, Start.AddHours(1));

    [Test]
    public async Task AbsoluteWindowIncludesStartAndExcludesEnd()
    {
        var availability = EventResourceAvailability.Create(Start, Start.AddHours(1));
        await Assert.That(availability.IsAvailableAt(Start.AddTicks(-1), Schedule)).IsFalse();
        await Assert.That(availability.IsAvailableAt(Start, Schedule)).IsTrue();
        await Assert.That(availability.IsAvailableAt(Start.AddHours(1).AddTicks(-1), Schedule)).IsTrue();
        await Assert.That(availability.IsAvailableAt(Start.AddHours(1), Schedule)).IsFalse();
    }

    [Test]
    public async Task RelativeWindowUsesCurrentSessionSchedule()
    {
        var availability = EventResourceAvailability.Create(
            startAnchor: EventResourceAvailabilityAnchorEnum.SessionEnd, startOffset: TimeSpan.FromMinutes(10));
        await Assert.That(availability.IsAvailableAt(Start.AddMinutes(70), Schedule)).IsTrue();
        await Assert.That(availability.IsAvailableAt(Start.AddMinutes(70),
            Schedule with { SessionEndUtc = Start.AddHours(2) })).IsFalse();
        await Assert.That(availability.IsAvailableAt(Start.AddHours(3),
            Schedule with { SessionEndUtc = null })).IsFalse();
    }

    [Test]
    public async Task InvalidAndAmbiguousWindowsRejectBeforePublication()
    {
        await Assert.That(() => EventResourceAvailability.Create(Start, Start)).Throws<ArgumentException>();
        await Assert.That(() => EventResourceAvailability.Create(Start.AddHours(1), Start)).Throws<ArgumentException>();
        await Assert.That(() => EventResourceAvailability.Create(Start,
            startAnchor: EventResourceAvailabilityAnchorEnum.EventStart, startOffset: TimeSpan.Zero)).Throws<ArgumentException>();
        await Assert.That(() => EventResourceAvailability.Create(startOffset: TimeSpan.FromMinutes(1))).Throws<ArgumentException>();
        await Assert.That(() => EventResourceAvailability.Create(startAnchor: (EventResourceAvailabilityAnchorEnum)99,
            startOffset: TimeSpan.Zero)).Throws<ArgumentException>();
    }

    [Test]
    public async Task MissingAndOverflowingAnchorsNeverUnlockMaterial()
    {
        var availability = EventResourceAvailability.Create(
            startAnchor: EventResourceAvailabilityAnchorEnum.EventEnd, startOffset: TimeSpan.MaxValue);
        await Assert.That(availability.IsAvailableAt(Start, Schedule)).IsFalse();
        await Assert.That(availability.IsAvailableAt(Start, Schedule with { EventEndUtc = null })).IsFalse();
    }

    [Test]
    public async Task OpenBoundsAndUtcNormalizationRetainTheirMeaning()
    {
        var open = EventResourceAvailability.Create();
        await Assert.That(open.IsAvailableAt(Start, Schedule)).IsTrue();
        var normalized = EventResourceAvailability.Create(Start.ToOffset(TimeSpan.FromHours(2)));
        await Assert.That(normalized.AbsoluteStartUtc!.Value.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(normalized.IsAvailableAt(Start, Schedule)).IsTrue();
        await Assert.That(EventResourceAvailability.Create(absoluteEndUtc: Start).IsAvailableAt(Start, Schedule)).IsFalse();
    }
}
