using Explore.Domain.Enums;
using Explore.Domain.Services.Lifecycle;

namespace Explore.Domain.UnitTests.Services.Lifecycle;

public sealed class EventLifecycleRulesTests
{
    [Test]
    public async Task CanTransitionCharacterizesEveryEventStatePair()
    {
        foreach (EventStatusEnum current in Enum.GetValues<EventStatusEnum>())
        {
            foreach (EventStatusEnum desired in Enum.GetValues<EventStatusEnum>())
            {
                bool expected = current == desired || AllowedTransitions[current].Contains(desired);

                await Assert.That(EventLifecycleRules.CanTransition(current, desired))
                    .IsEqualTo(expected);
            }
        }

        await Assert.That(EventLifecycleRules.CanTransition(
                (EventStatusEnum)999,
                EventStatusEnum.Published))
            .IsFalse();
        await Assert.That(EventLifecycleRules.CanTransition(
                EventStatusEnum.Published,
                (EventStatusEnum)999))
            .IsFalse();
        await Assert.That(() => EventLifecycleRules.EnsureCanTransition(
                EventStatusEnum.Moderated,
                EventStatusEnum.Published))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task IsDraftEditableAllowsOnlyDraft()
    {
        foreach (EventStatusEnum status in Enum.GetValues<EventStatusEnum>())
        {
            await Assert.That(EventLifecycleRules.IsDraftEditable(status))
                .IsEqualTo(status == EventStatusEnum.Draft);
        }

        EventLifecycleRules.EnsureDraftEditable(EventStatusEnum.Draft);

        await Assert.That(EventLifecycleRules.IsDraftEditable((EventStatusEnum)999)).IsFalse();
        await Assert.That(() => EventLifecycleRules.EnsureDraftEditable(EventStatusEnum.Published))
            .Throws<InvalidOperationException>();
    }

    private static Dictionary<EventStatusEnum, EventStatusEnum[]> AllowedTransitions { get; } =
        new()
        {
            [EventStatusEnum.Draft] =
            [
                EventStatusEnum.Published,
                EventStatusEnum.Cancelled,
                EventStatusEnum.Archived
            ],
            [EventStatusEnum.Published] =
            [
                EventStatusEnum.Cancelled,
                EventStatusEnum.Moderated
            ],
            [EventStatusEnum.Cancelled] = [EventStatusEnum.Archived],
            [EventStatusEnum.Completed] = [EventStatusEnum.Archived],
            [EventStatusEnum.Archived] = [],
            [EventStatusEnum.Moderated] = []
        };
}
