using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventOrganizerClaimTests
{
    [Test]
    public async Task Approve_PendingClaim_AssignsOrganizerWithoutChangingProvenance()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var claimantActorId = Guid.CreateVersion7();
        var reviewerUserId = Guid.CreateVersion7();
        var @event = CreateEvent(tenantId, eventId);
        var claim = EventOrganizerClaim.CreatePending(
            tenantId,
            eventId,
            claimantActorId,
            "DOMAIN_EMAIL",
            "evidence-reference",
            DomainTestClock.UtcNow);

        claim.Approve(@event, reviewerUserId, "VERIFIED_CONTROL", DomainTestClock.UtcNow);

        await Assert.That(claim.StatusId).IsEqualTo((int)EventOrganizerClaimStatusEnum.Approved);
        await Assert.That(@event.OrganizerActorId).IsEqualTo(claimantActorId);
        await Assert.That(@event.EventProvenanceTypeId).IsEqualTo((int)EventProvenanceTypeEnum.CommunityReported);
        await Assert.That(@event.SubmittedByUserId).IsNotNull();
    }

    [Test]
    public async Task Approve_AlreadyApprovedClaim_Throws()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var reviewerUserId = Guid.CreateVersion7();
        var @event = CreateEvent(tenantId, eventId);
        var claim = EventOrganizerClaim.CreatePending(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            "DOMAIN_EMAIL",
            "evidence-reference",
            DomainTestClock.UtcNow);

        claim.Approve(@event, reviewerUserId, "VERIFIED_CONTROL", DomainTestClock.UtcNow);

        await Assert.That(() => claim.Approve(@event, reviewerUserId, "VERIFIED_CONTROL", DomainTestClock.UtcNow))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Approve_EventFromDifferentTenant_ThrowsWithoutAssigningOrganizer()
    {
        var eventId = Guid.CreateVersion7();
        var claim = EventOrganizerClaim.CreatePending(
            Guid.CreateVersion7(),
            eventId,
            Guid.CreateVersion7(),
            "DOMAIN_EMAIL",
            "evidence-reference",
            DomainTestClock.UtcNow);
        var @event = CreateEvent(Guid.CreateVersion7(), eventId);

        await Assert.That(() => claim.Approve(@event, Guid.CreateVersion7(), "VERIFIED_CONTROL", DomainTestClock.UtcNow))
            .Throws<InvalidOperationException>();
        await Assert.That(@event.OrganizerActorId).IsNull();
    }

    private static global::Explore.Domain.Event CreateEvent(Guid tenantId, Guid eventId) => new()
    {
        Id = eventId,
        TenantId = tenantId,
        Title = "Community event",
        Actor = null!,
        Tenant = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!,
        EventProvenanceTypeId = (int)EventProvenanceTypeEnum.CommunityReported,
        SubmittedByUserId = Guid.CreateVersion7()
    };
}
