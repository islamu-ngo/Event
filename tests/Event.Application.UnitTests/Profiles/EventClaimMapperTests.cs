using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventOrganizerClaims.Handlers.Queries;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

[Category("EventClaimsMapping")]
public sealed class EventClaimMapperTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ClaimProjection_PreservesAuthorityButDoesNotSerializeItOrRecurse(bool unloadedPii)
    {
        var eventId = Guid.Parse("01900000-0000-7000-8000-000000000011");
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000012");
        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000013");
        var claim = EventOrganizerClaim.CreatePending(tenantId, eventId, groupId, "domain-proof", "bounded-reference", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var parent = new Explore.Domain.Event
        {
            Id = eventId, TenantId = tenantId, Title = "Claimed event", ActorId = groupId,
            Actor = new Actor { GroupId = groupId, Pii = new ActorPii { DisplayName = "Publisher" }, ActorType = null! },
            Tenant = null!, VisibilityType = null!, EventStatus = null!, EventFormat = null!,
            EventProvenanceTypeId = 2, EventProvenanceType = new EventProvenanceType { MasterCode = "COMMUNITY_REPORTED", FullName = "Community reported" }
        };
        if (unloadedPii)
            parent.Actor.Pii = null!;
        parent.OrganizerClaims.Add(claim);
        typeof(EventOrganizerClaim).GetProperty(nameof(EventOrganizerClaim.Event))!.SetValue(claim, parent);
        typeof(EventOrganizerClaim).GetProperty(nameof(EventOrganizerClaim.ClaimantActor))!.SetValue(claim, parent.Actor);
        var repository = Substitute.For<IEventOrganizerClaimRepository>();
        repository.GetDetailsAsync(claim.Id, false, Arg.Any<CancellationToken>()).Returns(claim);
        var handler = new GetEventOrganizerClaimRequestHandler(repository);
        var dto = await handler.Handle(new GetEventOrganizerClaimRequest(eventId, claim.Id), CancellationToken.None);
        var wrongParent = await handler.Handle(new GetEventOrganizerClaimRequest(groupId, claim.Id), CancellationToken.None);
        await Assert.That(wrongParent).IsNull();
        await Assert.That(dto).IsNotNull();
        await Assert.That(dto!.ClaimantActorGroupId).IsEqualTo(groupId);
        await Assert.That(dto.EventActorId).IsEqualTo(groupId);
        await Assert.That(dto.EventActorGroupId).IsEqualTo(groupId);
        await Assert.That(dto.EventProvenanceTypeCode).IsEqualTo("COMMUNITY_REPORTED");
        await Assert.That(dto.ClaimantActorDisplayName).IsEqualTo(unloadedPii ? null : "Publisher");
        await Assert.That(dto.EvidenceReference).IsEqualTo("bounded-reference");
        await Assert.That(dto.CreatedAt).IsEqualTo(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(dto.ConcurrencyStamp).IsEqualTo(claim.ConcurrencyStamp);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto));
        await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(14);
        await Assert.That(json.RootElement.TryGetProperty("EventActorGroupId", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("TenantId", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("Event", out _)).IsFalse();
    }

    [Test]
    public async Task UnloadedClaimNavigations_PreserveNullsAndEmptyAuthority()
    {
        var claim = EventOrganizerClaim.CreatePending(Guid.Parse("01900000-0000-7000-8000-000000000001"), Guid.Parse("01900000-0000-7000-8000-000000000002"), Guid.Parse("01900000-0000-7000-8000-000000000003"), "proof", "reference", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var dto = EventMapper.ToDetail(claim);
        await Assert.That(dto.EventActorId).IsEqualTo(Guid.Empty);
        await Assert.That(dto.EventProvenanceTypeId).IsEqualTo(0);
        await Assert.That(dto.StatusCode).IsNull();
        await Assert.That(dto.StatusName).IsNull();
        await Assert.That(dto.ClaimantActorDisplayName).IsNull();
        await Assert.That(dto.DecidedAt).IsNull();
    }
}
