using System.Text.Json;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Profiles;

[Category("EventClaimsMapping")]
public sealed class EventOrganizerClaimMappingTests
{
    [Test]
    public async Task EventListMapping_ProjectsProvenanceCode()
    {
        var eventEntity = new Explore.Domain.Event
        {
            Title = "Community program",
            Actor = new Actor
            {
                ActorType = new ActorType { MasterCode = "USER", FullName = "User" },
                Pii = new ActorPii { DisplayName = "Reporter" }
            },
            Tenant = new Tenant
            {
                FullName = "Test tenant",
                Slug = "test",
                TenantStatus = new TenantStatus { MasterCode = "ACTIVE", FullName = "Active" }
            },
            VisibilityType = new VisibilityType { MasterCode = "PUBLIC", FullName = "Public" },
            EventStatus = new EventStatus { MasterCode = "PUBLISHED", FullName = "Published" },
            EventFormat = new EventFormat { MasterCode = "LOCAL", FullName = "Local" },
            EventProvenanceType = new EventProvenanceType
            {
                Id = 2,
                MasterCode = "COMMUNITY_REPORTED",
                FullName = "Community reported"
            }
        };

        var dto = EventMapper.ToListItem(eventEntity);

        await Assert.That(dto.ProvenanceTypeCode).IsEqualTo("COMMUNITY_REPORTED");
    }

    [Test]
    public async Task EventOrganizerClaimMapping_ProjectsClaimantActorOwnership()
    {
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var eventId = Guid.Parse("01900000-0000-7000-8000-000000000002");
        var claimantActorId = Guid.Parse("01900000-0000-7000-8000-000000000003");
        var claimantGroupId = Guid.Parse("01900000-0000-7000-8000-000000000004");
        var claim = EventOrganizerClaim.CreatePending(
            tenantId,
            eventId,
            claimantActorId,
            "domain-proof",
            "bounded-reference",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        typeof(EventOrganizerClaim).GetProperty(nameof(EventOrganizerClaim.ClaimantActor))!
            .SetValue(claim, new Actor
            {
                Id = claimantActorId,
                GroupId = claimantGroupId,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Claimant group" }
            });

        var dto = EventMapper.ToDetail(claim);

        await Assert.That(dto.ClaimantActorGroupId).IsEqualTo(claimantGroupId);
        await Assert.That(dto.ClaimantActorUserId).IsNull();
        await Assert.That(dto.ClaimantActorOrganizationId).IsNull();
    }

    [Test]
    public async Task EventMapping_ProjectsOrganizerAuthorityWithoutSerializingIt()
    {
        var organizerActorId = Guid.NewGuid();
        var organizerGroupId = Guid.NewGuid();
        var eventEntity = new Explore.Domain.Event
        {
            Title = "Publisher differs from organizer",
            Actor = new Actor
            {
                ActorType = new ActorType { MasterCode = "USER", FullName = "User" },
                Pii = new ActorPii { DisplayName = "Publisher" }
            },
            OrganizerActorId = organizerActorId,
            OrganizerActor = new Actor
            {
                Id = organizerActorId,
                GroupId = organizerGroupId,
                ActorType = new ActorType { MasterCode = "GROUP", FullName = "Group" },
                Pii = new ActorPii { DisplayName = "Verified organizer" }
            },
            Tenant = new Tenant { FullName = "Test tenant", Slug = "test", TenantStatus = new TenantStatus { MasterCode = "ACTIVE", FullName = "Active" } },
            VisibilityType = new VisibilityType { MasterCode = "PUBLIC", FullName = "Public" },
            EventStatus = new EventStatus { MasterCode = "PUBLISHED", FullName = "Published" },
            EventFormat = new EventFormat { MasterCode = "LOCAL", FullName = "Local" }
        };

        var dto = EventMapper.ToDetail(eventEntity)!;
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(dto.OrganizerActorGroupId).IsEqualTo(organizerGroupId);
        await Assert.That(dto.OrganizerActorUserId).IsNull();
        await Assert.That(dto.OrganizerActorOrganizationId).IsNull();
        await Assert.That(json).DoesNotContain("organizerActorGroupId");
    }

    [Test]
    [Arguments(null, false)]
    [Arguments((int)ParticipationHandlingModeEnum.PlatformManaged, false)]
    [Arguments((int)ParticipationHandlingModeEnum.ExternalManaged, true)]
    public async Task EventMapping_ProjectsOnlyCompatibleActiveExternalRegistrationActions(
        int? participationHandlingModeId,
        bool expectedVisible)
    {
        Guid tenantId = Guid.CreateVersion7();
        var eventEntity = new Explore.Domain.Event
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Title = "External registration event",
            Actor = new Actor
            {
                ActorType = new ActorType { MasterCode = "USER", FullName = "User" },
                Pii = new ActorPii { DisplayName = "Publisher" }
            },
            Tenant = new Tenant { FullName = "Test tenant", Slug = "test", TenantStatus = new TenantStatus { MasterCode = "ACTIVE", FullName = "Active" } },
            VisibilityType = new VisibilityType { MasterCode = "PUBLIC", FullName = "Public" },
            EventStatus = new EventStatus { MasterCode = "PUBLISHED", FullName = "Published" },
            EventFormat = new EventFormat { MasterCode = "LOCAL", FullName = "Local" }
        };
        if (participationHandlingModeId is { } modeId)
        {
            eventEntity.ParticipationConfiguration = EventParticipationConfiguration.Create(
                eventEntity.Id,
                tenantId,
                modeId,
                (int)AdvanceRegistrationObligationEnum.Required,
                modeId == (int)ParticipationHandlingModeEnum.PlatformManaged
                    ? (int)IdentityAccessModeEnum.AccountRequired
                    : null,
                guestRecoveryPolicy: null,
                DateTime.UtcNow);
        }

        var action = new EventPublicAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventEntity.Id,
            EventPublicActionKindId = (int)EventPublicActionKindEnum.ExternalRegistration,
            HealthStateId = (int)EventPublicActionHealthStateEnum.Active
        };
        action.SetDestination(ExternalActionUrl.Create("https://registration.example.test/event"));
        eventEntity.PublicActions.Add(action);

        var dto = EventMapper.ToDetail(eventEntity)!;

        await Assert.That(dto.PublicActions.Count).IsEqualTo(expectedVisible ? 1 : 0);
    }
}
