using System.Reflection;
using System.Text.Json;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Routing;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class EventAuthorityLinkPolicyTests
{
    [Test]
    public async Task PublicActionCollectionPolicy_IsIndependentAndUsesStoredRedirectRoute()
    {
        var policyType = typeof(EventPublicActionCollectionLinkPolicy);
        var tenantId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var action = new EventPublicActionDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventActorId = Guid.CreateVersion7(),
            EventActorOrganizationId = organizationId,
            KindId = (int)EventPublicActionKindEnum.ExternalRegistration,
            Url = "https://registration.example/events/1",
            DestinationDomain = "registration.example"
        };

        var links = new EventPublicActionCollectionLinkPolicy().GetItemLinks(action, null).ToList();
        var destination = links.Single(link => link.Rel == LinkRelations.ExternalRegistration);

        await Assert.That(policyType.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            .Any(field => field.FieldType == typeof(EventPublicActionDetailLinkPolicy))).IsFalse();
        await Assert.That(destination.RouteName).IsEqualTo(Explore.API.Hateoas.RouteNames.RedirectEventPublicAction);
        await Assert.That(destination.RouteValues?.ToString()).DoesNotContain(action.Url);
        var edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(edit.PermissionFacts).IsTypeOf<EventAuthorizationFacts>();
        var editFacts = (EventAuthorizationFacts)edit.PermissionFacts!;
        await Assert.That(editFacts.TenantId).IsEqualTo(tenantId);
        await Assert.That(editFacts.OrganizationId).IsEqualTo(organizationId);
    }

    [Test]
    public async Task PublicActionDetailPolicy_PassesEventDetailSurfaceQueryValue()
    {
        var action = new EventPublicActionDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            EventActorId = Guid.CreateVersion7(),
            EventActorOrganizationId = Guid.CreateVersion7(),
            KindId = (int)EventPublicActionKindEnum.ExternalEventPage,
            Url = "https://events.example/item",
            DestinationDomain = "events.example"
        };

        var links = new EventPublicActionDetailLinkPolicy().GetLinks(action, null).ToList();
        var destination = links.Single(link => link.Rel == LinkRelations.ExternalEventPage);
        var routeValues = new RouteValueDictionary(destination.RouteValues);

        await Assert.That(routeValues["surface"]?.ToString()).IsEqualTo("event_detail");
        await Assert.That(routeValues["eventId"]?.ToString()).IsEqualTo(action.EventId.ToString());
        await Assert.That(routeValues["actionId"]?.ToString()).IsEqualTo(action.Id.ToString());
    }

    [Test]
    public async Task OrganizerClaimCollectionPolicy_IsIndependentAndPermissionBound()
    {
        var policyType = typeof(EventOrganizerClaimCollectionLinkPolicy);
        var tenantId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var claimantOrganizationId = Guid.CreateVersion7();
        var claim = new EventOrganizerClaimDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventActorId = Guid.CreateVersion7(),
            EventActorOrganizationId = organizationId,
            ClaimantActorId = Guid.CreateVersion7(),
            ClaimantActorOrganizationId = claimantOrganizationId,
            StatusId = (int)EventOrganizerClaimStatusEnum.Pending,
            StatusCode = "PENDING",
            EvidenceType = "domain-proof",
            EvidenceReference = "bounded-reference"
        };

        var links = new EventOrganizerClaimCollectionLinkPolicy().GetItemLinks(claim, null).ToList();
        var review = links.Single(link => link.Rel == LinkRelations.ReviewClaim);
        var withdraw = links.Single(link => link.Rel == LinkRelations.WithdrawClaim);

        await Assert.That(policyType.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            .Any(field => field.FieldType == typeof(EventOrganizerClaimDetailLinkPolicy))).IsFalse();
        await Assert.That(review.PermissionResourceKind).IsEqualTo(ResourceKinds.EventOrganizerClaim);
        await Assert.That(review.PermissionAction).IsEqualTo(AuthorizationActions.Events.ReviewOrganizerClaim);
        await Assert.That(review.PermissionResourceId).IsEqualTo(claim.EventId.ToString());
        await Assert.That(review.PermissionFacts).IsTypeOf<EventOrganizerClaimAuthorizationFacts>();
        var reviewFacts = (EventOrganizerClaimAuthorizationFacts)review.PermissionFacts!;
        await Assert.That(reviewFacts.TenantId).IsEqualTo(tenantId);
        await Assert.That(reviewFacts.ClaimId).IsEqualTo(claim.Id);
        await Assert.That(reviewFacts.Status).IsEqualTo("PENDING");
        await Assert.That(withdraw.PermissionAction).IsEqualTo(AuthorizationActions.Events.WithdrawOrganizerClaim);
        await Assert.That(((EventOrganizerClaimAuthorizationFacts)withdraw.PermissionFacts!).ClaimantOrganizationId)
            .IsEqualTo(claimantOrganizationId);
    }

    [Test]
    public async Task OrganizerClaimPolicy_TerminalClaimOmitsWithdrawAndReviewCandidates()
    {
        var claim = new EventOrganizerClaimDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            EventActorId = Guid.CreateVersion7(),
            ClaimantActorId = Guid.CreateVersion7(),
            StatusId = (int)EventOrganizerClaimStatusEnum.Approved,
            StatusCode = "APPROVED",
            EvidenceType = "domain-proof",
            EvidenceReference = "bounded-reference"
        };

        var links = new EventOrganizerClaimDetailLinkPolicy().GetLinks(claim, null).ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.WithdrawClaim)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.ReviewClaim)).IsFalse();
    }

    [Test]
    public async Task PublicActionDto_SerializesFixedExternalGuidanceWithoutAuthorizationMetadata()
    {
        var action = new EventPublicActionDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            EventActorId = Guid.CreateVersion7(),
            EventActorOrganizationId = Guid.CreateVersion7(),
            KindId = (int)EventPublicActionKindEnum.ExternalEventPage,
            Url = "https://events.example/item",
            DestinationDomain = "events.example"
        };

        var json = JsonSerializer.Serialize(action, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(json).Contains("\"openInNewTab\":true");
        await Assert.That(json).Contains("\"rel\":\"noopener noreferrer\"");
        await Assert.That(json).DoesNotContain("tenantId");
        await Assert.That(json).DoesNotContain("eventActorId");
    }

    [Test]
    public async Task OrganizerClaimDto_DoesNotSerializeClaimantOwnershipMetadata()
    {
        var claim = new EventOrganizerClaimDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            ClaimantActorId = Guid.CreateVersion7(),
            ClaimantActorUserId = Guid.CreateVersion7(),
            ClaimantActorOrganizationId = Guid.CreateVersion7(),
            ClaimantActorGroupId = Guid.CreateVersion7(),
            EvidenceType = "domain-proof",
            EvidenceReference = "bounded-reference"
        };

        var json = JsonSerializer.Serialize(claim, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(json).DoesNotContain("claimantActorUserId");
        await Assert.That(json).DoesNotContain("claimantActorOrganizationId");
        await Assert.That(json).DoesNotContain("claimantActorGroupId");
    }
}
