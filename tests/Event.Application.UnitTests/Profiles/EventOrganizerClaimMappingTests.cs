using System.Text.Json;
using AutoMapper;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Profiles;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventOrganizerClaimMappingTests
{
    [Test]
    public async Task EventListMapping_ProjectsProvenanceCode()
    {
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<EventMappingProfile>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
#else
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<EventMappingProfile>());
#endif
        var mapper = configuration.CreateMapper();
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

        var dto = mapper.Map<Explore.Application.DTOs.Event.EventListDto>(eventEntity);

        await Assert.That(dto.ProvenanceTypeCode).IsEqualTo("COMMUNITY_REPORTED");
    }

    [Test]
    public async Task EventOrganizerClaimMapping_ProjectsClaimantActorOwnership()
    {
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<EventMappingProfile>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
#else
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<EventMappingProfile>());
#endif
        var mapper = configuration.CreateMapper();
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var claimantActorId = Guid.NewGuid();
        var claimantGroupId = Guid.NewGuid();
        var claim = EventOrganizerClaim.CreatePending(
            tenantId,
            eventId,
            claimantActorId,
            "domain-proof",
            "bounded-reference",
            DateTime.UtcNow);
        typeof(EventOrganizerClaim).GetProperty(nameof(EventOrganizerClaim.ClaimantActor))!
            .SetValue(claim, new Actor
            {
                Id = claimantActorId,
                GroupId = claimantGroupId,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Claimant group" }
            });

        var dto = mapper.Map<EventOrganizerClaimDto>(claim);

        await Assert.That(dto.ClaimantActorGroupId).IsEqualTo(claimantGroupId);
        await Assert.That(dto.ClaimantActorUserId).IsNull();
        await Assert.That(dto.ClaimantActorOrganizationId).IsNull();
    }

    [Test]
    public async Task EventMapping_ProjectsOrganizerAuthorityWithoutSerializingIt()
    {
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<EventMappingProfile>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
#else
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<EventMappingProfile>());
#endif
        var mapper = configuration.CreateMapper();
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

        var dto = mapper.Map<EventDto>(eventEntity);
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

#if USE_COMMERCIAL_LUCKYPENNY_LIBS
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<EventMappingProfile>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
#else
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<EventMappingProfile>());
#endif
        var dto = configuration.CreateMapper().Map<EventDto>(eventEntity);

        await Assert.That(dto.PublicActions.Count).IsEqualTo(expectedVisible ? 1 : 0);
    }
}
