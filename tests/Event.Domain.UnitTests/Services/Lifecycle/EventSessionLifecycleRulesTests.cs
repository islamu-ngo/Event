using Explore.Domain.Enums;
using Explore.Domain.Services.Lifecycle;

namespace Explore.Domain.UnitTests.Services.Lifecycle;

public sealed class EventSessionLifecycleRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(2);

    [Test]
    public async Task CanScheduleAllowsOnlyMutableReviewStates()
    {
        foreach (EventSessionStatusEnum status in SessionStatuses())
        {
            await Assert.That(EventSessionLifecycleRules.CanSchedule(status))
                .IsEqualTo(Schedulable.Contains(status));
        }

        await Assert.That(EventSessionLifecycleRules.CanSchedule((EventSessionStatusEnum)999)).IsFalse();
    }

    [Test]
    public async Task CanPublishRequiresEligibleCurrentPublishedParentAndPublishableSchedule()
    {
        foreach (EventSessionStatusEnum current in SessionStatuses())
        {
            foreach (EventStatusEnum parent in ParentStatuses())
            {
                bool expected = PublishableCurrent.Contains(current) && parent == EventStatusEnum.Published;

                await Assert.That(EventSessionLifecycleRules.CanPublish(current, parent, Start, End, SessionEndTimeType.Fixed))
                    .IsEqualTo(expected);
            }
        }

        await Assert.That(EventSessionLifecycleRules.CanPublish(EventSessionStatusEnum.Draft, EventStatusEnum.Published, null, End, SessionEndTimeType.Fixed)).IsFalse();
        await Assert.That(EventSessionLifecycleRules.CanPublish(EventSessionStatusEnum.Draft, EventStatusEnum.Published, Start, Start, SessionEndTimeType.Fixed)).IsFalse();
        await Assert.That(EventSessionLifecycleRules.CanPublish(EventSessionStatusEnum.Draft, EventStatusEnum.Published, Start, null, SessionEndTimeType.Fixed)).IsFalse();
        await Assert.That(EventSessionLifecycleRules.CanPublish(EventSessionStatusEnum.Draft, EventStatusEnum.Published, Start, null, SessionEndTimeType.OpenEnded)).IsTrue();
        await Assert.That(EventSessionLifecycleRules.CanPublish(EventSessionStatusEnum.Draft, EventStatusEnum.Published, Start, End, SessionEndTimeType.Fixed)).IsTrue();
        await Assert.That(EventSessionLifecycleRules.CanPublish(EventSessionStatusEnum.Draft, EventStatusEnum.Published, Start, null, SessionEndTimeType.RelativeToPrayer)).IsTrue();
        await Assert.That(EventSessionLifecycleRules.CanPublish(EventSessionStatusEnum.Published, EventStatusEnum.Published, Start, End, SessionEndTimeType.Fixed)).IsFalse();
    }

    [Test]
    public async Task IsPublishParentCompatibleRequiresDefinedPublishedParent()
    {
        foreach (EventStatusEnum parent in ParentStatuses())
        {
            await Assert.That(EventSessionLifecycleRules.IsPublishParentCompatible(parent))
                .IsEqualTo(parent == EventStatusEnum.Published);
        }

        await Assert.That(EventSessionLifecycleRules.IsPublishParentCompatible((EventStatusEnum)999)).IsFalse();
    }

    [Test]
    public async Task HasPublishableScheduleRequiresValidShapeForEndTimeType()
    {
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(null, End, SessionEndTimeType.Fixed)).IsFalse();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, null, SessionEndTimeType.Fixed)).IsFalse();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, Start, SessionEndTimeType.Fixed)).IsFalse();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, End, SessionEndTimeType.Fixed)).IsTrue();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, null, SessionEndTimeType.OpenEnded)).IsTrue();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, End, SessionEndTimeType.OpenEnded)).IsFalse();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, null, SessionEndTimeType.RelativeToPrayer)).IsTrue();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, Start, SessionEndTimeType.RelativeToPrayer)).IsFalse();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, End, SessionEndTimeType.RelativeToPrayer)).IsTrue();
        await Assert.That(EventSessionLifecycleRules.HasPublishableSchedule(Start, End, (SessionEndTimeType)999)).IsFalse();
    }

    [Test]
    public async Task CanCancelRequiresCancellableCurrentAndMutableParent()
    {
        foreach (EventSessionStatusEnum current in SessionStatuses())
        {
            foreach (EventStatusEnum parent in ParentStatuses())
            {
                bool expected = Cancellable.Contains(current) && MutableParents.Contains(parent);

                await Assert.That(EventSessionLifecycleRules.CanCancel(current, parent))
                    .IsEqualTo(expected);
            }
        }
    }

    [Test]
    public async Task CanCompleteRequiresPublishedSessionAndPublishedParent()
    {
        foreach (EventSessionStatusEnum current in SessionStatuses())
        {
            foreach (EventStatusEnum parent in ParentStatuses())
            {
                bool expected = current == EventSessionStatusEnum.Published && parent == EventStatusEnum.Published;

                await Assert.That(EventSessionLifecycleRules.CanComplete(current, parent))
                    .IsEqualTo(expected);
            }
        }
    }

    [Test]
    public async Task CanArchiveRequiresArchivableCurrentAndMutableParent()
    {
        foreach (EventSessionStatusEnum current in SessionStatuses())
        {
            foreach (EventStatusEnum parent in ParentStatuses())
            {
                bool expected = Archivable.Contains(current) && MutableParents.Contains(parent);

                await Assert.That(EventSessionLifecycleRules.CanArchive(current, parent))
                    .IsEqualTo(expected);
            }
        }
    }

    [Test]
    public async Task EnsureMethodsRejectInvalidOrDisallowedInputs()
    {
        await Assert.That(() => EventSessionLifecycleRules.EnsureCanSchedule(EventSessionStatusEnum.Archived)).Throws<InvalidOperationException>();
        await Assert.That(() => EventSessionLifecycleRules.EnsureCanPublish(EventSessionStatusEnum.Draft, EventStatusEnum.Draft, Start, End, SessionEndTimeType.Fixed)).Throws<InvalidOperationException>();
        await Assert.That(() => EventSessionLifecycleRules.EnsureCanCancel(EventSessionStatusEnum.Archived, EventStatusEnum.Published)).Throws<InvalidOperationException>();
        await Assert.That(() => EventSessionLifecycleRules.EnsureCanComplete(EventSessionStatusEnum.Draft, EventStatusEnum.Published)).Throws<InvalidOperationException>();
        await Assert.That(() => EventSessionLifecycleRules.EnsureCanArchive(EventSessionStatusEnum.Published, EventStatusEnum.Published)).Throws<InvalidOperationException>();
        await Assert.That(() => EventSessionLifecycleRules.EnsureDefinedStatus((EventSessionStatusEnum)999, "status")).Throws<ArgumentException>();
    }

    private static EventSessionStatusEnum[] SessionStatuses() => Enum.GetValues<EventSessionStatusEnum>();

    private static EventStatusEnum[] ParentStatuses() => Enum.GetValues<EventStatusEnum>();

    private static HashSet<EventSessionStatusEnum> Schedulable { get; } =
    [
        EventSessionStatusEnum.Draft,
        EventSessionStatusEnum.Submitted,
        EventSessionStatusEnum.UnderReview,
        EventSessionStatusEnum.Approved,
        EventSessionStatusEnum.Published
    ];

    private static HashSet<EventSessionStatusEnum> PublishableCurrent { get; } =
    [
        EventSessionStatusEnum.Draft,
        EventSessionStatusEnum.Submitted,
        EventSessionStatusEnum.UnderReview,
        EventSessionStatusEnum.Approved
    ];

    private static HashSet<EventSessionStatusEnum> Cancellable { get; } =
    [
        EventSessionStatusEnum.Draft,
        EventSessionStatusEnum.Submitted,
        EventSessionStatusEnum.UnderReview,
        EventSessionStatusEnum.Approved,
        EventSessionStatusEnum.Published
    ];

    private static HashSet<EventSessionStatusEnum> Archivable { get; } =
    [
        EventSessionStatusEnum.Draft,
        EventSessionStatusEnum.Cancelled,
        EventSessionStatusEnum.Completed
    ];

    private static HashSet<EventStatusEnum> MutableParents { get; } =
    [
        EventStatusEnum.Draft,
        EventStatusEnum.Published,
        EventStatusEnum.Cancelled,
        EventStatusEnum.Completed
    ];
}
