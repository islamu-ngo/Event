using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Domain.UnitTests.Entities;

public sealed class EventLifecycleTests
{
    private static readonly DateTime FirstMutation = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondMutation = FirstMutation.AddMinutes(5);

    [Test]
    public async Task SemanticMethodsApplyExpectedLifecycleMutations()
    {
        global::Explore.Domain.Event publishEvent = CreateEvent(EventStatusEnum.Draft);
        global::Explore.Domain.Event cancelEvent = CreateEvent(EventStatusEnum.Published);
        global::Explore.Domain.Event archiveEvent = CreateEvent(EventStatusEnum.Cancelled);
        global::Explore.Domain.Event lightModerationEvent = CreateEvent(EventStatusEnum.Published);
        global::Explore.Domain.Event restoreEvent = CreateEvent(EventStatusEnum.Moderated);
        global::Explore.Domain.Event heavyModerationEvent = CreateEvent(EventStatusEnum.Cancelled);
        global::Explore.Domain.Event federatedEvent = CreateEvent(EventStatusEnum.Moderated);

        bool published = publishEvent.Publish(FirstMutation);
        bool publishRetry = publishEvent.Publish(SecondMutation);
        bool cancelled = cancelEvent.Cancel(FirstMutation);
        bool archived = archiveEvent.Archive(FirstMutation);
        bool lightModerated = lightModerationEvent.ApplyLightModeration(FirstMutation);
        bool restored = restoreEvent.RestoreAfterLightModeration(FirstMutation);
        bool heavyModerated = heavyModerationEvent.ApplyHeavyModeration(FirstMutation);
        bool federatedSynced = federatedEvent.SynchronizeFederatedLifecycle(EventStatusEnum.Published, FirstMutation);

        await Assert.That(published).IsTrue();
        await Assert.That(publishRetry).IsFalse();
        await Assert.That(cancelled).IsTrue();
        await Assert.That(archived).IsTrue();
        await Assert.That(lightModerated).IsTrue();
        await Assert.That(restored).IsTrue();
        await Assert.That(heavyModerated).IsTrue();
        await Assert.That(federatedSynced).IsTrue();
        await Assert.That(publishEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(publishEvent.UpdatedAt).IsEqualTo(FirstMutation);
        await Assert.That(cancelEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Cancelled);
        await Assert.That(archiveEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Archived);
        await Assert.That(lightModerationEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Moderated);
        await Assert.That(restoreEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(heavyModerationEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Moderated);
        await Assert.That(federatedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
    }

    [Test]
    public async Task InvalidUsageDoesNotMutateStatusTimestampOrConcurrencyStamp()
    {
        global::Explore.Domain.Event invalidTransitionEvent = CreateEvent(EventStatusEnum.Published);
        global::Explore.Domain.Event invalidTimeEvent = CreateEvent(EventStatusEnum.Draft);
        global::Explore.Domain.Event invalidFederatedEvent = CreateEvent(EventStatusEnum.Draft);
        Guid originalStamp = invalidTransitionEvent.ConcurrencyStamp;

        await Assert.That(() => invalidTransitionEvent.Archive(FirstMutation))
            .Throws<InvalidOperationException>();
        await Assert.That(() => invalidTimeEvent.Publish(DateTime.SpecifyKind(FirstMutation, DateTimeKind.Local)))
            .Throws<ArgumentException>();
        await Assert.That(() => invalidFederatedEvent.SynchronizeFederatedLifecycle((EventStatusEnum)999, FirstMutation))
            .Throws<ArgumentException>();

        await Assert.That(invalidTransitionEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(invalidTransitionEvent.UpdatedAt).IsNull();
        await Assert.That(invalidTransitionEvent.ConcurrencyStamp).IsEqualTo(originalStamp);
        await Assert.That(invalidTimeEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(invalidTimeEvent.UpdatedAt).IsNull();
        await Assert.That(invalidFederatedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(invalidFederatedEvent.UpdatedAt).IsNull();
    }

    [Test]
    public async Task EnsureDraftEditableRejectsNonDraftWithoutMutation()
    {
        global::Explore.Domain.Event draftEvent = CreateEvent(EventStatusEnum.Draft);
        global::Explore.Domain.Event publishedEvent = CreateEvent(EventStatusEnum.Published);

        draftEvent.EnsureDraftEditable();

        await Assert.That(() => publishedEvent.EnsureDraftEditable())
            .Throws<InvalidOperationException>();
        await Assert.That(publishedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(publishedEvent.UpdatedAt).IsNull();
    }

    private static global::Explore.Domain.Event CreateEvent(EventStatusEnum status) => new(status)
    {
        Title = "Lifecycle test event",
        EventStatus = new EventStatus { Id = (int)status, MasterCode = status.ToString().ToUpperInvariant(), FullName = status.ToString() },
        Actor = new Actor { Pii = new ActorPii { DisplayName = "Actor" }, ActorType = new ActorType { MasterCode = "USER", FullName = "User" } },
        Tenant = new Tenant
        {
            FullName = "Tenant",
            Slug = "tenant",
            TenantStatusId = 2,
            TenantStatus = new TenantStatus { Id = 2, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true }
        },
        VisibilityType = new VisibilityType { MasterCode = "PUBLIC", FullName = "Public" },
        EventFormat = new EventFormat { MasterCode = "ONLINE", FullName = "Online" },
        ConcurrencyStamp = Guid.CreateVersion7()
    };
}
