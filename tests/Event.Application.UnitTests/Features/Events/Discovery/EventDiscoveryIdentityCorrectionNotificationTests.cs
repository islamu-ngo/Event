using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Events.Discovery;

using Event = Explore.Domain.Event;

public sealed class EventDiscoveryIdentityCorrectionNotificationTests
{
    private static readonly DateTime Now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task DeliveryUsesCurrentActiveOwnersAndNeverExposesTheCounterpart()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid memberEventId = Guid.CreateVersion7();
        Guid primaryEventId = Guid.CreateVersion7();
        Guid memberOwnerId = Guid.CreateVersion7();
        Guid primaryOwnerId = Guid.CreateVersion7();
        Guid suspendedOwnerId = Guid.CreateVersion7();
        Event member = CreateEvent(tenantId, memberEventId);
        Event primary = CreateEvent(tenantId, primaryEventId);
        var events = Substitute.For<IEventRepository>();
        events.GetAuthorizationTargetsByIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([member, primary]);
        var roles = Substitute.For<IEventRoleAssignmentRepository>();
        roles.GetEffectiveOwnersForEventAsync(tenantId, memberEventId, Now, Arg.Any<CancellationToken>())
            .Returns([
                CreateOwner(tenantId, member, memberOwnerId),
                CreateOwner(tenantId, member, suspendedOwnerId)
            ]);
        roles.GetEffectiveOwnersForEventAsync(tenantId, primaryEventId, Now, Arg.Any<CancellationToken>())
            .Returns([CreateOwner(tenantId, primary, primaryOwnerId)]);
        var memberships = Substitute.For<ITenantUserRepository>();
        memberships.IsActiveTenantUserAsync(tenantId, memberOwnerId, Arg.Any<CancellationToken>())
            .Returns(true);
        memberships.IsActiveTenantUserAsync(tenantId, primaryOwnerId, Arg.Any<CancellationToken>())
            .Returns(true);
        memberships.IsActiveTenantUserAsync(tenantId, suspendedOwnerId, Arg.Any<CancellationToken>())
            .Returns(false);
        var captured = new List<RecipientNotificationMaterialization>();
        var materializer = Substitute.For<IRecipientNotificationMaterializer>();
        materializer.MaterializeAsync(
                Arg.Do<RecipientNotificationMaterialization>(captured.Add),
                Arg.Any<CancellationToken>())
            .Returns(call => RecipientNotificationMaterializationResult.Skipped());
        var service = CreateService(events, roles, memberships, materializer);
        var request = new EventDiscoveryIdentityCorrectionRequested(
            tenantId,
            memberEventId,
            primaryEventId,
            7,
            "same-offering",
            "same_program");

        await service.DeliverAsync(Guid.CreateVersion7(), request);

        await Assert.That(captured.Count).IsEqualTo(2);
        await Assert.That(captured.Select(item => item.Intent.UserId!.Value))
            .IsEquivalentTo([memberOwnerId, primaryOwnerId]);
        foreach (RecipientNotificationMaterialization notification in captured)
        {
            Guid ownEventId = notification.Intent.UserId == memberOwnerId
                ? memberEventId
                : primaryEventId;
            Guid counterpartId = ownEventId == memberEventId ? primaryEventId : memberEventId;
            await Assert.That(notification.Intent.EventId).IsEqualTo(ownEventId);
            await Assert.That(notification.InApp!.EntityId).IsEqualTo(ownEventId.ToString("D"));
            await Assert.That(notification.InApp.Body).DoesNotContain(counterpartId.ToString("D"));
            await Assert.That(notification.Intent.SafePayloadReference).DoesNotContain(counterpartId.ToString("D"));
        }
    }

    [Test]
    public async Task CrossTenantSourceFailsClosedBeforeAnyRecipientIsMaterialized()
    {
        Guid tenantId = Guid.CreateVersion7();
        var events = Substitute.For<IEventRepository>();
        events.GetAuthorizationTargetsByIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([CreateEvent(Guid.CreateVersion7(), Guid.CreateVersion7())]);
        var materializer = Substitute.For<IRecipientNotificationMaterializer>();
        var captured = new List<RecipientNotificationMaterialization>();
        materializer.MaterializeAsync(Arg.Do<RecipientNotificationMaterialization>(captured.Add),
            Arg.Any<CancellationToken>()).Returns(RecipientNotificationMaterializationResult.Skipped());
        var service = CreateService(
            events,
            Substitute.For<IEventRoleAssignmentRepository>(),
            Substitute.For<ITenantUserRepository>(),
            materializer);
        var request = new EventDiscoveryIdentityCorrectionRequested(
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            3,
            "reverse",
            "review_corrected");

        await Assert.That(() => service.DeliverAsync(Guid.CreateVersion7(), request))
            .Throws<InvalidOperationException>();
        await Assert.That(captured.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RetryProducesTheSameRecipientDeduplicationKey()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid primaryEventId = Guid.CreateVersion7();
        Guid ownerId = Guid.CreateVersion7();
        Event member = CreateEvent(tenantId, eventId);
        var events = Substitute.For<IEventRepository>();
        events.GetAuthorizationTargetsByIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([member]);
        var roles = Substitute.For<IEventRoleAssignmentRepository>();
        roles.GetEffectiveOwnersForEventAsync(tenantId, eventId, Now, Arg.Any<CancellationToken>())
            .Returns([CreateOwner(tenantId, member, ownerId)]);
        roles.GetEffectiveOwnersForEventAsync(tenantId, primaryEventId, Now, Arg.Any<CancellationToken>())
            .Returns([]);
        var memberships = Substitute.For<ITenantUserRepository>();
        memberships.IsActiveTenantUserAsync(tenantId, ownerId, Arg.Any<CancellationToken>())
            .Returns(true);
        var captured = new List<RecipientNotificationMaterialization>();
        var materializer = Substitute.For<IRecipientNotificationMaterializer>();
        materializer.MaterializeAsync(
                Arg.Do<RecipientNotificationMaterialization>(captured.Add),
                Arg.Any<CancellationToken>())
            .Returns(call => RecipientNotificationMaterializationResult.Skipped());
        var service = CreateService(events, roles, memberships, materializer);
        var request = new EventDiscoveryIdentityCorrectionRequested(
            tenantId,
            eventId,
            primaryEventId,
            0,
            "different-offering",
            "distinct_programs");

        Guid messageId = Guid.CreateVersion7();
        await service.DeliverAsync(messageId, request);
        await service.DeliverAsync(messageId, request);
        await service.DeliverAsync(Guid.CreateVersion7(), request);

        await Assert.That(captured.Count).IsEqualTo(3);
        await Assert.That(captured[0].Intent.DeduplicationKey)
            .IsEqualTo(captured[1].Intent.DeduplicationKey);
        await Assert.That(captured[0].Intent.DeduplicationKey)
            .IsNotEqualTo(captured[2].Intent.DeduplicationKey);
    }

    private static EventDiscoveryIdentityCorrectionNotificationService CreateService(
        IEventRepository events,
        IEventRoleAssignmentRepository roles,
        ITenantUserRepository memberships,
        IRecipientNotificationMaterializer materializer) =>
        new(
            events,
            roles,
            memberships,
            materializer,
            new EventDiscoveryIdentityCorrectionNotificationFactory(),
            new FixedTimeProvider(Now));

    private static Event CreateEvent(Guid tenantId, Guid eventId) => new()
    {
        Id = eventId,
        TenantId = tenantId,
        Tenant = null!,
        Title = "Managed event",
        ActorId = Guid.CreateVersion7(),
        Actor = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };

    private static EventRoleAssignment CreateOwner(Guid tenantId, Event sourceEvent, Guid userId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        EventId = sourceEvent.Id,
        Event = sourceEvent,
        UserId = userId,
        User = new User
        {
            Id = userId,
            Pii = new UserPii
            {
                Email = $"{userId:N}@example.test",
                FirstName = "Source",
                LastName = "Publisher"
            }
        },
        RoleId = (int)RoleEnum.EventOwner,
        Role = null!,
        Status = EventRoleAssignmentStatus.Active,
        StartsAtUtc = Now.AddDays(-1)
    };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
