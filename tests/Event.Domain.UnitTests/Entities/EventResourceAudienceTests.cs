using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventResourceAudienceTests
{
    [Test]
    public async Task EveryRestrictedAudienceRequiresCurrentSameSubjectAndScope()
    {
        foreach (var kind in Enum.GetValues<EventResourceAudienceKindEnum>().Where(value => value != EventResourceAudienceKindEnum.Public))
        {
            var (resource, parent, subject) = EventResourceTestData.CreatePublished(kind);
            var fact = Fact(resource, subject, kind);
            await Assert.That(EventResourceTestData.Decision(resource, parent, subject, [fact]).CanAccess).IsTrue();
            foreach (var invalid in new[]
            {
                fact with { IsCurrent = false },
                fact with { SubjectUserId = Guid.CreateVersion7() },
                fact with { TenantId = Guid.CreateVersion7() },
                fact with { EventId = Guid.CreateVersion7() },
                fact with { Kind = (EventResourceAudienceKindEnum)99 },
                fact with { ExpiresAtUtc = new DateTimeOffset(EventResourceTestData.Now) },
                fact with { ValidFromUtc = new DateTimeOffset(EventResourceTestData.Now.AddMinutes(1)) }
            })
            {
                await Assert.That(EventResourceTestData.Decision(resource, parent, subject, [invalid]).CanAccess).IsFalse();
            }

            await Assert.That(EventResourceTestData.Decision(resource, parent, subject, []).CanAccess).IsFalse();
            await Assert.That(EventResourceTestData.Decision(resource, parent, null, [fact]).CanAccess).IsFalse();
            await Assert.That(EventResourceTestData.Decision(resource, parent, subject, [fact], machine: true).CanAccess).IsFalse();
        }
    }

    [Test]
    public async Task ApprovalAndCompletionCannotBeCombinedAcrossSubjectsOrRows()
    {
        var (resource, parent, subject) = EventResourceTestData.CreatePublished(
            EventResourceAudienceKindEnum.SessionRegistrant, requireApproval: true, requireCompletion: true);
        var approved = Fact(resource, subject, EventResourceAudienceKindEnum.SessionRegistrant)
            with { ApprovedSubjectUserId = subject };
        var otherCompleted = approved with
        {
            SubjectUserId = Guid.CreateVersion7(),
            ApprovedSubjectUserId = null,
            CompletedSubjectUserId = Guid.CreateVersion7()
        };
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject, [approved, otherCompleted]).CanAccess).IsFalse();
        var sameSubjectCompleted = approved with { ApprovedSubjectUserId = null, CompletedSubjectUserId = subject };
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject, [approved, sameSubjectCompleted]).CanAccess).IsFalse();
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject,
            [approved with { CompletedSubjectUserId = Guid.CreateVersion7() }]).CanAccess).IsFalse();
        var complete = approved with { CompletedSubjectUserId = subject };
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject, [complete]).CanAccess).IsTrue();
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject,
            [complete with { OrderConfirmed = false }]).CanAccess).IsFalse();
    }

    [Test]
    public async Task SessionAndCheckInTargetsMustMatchExactly()
    {
        foreach (var kind in new[] { EventResourceAudienceKindEnum.SessionRegistrant,
            EventResourceAudienceKindEnum.TicketHolder, EventResourceAudienceKindEnum.SessionSpeaker,
            EventResourceAudienceKindEnum.CheckedInParticipant })
        {
            var (resource, parent, subject) = EventResourceTestData.CreatePublished(kind);
            var fact = Fact(resource, subject, kind);
            await Assert.That(EventResourceTestData.Decision(resource, parent, subject,
                [fact with { EventSessionId = Guid.CreateVersion7() }]).CanAccess).IsFalse();
            if (kind == EventResourceAudienceKindEnum.CheckedInParticipant)
            {
                await Assert.That(EventResourceTestData.Decision(resource, parent, subject,
                    [fact with { AdmissionTargetId = Guid.CreateVersion7() }]).CanAccess).IsFalse();
                await Assert.That(EventResourceTestData.Decision(resource, parent, subject,
                    [fact with { AdmissionTargetType = AdmissionTargetTypeEnum.Event }]).CanAccess).IsFalse();
            }
        }
    }

    [Test]
    public async Task TeaserAndPublicMetadataDoNotGrantContent()
    {
        foreach (var disclosure in Enum.GetValues<EventResourceDisclosureModeEnum>())
        {
            var (resource, parent, _) = EventResourceTestData.CreatePublished(
                EventResourceAudienceKindEnum.AuthenticatedTenantMember,
                metadata: EventResourceTestData.Metadata with { DisclosureMode = disclosure, PublicTitle = "Public summary" });
            var decision = EventResourceTestData.Decision(resource, parent, null);
            await Assert.That(decision.DiscloseMetadata).IsEqualTo(disclosure != EventResourceDisclosureModeEnum.EligibleOnly);
            await Assert.That(decision.DisclosePrivateMetadata).IsFalse();
            await Assert.That(decision.CanAccess).IsFalse();
        }
    }

    [Test]
    public async Task PublicAudienceDoesNotNeedSyntheticIdentityButStillNeedsSafePayload()
    {
        var (resource, parent, _) = EventResourceTestData.CreatePublished();
        await Assert.That(EventResourceTestData.Decision(resource, parent, null).CanAccess).IsTrue();
        await Assert.That(EventResourceTestData.Decision(resource, parent, null, payloadSafe: false).CanAccess).IsFalse();
    }

    [Test]
    public async Task EmptyMixedAndForeignPoliciesRejectWithoutPartialReplacement()
    {
        var (resource, _, subject) = EventResourceTestData.CreateDraft();
        var availability = EventResourceAvailability.Create();
        await Assert.That(() => resource.ReplacePolicy(availability, [], resource.ConcurrencyStamp, subject, EventResourceTestData.Now))
            .Throws<ArgumentException>();
        var restricted = EventResourceAudienceRule.Create(resource.TenantId, resource.EventId, resource.Id,
            EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        await Assert.That(() => resource.ReplacePolicy(availability, [resource.AudienceRules.Single(), restricted],
            resource.ConcurrencyStamp, subject, EventResourceTestData.Now)).Throws<ArgumentException>();
        var foreign = EventResourceAudienceRule.Create(Guid.CreateVersion7(), resource.EventId, resource.Id,
            EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        await Assert.That(() => resource.ReplacePolicy(availability, [foreign],
            resource.ConcurrencyStamp, subject, EventResourceTestData.Now)).Throws<ArgumentException>();
        var wrongSession = EventResourceAudienceRule.Create(resource.TenantId, resource.EventId, resource.Id,
            EventResourceAudienceKindEnum.SessionSpeaker, Guid.CreateVersion7());
        await Assert.That(() => resource.ReplacePolicy(availability, [wrongSession],
            resource.ConcurrencyStamp, subject, EventResourceTestData.Now)).Throws<ArgumentException>();
        await Assert.That(resource.AudienceRules.Single().AudienceKindId).IsEqualTo((int)EventResourceAudienceKindEnum.Public);
    }

    [Test]
    public async Task AlternativeRulesAreOrAndPublishedPoliciesSnapshotCallerCollections()
    {
        var (resource, parent, subject) = EventResourceTestData.CreatePublished(EventResourceAudienceKindEnum.Organizer);
        var member = EventResourceAudienceRule.Create(resource.TenantId, resource.EventId, resource.Id,
            EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        List<EventResourceAudienceRule> rules = [resource.AudienceRules.Single(), member];
        resource.ReplacePolicy(EventResourceAvailability.Create(), rules, resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        rules.Clear();
        List<EventResourceAudienceFact> evidence = [Fact(resource, subject, EventResourceAudienceKindEnum.AuthenticatedTenantMember)];
        var frozen = new EventResourceAccessFacts(resource.TenantId, subject, false, parent, evidence, true);
        evidence.Clear();
        await Assert.That(resource.AudienceRules.Count).IsEqualTo(2);
        await Assert.That(EventResourceAccessRules.Evaluate(resource, frozen, new DateTimeOffset(EventResourceTestData.Now)).CanAccess).IsTrue();
    }

    [Test]
    public async Task UnknownAndContradictoryRuleQualifiersAreRejected()
    {
        var tenant = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var resourceId = Guid.CreateVersion7();
        await Assert.That(() => EventResourceAudienceRule.Create(tenant, eventId, resourceId, (EventResourceAudienceKindEnum)99))
            .Throws<ArgumentException>();
        await Assert.That(() => EventResourceAudienceRule.Create(tenant, eventId, resourceId,
            EventResourceAudienceKindEnum.Public, requireApproval: true)).Throws<ArgumentException>();
        await Assert.That(() => EventResourceAudienceRule.Create(tenant, eventId, resourceId,
            EventResourceAudienceKindEnum.SessionSpeaker)).Throws<ArgumentException>();
        await Assert.That(() => EventResourceAudienceRule.Create(tenant, eventId, resourceId,
            EventResourceAudienceKindEnum.CheckedInParticipant, targetType: AdmissionTargetTypeEnum.EventSession))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CheckInTargetIdentityIsDistinctFromItsSessionScope()
    {
        var (resource, parent, subject) = EventResourceTestData.CreatePublished(EventResourceAudienceKindEnum.Organizer);
        var targetId = Guid.CreateVersion7();
        var rule = EventResourceAudienceRule.Create(resource.TenantId, resource.EventId, resource.Id,
            EventResourceAudienceKindEnum.CheckedInParticipant, resource.EventSessionId,
            targetType: AdmissionTargetTypeEnum.EventSession, targetId: targetId);
        resource.ReplacePolicy(EventResourceAvailability.Create(), [rule],
            resource.ConcurrencyStamp, subject, EventResourceTestData.Now);
        var fact = Fact(resource, subject, EventResourceAudienceKindEnum.CheckedInParticipant)
            with { AdmissionTargetId = targetId };
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject, [fact]).CanAccess).IsTrue();
        await Assert.That(EventResourceTestData.Decision(resource, parent, subject,
            [fact with { AdmissionTargetId = resource.EventSessionId }]).CanAccess).IsFalse();
    }

    private static EventResourceAudienceFact Fact(EventResource resource, Guid subject, EventResourceAudienceKindEnum kind) => new()
    {
        TenantId = resource.TenantId,
        EventId = resource.EventId,
        SubjectUserId = subject,
        Kind = kind,
        IsCurrent = true,
        EventSessionId = resource.EventSessionId,
        OrderConfirmed = true,
        AdmissionTargetType = AdmissionTargetTypeEnum.EventSession,
        AdmissionTargetId = resource.EventSessionId
    };
}
