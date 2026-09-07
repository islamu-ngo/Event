using Explore.Domain;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Domain.UnitTests.Aspects;

public class EventSessionIslamicAspectTests
{
    [Test]
    public async Task ApplyScheduling_FixedWithoutPrayerFields_ClearsPrayerState()
    {
        var aspect = new EventSessionIslamicAspect();

        aspect.ApplyScheduling(SessionStartTimeType.Fixed, null, null);

        await Assert.That(aspect.StartTimeType).IsEqualTo(SessionStartTimeType.Fixed);
        await Assert.That(aspect.ReferencePrayer).IsNull();
        await Assert.That(aspect.OffsetMinutes).IsNull();
    }

    [Test]
    public async Task ApplyScheduling_FixedWithPrayerFields_Throws()
    {
        var aspect = new EventSessionIslamicAspect();

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            aspect.ApplyScheduling(SessionStartTimeType.Fixed, PrayerTime.Dhuhr, 0);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ApplyScheduling_RelativeWithoutPrayerFields_Throws()
    {
        var aspect = new EventSessionIslamicAspect();

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            aspect.ApplyScheduling(SessionStartTimeType.RelativeToPrayer, PrayerTime.Asr, null);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ApplyScheduling_RelativeWithOffsetOutOfRange_Throws()
    {
        var aspect = new EventSessionIslamicAspect();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        {
            aspect.ApplyScheduling(SessionStartTimeType.RelativeToPrayer, PrayerTime.Maghrib, 181);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task IsValidSchedulingState_RequiresExactFieldShape()
    {
        await Assert.That(EventSessionIslamicAspect.IsValidSchedulingState(SessionStartTimeType.Fixed, null, null)).IsTrue();
        await Assert.That(EventSessionIslamicAspect.IsValidSchedulingState(SessionStartTimeType.Fixed, PrayerTime.Dhuhr, null)).IsFalse();
        await Assert.That(EventSessionIslamicAspect.IsValidSchedulingState(SessionStartTimeType.RelativeToPrayer, PrayerTime.Isha, -30)).IsTrue();
        await Assert.That(EventSessionIslamicAspect.IsValidSchedulingState(SessionStartTimeType.RelativeToPrayer, null, -30)).IsFalse();
    }
}
