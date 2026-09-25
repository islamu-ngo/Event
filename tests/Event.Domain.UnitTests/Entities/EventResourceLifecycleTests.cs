using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventResourceLifecycleTests
{
    [Test]
    public async Task DraftRequiresPayloadAndCannotChangeOwnerThroughTenantInterface()
    {
        var (resource, parent, subject) = EventResourceTestData.CreateDraft();
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
        await Assert.That(resource.StorageObjectId).IsNull();
        await Assert.That(() => resource.Publish(parent, true, resource.ConcurrencyStamp, subject, EventResourceTestData.Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ((ITenantEntity)resource).TenantId = Guid.CreateVersion7()).Throws<InvalidOperationException>();
        await Assert.That(resource.TenantId).IsEqualTo(parent.TenantId);
    }

    [Test]
    public async Task PublicationWithdrawalAndRepublishingRespectTerminalStates()
    {
        var (resource, parent, subject) = EventResourceTestData.CreatePublished();
        resource.Withdraw(resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject).CanAccess).IsFalse();
        resource.Republish(parent, true, resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject).CanAccess).IsTrue();
        await Assert.That(() => resource.Archive(resource.ConcurrencyStamp, subject, EventResourceTestData.Now))
            .Throws<InvalidOperationException>();
        resource.Withdraw(resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        resource.Archive(resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(() => resource.Republish(parent, true, resource.ConcurrencyStamp, subject, EventResourceTestData.Now))
            .Throws<InvalidOperationException>();
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject).DiscloseMetadata).IsFalse();
    }

    [Test]
    public async Task ParentCancellationAndWrongLineageDenyWithoutChangingResource()
    {
        var (resource, parent, subject) = EventResourceTestData.CreatePublished();
        foreach (var denied in new[]
        {
            parent with { EventStatus = EventStatusEnum.Cancelled },
            parent with { EventStatus = EventStatusEnum.Draft },
            parent with { EventStatus = EventStatusEnum.Completed },
            parent with { EventStatus = EventStatusEnum.Moderated },
            parent with { EventDeleted = true },
            parent with { EventEligible = false },
            parent with { TenantId = Guid.CreateVersion7() },
            parent with { EventId = Guid.CreateVersion7() },
            parent with { EventSessionId = Guid.CreateVersion7() },
            parent with { SessionDeleted = true },
            parent with { SessionStatus = EventSessionStatusEnum.Rejected },
            parent with { SessionStatus = EventSessionStatusEnum.Cancelled },
            parent with { SessionStatus = EventSessionStatusEnum.Archived },
            parent with { SessionStatus = EventSessionStatusEnum.Moderated },
            parent with { SessionStatus = null }
        })
        {
            await Assert.That(EventResourceTestData.Decision(resource, denied, subject).DiscloseMetadata).IsFalse();
        }

        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Published);
        await Assert.That(EventResourceTestData.Decision(resource,
            parent with { SessionStatus = EventSessionStatusEnum.Completed }, subject).CanAccess).IsTrue();
    }

    [Test]
    public async Task ReplacementClearsPriorPayloadAndDeletionDoesNotResurrect()
    {
        var (resource, parent, subject) = EventResourceTestData.CreatePublished();
        var previousFile = resource.StorageObjectId;
        var detached = resource.SetExternalDestination("protected-test-envelope", 1, "https://material.example",
            resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(detached).IsEqualTo(previousFile);
        await Assert.That(resource.StorageObjectId).IsNull();
        await Assert.That(resource.EventResourceDeliveryTypeId).IsEqualTo((int)EventResourceDeliveryTypeEnum.ExternalLink);
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(resource.ExternalDestinationCiphertext).IsNull();
        await Assert.That(resource.ExternalDestinationSafeOrigin).IsNull();
        await Assert.That(resource.ExternalDestinationProtectionVersion).IsNull();

        var deletedFile = resource.Delete(resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(deletedFile).IsNotNull();
        await Assert.That(resource.IsDeleted).IsTrue();
        await Assert.That(resource.StorageObjectId).IsNull();
        await Assert.That(() => resource.SetStoredFile(Guid.CreateVersion7(),
            resource.ConcurrencyStamp, subject, EventResourceTestData.Now)).Throws<InvalidOperationException>();
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject).CanAccess).IsFalse();
    }

    [Test]
    public async Task StaleEditAndInvalidMetadataLeaveStateUnchanged()
    {
        var (resource, _, subject) = EventResourceTestData.CreateDraft();
        resource.ConcurrencyStamp = Guid.CreateVersion7();
        var metadata = EventResourceTestData.Metadata with { Title = "Changed" };
        await Assert.That(() => resource.UpdateMetadata(metadata, Guid.CreateVersion7(), subject, EventResourceTestData.Now))
            .Throws<InvalidOperationException>();
        await Assert.That(resource.Title).IsEqualTo("Private material");
        var stamp = resource.ConcurrencyStamp;
        await Assert.That(() => resource.UpdateMetadata(metadata with
            { DisclosureMode = EventResourceDisclosureModeEnum.Teaser, PublicTitle = null },
            stamp, subject, EventResourceTestData.Now)).Throws<ArgumentException>();
        await Assert.That(resource.Title).IsEqualTo("Private material");
        resource.UpdateMetadata(metadata, stamp, subject, EventResourceTestData.Now);
        await Assert.That(resource.Title).IsEqualTo("Changed");
        await Assert.That(resource.ConcurrencyStamp).IsEqualTo(stamp);
    }

    [Test]
    public async Task PublicationRejectsUnsafePayloadAndForeignParents()
    {
        var (resource, parent, subject) = EventResourceTestData.CreateDraft();
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(() => resource.Publish(parent, false, resource.ConcurrencyStamp, subject, EventResourceTestData.Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => resource.Publish(parent with { EventId = Guid.CreateVersion7() },
            true, resource.ConcurrencyStamp, subject, EventResourceTestData.Now)).Throws<InvalidOperationException>();
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
    }

    [Test]
    public async Task SemanticCodesAreStableAndOnlyImplementedDeliveryTypesExist()
    {
        var kinds = EventResourceKind.CreateDefaults();
        await Assert.That(string.Join(",", kinds.Select(kind => $"{kind.Id}:{kind.MasterCode}"))).IsEqualTo(
            "1:GENERAL_DOCUMENT,2:PRESENTATION,3:SPEAKER_MATERIAL,4:ATTENDEE_HANDBOOK,5:SCHEDULE,6:WORKSHEET,7:RECORDING,8:LIVESTREAM,9:VIRTUAL_MEETING,10:CERTIFICATE,11:SPONSOR_MATERIAL,12:ORGANIZER_INTERNAL,13:OTHER");
        var deliveryTypes = EventResourceDeliveryType.CreateDefaults();
        await Assert.That(string.Join(",", deliveryTypes.Select(type => $"{type.Id}:{type.MasterCode}")))
            .IsEqualTo("1:STORED_FILE,2:EXTERNAL_LINK");
    }

    [Test]
    public async Task AuditRequiresClosedValuesAndSupportsScopedAttributionErasure()
    {
        var tenant = Guid.CreateVersion7();
        var resource = Guid.CreateVersion7();
        var manager = Guid.CreateVersion7();
        var audit = EventResourceAuditEntry.Create(tenant, resource, manager,
            EventResourceAuditAction.Publish, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, EventResourceTestData.Now);
        audit.EraseAttribution(Guid.CreateVersion7());
        await Assert.That(audit.ResponsibleManagerUserId).IsEqualTo(manager);
        audit.EraseAttribution(manager);
        await Assert.That(audit.ResponsibleManagerUserId).IsNull();
        await Assert.That(audit.Action).IsEqualTo(EventResourceAuditAction.Publish);
        await Assert.That(audit.EventResourceId).IsEqualTo(resource);
        await Assert.That(() => EventResourceAuditEntry.Create(tenant, resource, manager,
            (EventResourceAuditAction)99, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, EventResourceTestData.Now)).Throws<ArgumentException>();
        await Assert.That(() => EventResourceAuditEntry.Create(tenant, resource, manager,
            EventResourceAuditAction.Publish, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, DateTime.SpecifyKind(EventResourceTestData.Now, DateTimeKind.Unspecified)))
            .Throws<ArgumentException>();
    }
    [Test]
    public async Task EventOwnedResourceCannotBorrowAnUnboundSessionSchedule()
    {
        var (resource, parent, subject) = EventResourceTestData.CreateDraft(sessionOwned: false);
        resource.ReplacePolicy(EventResourceAvailability.Create(
                startAnchor: EventResourceAvailabilityAnchorEnum.SessionStart, startOffset: TimeSpan.Zero),
            resource.AudienceRules, resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(() => resource.Publish(parent, true, resource.ConcurrencyStamp, subject, EventResourceTestData.Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DeletedResourceCannotBeResurrectedThroughPersistenceInterface()
    {
        var (resource, _, subject) = EventResourceTestData.CreatePublished();
        resource.Delete(resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        await Assert.That(() => ((ISoftDeletable)resource).IsDeleted = false).Throws<InvalidOperationException>();
        await Assert.That(resource.IsDeleted).IsTrue();
    }
}

internal static class EventResourceTestData
{
    internal static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
    internal static readonly EventResourceMetadata Metadata = new()
    {
        Title = "Private material",
        Kind = EventResourceKindEnum.GeneralDocument,
        DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
    };

    internal static (EventResource Resource, EventResourceParentFacts Parent, Guid Subject) CreateDraft(
        EventResourceAudienceKindEnum kind = EventResourceAudienceKindEnum.Public,
        bool requireApproval = false, bool requireCompletion = false,
        EventResourceMetadata? metadata = null, bool sessionOwned = true)
    {
        var tenant = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var session = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var scopedSession = kind is EventResourceAudienceKindEnum.SessionRegistrant
            or EventResourceAudienceKindEnum.TicketHolder or EventResourceAudienceKindEnum.SessionSpeaker
            or EventResourceAudienceKindEnum.CheckedInParticipant ? (Guid?)session : null;
        var rule = EventResourceAudienceRule.Create(tenant, eventId, id, kind, scopedSession,
            targetType: kind == EventResourceAudienceKindEnum.CheckedInParticipant ? AdmissionTargetTypeEnum.EventSession : null,
            targetId: kind == EventResourceAudienceKindEnum.CheckedInParticipant ? session : null,
            requireApproval: requireApproval, requireCompletion: requireCompletion);
        var resource = EventResource.CreateDraft(id, tenant, eventId, sessionOwned ? session : null,
            metadata ?? Metadata, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
            [rule], subject, Now);
        var instant = new DateTimeOffset(Now);
        var parent = new EventResourceParentFacts(tenant, eventId, sessionOwned ? session : null, EventStatusEnum.Published, false, true,
            EventSessionStatusEnum.Published, false,
            new EventResourceScheduleFacts(instant, instant.AddHours(4), instant, instant.AddHours(1)));
        return (resource, parent, subject);
    }

    internal static (EventResource Resource, EventResourceParentFacts Parent, Guid Subject) CreatePublished(
        EventResourceAudienceKindEnum kind = EventResourceAudienceKindEnum.Public,
        bool requireApproval = false, bool requireCompletion = false,
        EventResourceMetadata? metadata = null)
    {
        var scenario = CreateDraft(kind, requireApproval, requireCompletion, metadata);
        scenario.Resource.SetStoredFile(Guid.CreateVersion7(), scenario.Resource.ConcurrencyStamp, scenario.Subject, Now);
        scenario.Resource.Publish(scenario.Parent, true, scenario.Resource.ConcurrencyStamp, scenario.Subject, Now);
        return scenario;
    }

    internal static EventResourceAccessDecision Decision(EventResource resource, EventResourceParentFacts parent,
        Guid? subject, IEnumerable<EventResourceAudienceFact>? audience = null, bool machine = false,
        bool payloadSafe = true) => EventResourceAccessRules.Evaluate(resource,
            new EventResourceAccessFacts(resource.TenantId, subject, machine, parent, audience ?? [], payloadSafe),
            new DateTimeOffset(Now));
}
