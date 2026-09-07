using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Federation;

public sealed class AtprotoEventPublicationSnapshotFactoryTests
{
    [Test]
    public async Task CreateAsync_PublicPersistedEvent_BuildsImmutableSnapshot()
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        AtprotoEventPublicationSnapshotFactory factory = CreateFactory();

        AtprotoEventPublicationSnapshotResult result = await factory.CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsTrue();
        await Assert.That(result.Snapshot!.Name).IsEqualTo("Public event");
        await Assert.That(result.Snapshot.Organizer.DisplayName).IsEqualTo("Organizer");
        await Assert.That(result.Snapshot.RsvpExpected).IsFalse();
        await Assert.That(result.Snapshot.Uris.Any(uri => uri.Name == "Registration")).IsFalse();
        await Assert.That(result.Snapshot.ToString()).DoesNotContain("private-provider-canary");
    }

    [Test]
    public async Task CreateAsync_RequiredParticipationWithActiveExternalAction_DerivesRsvpAndRegistrationUri()
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        graph.Event.ParticipationConfiguration!.Reconfigure(
            (int)ParticipationHandlingModeEnum.ExternalManaged,
            (int)AdvanceRegistrationObligationEnum.Required,
            identityAccessModeId: null,
            guestRecoveryPolicy: null);
        var action = new EventPublicAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.Event.TenantId,
            EventId = graph.Event.Id,
            EventPublicActionKindId = (int)EventPublicActionKindEnum.ExternalRegistration,
            HealthStateId = (int)EventPublicActionHealthStateEnum.Active,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        action.SetDestination(ExternalActionUrl.Create("https://registration.example.test/public-event"));
        graph.Event.PublicActions.Add(action);

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsTrue();
        await Assert.That(result.Snapshot!.RsvpExpected).IsTrue();
        await Assert.That(result.Snapshot.Uris).Contains(uri =>
            uri.Name == "Registration"
            && uri.Uri == "https://registration.example.test/public-event");
    }

    [Test]
    public async Task CreateAsync_PlatformManagedEvent_HidesStaleExternalRegistrationUri()
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        graph.Event.ParticipationConfiguration!.Reconfigure(
            (int)ParticipationHandlingModeEnum.PlatformManaged,
            (int)AdvanceRegistrationObligationEnum.Required,
            (int)IdentityAccessModeEnum.AccountRequired,
            guestRecoveryPolicy: null);
        var action = new EventPublicAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.Event.TenantId,
            EventId = graph.Event.Id,
            EventPublicActionKindId = (int)EventPublicActionKindEnum.ExternalRegistration,
            HealthStateId = (int)EventPublicActionHealthStateEnum.Active,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        action.SetDestination(ExternalActionUrl.Create("https://registration.example.test/stale"));
        graph.Event.PublicActions.Add(action);

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsTrue();
        await Assert.That(result.Snapshot!.Uris.Any(uri => uri.Name == "Registration")).IsFalse();
    }

    [Test]
    public async Task CreateAsync_RawLocationWithoutEventLocationAssociation_IsIneligible()
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        EventSession session = CreateSession(graph.Event);
        session.LocationId = Guid.CreateVersion7();
        graph = graph with { Sessions = [session] };

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsFalse();
        await Assert.That(result.Errors).Contains(error => error.Contains("raw location", StringComparison.Ordinal));
    }

    [Test]
    public async Task CreateAsync_CrossTenantGraphRow_IsIneligible()
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        EventSession session = CreateSession(graph.Event);
        session.TenantId = Guid.CreateVersion7();
        graph = graph with { Sessions = [session] };

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsFalse();
        await Assert.That(result.Errors).Contains(error => error.Contains("cross-tenant", StringComparison.Ordinal));
    }

    [Test]
    public async Task CreateAsync_LegacyLocationMismatchWithEventLocation_IsIneligible()
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Location",
            Country = "BE",
            City = "Brussels",
            TenantId = graph.Event.TenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        EventLocation placement = EventLocation.CreatePhysical(
            graph.Event.TenantId,
            graph.Event.Id,
            location.Id,
            Guid.CreateVersion7(),
            DateTime.UtcNow);
        EventSession session = CreateSession(graph.Event);
        session.AssignEventLocation(placement);
        session.LocationId = Guid.CreateVersion7();
        graph = graph with { EventLocations = [placement], Sessions = [session] };

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsFalse();
        await Assert.That(result.Errors).Contains(error => error.Contains("does not match", StringComparison.Ordinal));
    }

    [Test]
    public async Task CreateAsync_StandaloneEventLocation_IsEvaluatedAndIncluded()
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        EventLocation placement = EventLocation.CreateToBeAnnounced(
            graph.Event.TenantId,
            graph.Event.Id,
            Guid.CreateVersion7(),
            DateTime.UtcNow);
        graph = graph with { EventLocations = [placement] };

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsTrue();
        await Assert.That(result.Snapshot!.Locations).Count().IsEqualTo(1);
        await Assert.That(result.Snapshot.Locations[0].State).IsEqualTo(
            Explore.Application.Contracts.LocationPrivacy.EventLocationDisclosureState.ToBeAnnounced);
    }

    [Test]
    public async Task CreateAsync_PublicSessionWithoutLoadedStatus_IsIneligibleWithoutNumericFallback()
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        EventSession session = CreateSession(graph.Event);
        session.EventSessionStatus = null!;
        graph = graph with { Sessions = [session] };

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsFalse();
        await Assert.That(result.Errors).Contains(error => error.Contains("status lookup", StringComparison.Ordinal));
    }

    [Test]
    [Arguments(false, VisibilityTypeEnum.Public, false)]
    [Arguments(true, VisibilityTypeEnum.Private, false)]
    [Arguments(true, VisibilityTypeEnum.Public, true)]
    public async Task CreateAsync_NonpublicOrDeletedSeries_IsExcluded(
        bool isPublished,
        VisibilityTypeEnum visibility,
        bool isDeleted)
    {
        AtprotoEventPublicationEntityGraph graph = CreateGraph();
        graph.Event.EventSeries = new Explore.Domain.EventSeries
        {
            Id = Guid.CreateVersion7(),
            Title = "private-series-canary",
            ActorId = graph.Event.ActorId,
            Actor = graph.Event.Actor,
            IsPublished = isPublished,
            VisibilityTypeId = (int)visibility,
            VisibilityType = new VisibilityType
            {
                Id = (int)visibility,
                MasterCode = visibility.ToString().ToUpperInvariant(),
                FullName = visibility.ToString()
            },
            TenantId = graph.Event.TenantId,
            Tenant = null!,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow
        };

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsTrue();
        await Assert.That(result.Snapshot!.Series).IsNull();
        await Assert.That(AtprotoEventDescriptionFormatter.Format(result.Snapshot)).DoesNotContain("private-series-canary");
    }

    [Test]
    public async Task CreateAsync_MaximalRepositoryShapedGraph_ProjectsEveryCollectionIntoDescription()
    {
        AtprotoEventPublicationEntityGraph graph = CreateMaximalGraph();

        AtprotoEventPublicationSnapshotResult result = await CreateFactory().CreateAsync(
            graph,
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(result.IsEligible).IsTrue();
        await Assert.That(graph.EventLocations).Count().IsEqualTo(2);
        await Assert.That(graph.Sessions).Count().IsEqualTo(2);
        await Assert.That(graph.Days).Count().IsEqualTo(2);
        await Assert.That(graph.SessionGroups).IsNotEmpty();
        await Assert.That(graph.SessionGroupSessions).Count().IsEqualTo(2);
        await Assert.That(graph.AgendaItems).IsNotEmpty();
        await Assert.That(graph.SessionAgendaItems).IsNotEmpty();
        await Assert.That(graph.Categories).IsNotEmpty();
        await Assert.That(graph.Tags).IsNotEmpty();
        await Assert.That(graph.SessionCategories).IsNotEmpty();
        await Assert.That(graph.SessionTags).IsNotEmpty();
        await Assert.That(graph.SessionLanguages).IsNotEmpty();
        await Assert.That(graph.SessionSpeakers).IsNotEmpty();
        await Assert.That(graph.CustomPropertyDefinitions).IsNotEmpty();
        await Assert.That(graph.SessionCustomPropertyDefinitions).IsNotEmpty();

        string description = AtprotoEventDescriptionFormatter.Format(result.Snapshot!);
        foreach (string canary in new[]
                 {
                     "series-canary", "event-category-canary", "event-tag-canary", "day-canary",
                     "session-one-canary", "session-two-canary", "group-canary", "event-agenda-canary",
                     "session-agenda-canary", "session-category-canary", "session-tag-canary",
                     "language-canary", "speaker-canary", "event-property-canary", "event-option-canary",
                     "event-value-canary", "session-property-canary", "session-value-canary",
                     "session-option-canary", "17.25", "91.75", "2026-07-21T08:00:00.0000000+00:00",
                     "Islamic", "hackathon-canary", "Room canary", "Brussels", "organization-canary",
                     "organizer-group-canary", "Conference canary", "Open registration canary",
                     "https://cdn.example.test/event-image-canary.png",
                     "https://cdn.example.test/session-image-canary.png"
                 })
        {
            await Assert.That(description).Contains(canary);
        }
    }

    private static AtprotoEventPublicationSnapshotFactory CreateFactory()
    {
        ILocationPrivacyGovernanceService governance = Substitute.For<ILocationPrivacyGovernanceService>();
        governance.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new EffectiveLocationPrivacyGovernance(
                true,
                LocationPrivacyGovernanceReasonCode.Resolved,
                true,
                true,
                true,
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
                TimeSpan.FromDays(30)));
        return new(new(governance, new EventLocationDisclosureEvaluator()));
    }

    private static AtprotoEventPublicationEntityGraph CreateGraph()
    {
        Guid tenantId = Guid.CreateVersion7();
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = 1,
            ActorType = new ActorType { Id = 1, MasterCode = "ORG", FullName = "Organization" },
            Pii = new ActorPii { DisplayName = "Organizer" },
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        actor.AtprotoIdentities.Add(new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:private-provider-canary"))
        {
            Id = Guid.CreateVersion7(),

            ActorId = actor.Id,
            Actor = actor,
            PdsHost = "https://pds.example.test",
            IsActive = true,
            LastResolvedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            Title = "Public event",
            ActorId = actor.Id,
            Actor = actor,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = new VisibilityType { Id = (int)VisibilityTypeEnum.Public, MasterCode = "PUBLIC", FullName = "Public" },
            EventStatus = new EventStatus { Id = (int)EventStatusEnum.Draft, MasterCode = "DRAFT", FullName = "Draft" },
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = new EventFormat { Id = (int)EventFormatEnum.Digital, MasterCode = "DIGITAL", FullName = "Digital" },
            CreatedAt = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc),
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        eventEntity.ParticipationConfiguration = EventParticipationConfiguration.Create(
            eventEntity.Id,
            tenantId,
            (int)ParticipationHandlingModeEnum.InformationOnly,
            (int)AdvanceRegistrationObligationEnum.NotApplicable,
            identityAccessModeId: null,
            guestRecoveryPolicy: null,
            DateTime.UtcNow);
        return new(eventEntity, [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);
    }

    private static AtprotoEventPublicationEntityGraph CreateMaximalGraph()
    {
        AtprotoEventPublicationEntityGraph baseline = CreateGraph();
        Explore.Domain.Event eventEntity = baseline.Event;
        Guid tenantId = eventEntity.TenantId;
        Guid actorUserId = Guid.CreateVersion7();
        DateTime now = new(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);

        eventEntity.Description = "event-description-canary";
        eventEntity.Subtitle = "event-subtitle-canary";
        eventEntity.Content = "<p>event-content-canary</p>";
        eventEntity.Slug = "event-slug-canary";
        eventEntity.PublicCode = "EVENT-CODE-CANARY";
        eventEntity.ParticipationConfiguration = EventParticipationConfiguration.Create(
            eventEntity.Id,
            tenantId,
            (int)ParticipationHandlingModeEnum.ExternalManaged,
            (int)AdvanceRegistrationObligationEnum.Required,
            identityAccessModeId: null,
            guestRecoveryPolicy: null,
            DateTime.UtcNow);
        eventEntity.TotalViews = 42;
        eventEntity.SessionCount = 2;
        eventEntity.FirstSessionDate = new(2026, 7, 19);
        eventEntity.LastSessionDate = new(2026, 7, 20);
        eventEntity.LastSessionStartUtc = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
        eventEntity.EventTimeZoneId = "Europe/Brussels";
        eventEntity.BackgroundColor = "#112233";
        eventEntity.BackgroundEffect = "background-effect-canary";
        eventEntity.EventTypeId = 1;
        eventEntity.EventType = new EventType { Id = 1, MasterCode = "CONFERENCE", FullName = "Conference canary" };
        eventEntity.AudienceGenderId = 1;
        eventEntity.AudienceGender = new AudienceGender { Id = 1, MasterCode = "ALL", FullName = "All genders canary" };
        eventEntity.AudienceAgeId = 1;
        eventEntity.AudienceAge = new AudienceAge { Id = 1, MasterCode = "ADULT", FullName = "Adults canary" };
        eventEntity.MadhabId = 1;
        eventEntity.Madhab = new Madhab { Id = 1, MasterCode = "GENERAL", FullName = "Madhab canary" };
        eventEntity.RegistrationPolicyId = 1;
        eventEntity.RegistrationPolicy = new EventRegistrationPolicy { Id = 1, MasterCode = "OPEN", FullName = "Open registration canary" };
        StorageObject eventImage = CreatePublicImage(tenantId, "event-image-canary");
        StorageObject backgroundImage = CreatePublicImage(tenantId, "background-image-canary");
        eventEntity.FeaturedImageId = eventImage.Id;
        eventEntity.FeaturedImage = eventImage;
        eventEntity.BackgroundImageId = backgroundImage.Id;
        eventEntity.BackgroundImage = backgroundImage;

        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii
            {
                FullName = "organization-canary",
                Country = "Belgium",
                City = "Brussels"
            },
            WebsiteUrl = "https://organization.example.test",
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var organizerGroup = new Group
        {
            Id = Guid.CreateVersion7(),
            FullName = "organizer-group-canary",
            Description = "organizer-group-description-canary",
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        eventEntity.Actor.OrganizationId = organization.Id;
        eventEntity.Actor.Organization = organization;
        eventEntity.Actor.GroupId = organizerGroup.Id;
        eventEntity.Actor.Group = organizerGroup;
        eventEntity.Actor.Description = "organizer-description-canary";
        eventEntity.Actor.AtprotoIdentities.Add(new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:organizer-canary"))
        {
            Id = Guid.CreateVersion7(),

            ActorId = eventEntity.Actor.Id,
            Actor = eventEntity.Actor,
            Handle = "organizer.handle.canary",
            PdsHost = "https://pds.example.test",
            IsActive = true,
            LastResolvedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        eventEntity.Actor.ProfilePictureUri = "organizer-profile-canary";
        eventEntity.Actor.BackgroundColor = "#445566";
        eventEntity.Actor.BackgroundEffect = "organizer-effect-canary";
        eventEntity.Actor.BannerColor = "#778899";
        eventEntity.IslamicAspect = new EventIslamicAspect
        {
            Id = eventEntity.Id,
            Event = eventEntity,
            ReferencePrayer = PrayerTime.Asr,
            PrayerTimeOffset = 15,
            GenderMode = GenderSegregationMode.Family,
            IncludesQuranRecitation = true,
            PrimaryLanguageId = 1,
            PrimaryLanguage = new Language { Id = 1, MasterCode = "AR", FullName = "Islamic Arabic" }
        };
        eventEntity.TechAspect = new EventTechAspect
        {
            Id = eventEntity.Id,
            Event = eventEntity,
            GithubRepoUrl = "https://github.com/example/hackathon-canary",
            HackathonTrack = "hackathon-canary",
            TechStackTags = ".NET,PostgreSQL",
            RequiresLaptop = true,
            IsCodingCompetition = true,
            MaxTeamSize = 4,
            PrizePool = 500,
            PrizeCurrencyCode = "EUR"
        };
        eventEntity.EventSeries = new Explore.Domain.EventSeries
        {
            Id = Guid.CreateVersion7(),
            Title = "series-canary",
            Description = "series-description-canary",
            Slug = "series-canary",
            ActorId = eventEntity.ActorId,
            Actor = eventEntity.Actor,
            IsPublished = true,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = eventEntity.VisibilityType,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = now
        };

        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Venue canary",
            Country = "Belgium",
            City = "Brussels",
            Timezone = "Europe/Brussels",
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        location.SetProviderAddress(
            "Canary street 1",
            "1000",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(50.85, 4.35));
        var room = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            LocationId = location.Id,
            Location = location,
            Name = "Room canary",
            Slug = "room-canary",
            Description = "room-description-canary",
            Capacity = 120,
            SortOrder = 2,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        location.Rooms.Add(room);
        EventLocation physicalPlacement = EventLocation.CreatePhysical(
            tenantId, eventEntity.Id, location.Id, actorUserId, now);
        physicalPlacement.AttachLocation(location);
        physicalPlacement.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.All,
            LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
            null,
            1,
            actorUserId,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            now.AddMinutes(1),
            needsPrivacyReview: false);
        EventLocation tbaPlacement = EventLocation.CreateToBeAnnounced(
            tenantId, eventEntity.Id, actorUserId, now);

        EventDay dayOne = CreateDay(eventEntity, new(2026, 7, 19), "day-canary", 1);
        EventDay dayTwo = CreateDay(eventEntity, new(2026, 7, 20), "second-day-canary", 2);
        EventSession sessionOne = CreateSession(eventEntity);
        sessionOne.Title = "session-one-canary";
        sessionOne.Description = "session-one-description-canary";
        sessionOne.Slug = "session-one-slug-canary";
        sessionOne.EventSessionKindId = 1;
        sessionOne.EventSessionKind = new EventSessionKind { Id = 1, MasterCode = "TALK", FullName = "Talk canary" };
        sessionOne.RegistrationModeId = 1;
        sessionOne.RegistrationMode = new RegistrationMode { Id = 1, MasterCode = "OPEN", FullName = "Open mode canary" };
        sessionOne.MaxAudienceAttendees = 150;
        sessionOne.CurrentAudienceAttendees = 75;
        sessionOne.FeaturedImage = CreatePublicImage(tenantId, "session-image-canary");
        sessionOne.StartTime = new DateTimeOffset(2026, 7, 19, 9, 0, 0, TimeSpan.Zero);
        sessionOne.EndTime = sessionOne.StartTime.Value.AddHours(1);
        sessionOne.EventDayId = dayOne.Id;
        sessionOne.EventDay = dayOne;
        sessionOne.AssignEventLocation(physicalPlacement);
        sessionOne.RoomId = room.Id;
        sessionOne.Room = room;
        sessionOne.IslamicAspect = new EventSessionIslamicAspect
        {
            EventSessionId = sessionOne.Id,
            EventSession = sessionOne,
            StartTimeType = SessionStartTimeType.RelativeToPrayer,
            ReferencePrayer = PrayerTime.Dhuhr,
            OffsetMinutes = 10,
            RequiresWudu = true,
            RitualRequirementsJson = "{\"canary\":\"ritual-canary\"}"
        };
        EventSession sessionTwo = CreateSession(eventEntity);
        sessionTwo.Title = "session-two-canary";
        sessionTwo.SortOrder = 2;
        sessionTwo.EventDayId = dayTwo.Id;
        sessionTwo.EventDay = dayTwo;
        sessionTwo.AssignEventLocation(tbaPlacement);

        var group = new EventSessionGroup
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = eventEntity,
            Name = "group-canary",
            Slug = "group-canary",
            Description = "group-description-canary",
            SortOrder = 1,
            IsPublished = true,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        group.AssignEventLocation(physicalPlacement);
        group.RoomId = room.Id;
        group.Room = room;
        EventSessionGroupSession groupOne = CreateGroupLink(eventEntity, group, sessionOne, true, 1);
        EventSessionGroupSession groupTwo = CreateGroupLink(eventEntity, group, sessionTwo, false, 2);

        var eventAgenda = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = eventEntity,
            EventDayId = dayOne.Id,
            EventDay = dayOne,
            Title = "event-agenda-canary",
            Description = "event-agenda-description-canary",
            StartTime = sessionOne.StartTime!.Value,
            EndTime = sessionOne.EndTime!.Value,
            SortOrder = 1,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        eventAgenda.AssignEventLocation(physicalPlacement);
        eventAgenda.RoomId = room.Id;
        eventAgenda.Room = room;
        var sessionAgenda = new EventSessionAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = sessionOne.Id,
            EventSession = sessionOne,
            Title = "session-agenda-canary",
            Description = "session-agenda-description-canary",
            StartTime = sessionOne.StartTime.Value,
            EndTime = sessionOne.EndTime.Value,
            TenantId = tenantId,
            Tenant = null!
        };
        sessionAgenda.AssignEventLocation(physicalPlacement);

        Category eventCategory = CreateCategory(tenantId, "event-category-canary");
        Category sessionCategory = CreateCategory(tenantId, "session-category-canary");
        Tag eventTag = CreateTag(tenantId, "event-tag-canary");
        Tag sessionTag = CreateTag(tenantId, "session-tag-canary");
        var speaker = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = 1,
            ActorType = eventEntity.Actor.ActorType,
            Pii = new ActorPii { DisplayName = "speaker-canary", ProfilePictureUri = "speaker-profile-canary" },
            Description = "speaker-description-canary",
            BackgroundColor = "speaker-color-canary",
            BackgroundEffect = "speaker-effect-canary",
            BannerColor = "speaker-banner-color-canary",
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        EventCustomPropertyDefinition eventProperty = CreateEventProperty(eventEntity, "event-property-canary");
        EventCustomPropertyOption eventOption = new()
        {
            Id = Guid.CreateVersion7(),
            EventCustomPropertyDefinitionId = eventProperty.Id,
            Definition = eventProperty,
            Namespace = "canary",
            Key = "event-option-canary",
            DisplayName = "event-option-canary",
            Description = "event-option-description-canary",
            Value = "event-option-value-canary",
            IsDefault = true,
            IsActive = true,
            SortOrder = 1,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        EventCustomPropertyValue eventValue = new()
        {
            Id = Guid.CreateVersion7(),
            EventCustomPropertyDefinitionId = eventProperty.Id,
            Definition = eventProperty,
            EventId = eventEntity.Id,
            Event = eventEntity,
            TenantId = tenantId,
            Tenant = null!,
            Ordinal = 1,
            TextValue = "event-value-canary",
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        eventProperty.AddOption(eventOption);
        eventProperty.AddValue(eventValue);
        eventProperty.AddValue(CreateEventTypedValue(eventProperty, eventEntity, 2, number: 17.25m));
        eventProperty.AddValue(CreateEventTypedValue(eventProperty, eventEntity, 3, boolean: true));
        eventProperty.AddValue(CreateEventTypedValue(eventProperty, eventEntity, 4, dateTime: new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.Zero)));
        eventProperty.AddValue(CreateEventTypedValue(eventProperty, eventEntity, 5, option: eventOption));

        EventSessionCustomPropertyDefinition sessionProperty = CreateSessionProperty(sessionOne, "session-property-canary");
        EventSessionCustomPropertyValue sessionValue = new()
        {
            Id = Guid.CreateVersion7(),
            EventSessionCustomPropertyDefinitionId = sessionProperty.Id,
            Definition = sessionProperty,
            EventSessionId = sessionOne.Id,
            EventSession = sessionOne,
            TenantId = tenantId,
            Tenant = null!,
            Ordinal = 1,
            TextValue = "session-value-canary",
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        EventSessionCustomPropertyOption sessionOption = new()
        {
            Id = Guid.CreateVersion7(),
            EventSessionCustomPropertyDefinitionId = sessionProperty.Id,
            Definition = sessionProperty,
            Namespace = "canary",
            Key = "session-option-canary",
            DisplayName = "session-option-canary",
            Description = "session-option-description-canary",
            Value = "session-option-value-canary",
            IsDefault = true,
            IsActive = true,
            SortOrder = 1,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        sessionProperty.AddOption(sessionOption);
        sessionProperty.AddValue(sessionValue);
        sessionProperty.AddValue(CreateSessionTypedValue(sessionProperty, sessionOne, 2, number: 91.75m));
        sessionProperty.AddValue(CreateSessionTypedValue(sessionProperty, sessionOne, 3, boolean: false));
        sessionProperty.AddValue(CreateSessionTypedValue(sessionProperty, sessionOne, 4, dateTime: new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero)));
        sessionProperty.AddValue(CreateSessionTypedValue(sessionProperty, sessionOne, 5, option: sessionOption));

        return new(
            eventEntity,
            [physicalPlacement, tbaPlacement],
            [sessionOne, sessionTwo],
            [dayOne, dayTwo],
            [group],
            [groupOne, groupTwo],
            [eventAgenda],
            [sessionAgenda],
            [new Explore.Domain.EventCategories { Id = Guid.CreateVersion7(), EventId = eventEntity.Id, Event = eventEntity, CategoryId = eventCategory.Id, Category = eventCategory, TenantId = tenantId, Tenant = null!, CreatedAt = now, ConcurrencyStamp = Guid.CreateVersion7() }],
            [new Explore.Domain.EventTags { Id = Guid.CreateVersion7(), EventId = eventEntity.Id, Event = eventEntity, TagId = eventTag.Id, Tag = eventTag, TenantId = tenantId, Tenant = null!, CreatedAt = now, ConcurrencyStamp = Guid.CreateVersion7() }],
            [new EventSessionCategory { Id = Guid.CreateVersion7(), EventSessionId = sessionOne.Id, EventSession = sessionOne, CategoryId = sessionCategory.Id, Category = sessionCategory, TenantId = tenantId, Tenant = null!, CreatedAt = now }],
            [new EventSessionTag { Id = Guid.CreateVersion7(), EventSessionId = sessionOne.Id, EventSession = sessionOne, TagId = sessionTag.Id, Tag = sessionTag, TenantId = tenantId, Tenant = null!, CreatedAt = now }],
            [new EventSessionLanguage { Id = 1, EventSessionId = sessionOne.Id, EventSession = sessionOne, LanguageId = 2, Language = new Language { Id = 2, MasterCode = "EN", FullName = "language-canary" }, TenantId = tenantId, Tenant = null!, ConcurrencyStamp = Guid.CreateVersion7() }],
            [new EventSessionSpeaker { Id = Guid.CreateVersion7(), EventSessionId = sessionOne.Id, EventSession = sessionOne, ActorId = speaker.Id, Actor = speaker, TenantId = tenantId, Tenant = null!, ConcurrencyStamp = Guid.CreateVersion7() }],
            [eventProperty],
            [sessionProperty]);
    }

    private static EventDay CreateDay(Explore.Domain.Event eventEntity, DateOnly date, string label, int order)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = eventEntity,
            LocalDate = date,
            Label = label,
            Description = $"{label}-description",
            BannerText = $"{label}-banner",
            IsPublished = true,
            SortOrder = order,
            AllowsDayScopeRegistration = true,
            TenantId = eventEntity.TenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static EventSessionGroupSession CreateGroupLink(
        Explore.Domain.Event eventEntity, EventSessionGroup group, EventSession session, bool primary, int order)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventSessionGroupId = group.Id,
            EventSessionGroup = group,
            EventSessionId = session.Id,
            EventSession = session,
            EventId = eventEntity.Id,
            Event = eventEntity,
            IsPrimary = primary,
            SortOrder = order,
            TenantId = eventEntity.TenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow
        };

    private static Category CreateCategory(Guid tenantId, string name)
        => new() { Id = Guid.CreateVersion7(), MasterCode = name.ToUpperInvariant(), FullName = name, TenantId = tenantId, Tenant = null!, ConcurrencyStamp = Guid.CreateVersion7() };

    private static Tag CreateTag(Guid tenantId, string name)
        => new() { Id = Guid.CreateVersion7(), MasterCode = name.ToUpperInvariant(), FullName = name, TenantId = tenantId, Tenant = null! };

    private static EventCustomPropertyDefinition CreateEventProperty(Explore.Domain.Event eventEntity, string name)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = eventEntity,
            TenantId = eventEntity.TenantId,
            Tenant = null!,
            Namespace = "canary",
            Key = name,
            DisplayName = name,
            Description = $"{name}-description",
            PropertyType = PropertyType.Text,
            IsRequired = true,
            IsMulti = true,
            IsActive = true,
            SortOrder = 1,
            ExposureLevel = ExposureLevel.Public,
            IsSearchable = true,
            IsFilterable = true,
            IsExportable = true,
            IsModerationRelevant = true,
            IsAnalyticsRelevant = true,
            DefaultTextValue = "event-default-canary",
            MinLength = 1,
            MaxLength = 100,
            RegexPattern = "canary.*",
            AllowedUrlSchemes = "https",
            InstantiatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static EventSessionCustomPropertyDefinition CreateSessionProperty(EventSession session, string name)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = session.Id,
            EventSession = session,
            TenantId = session.TenantId,
            Tenant = null!,
            Namespace = "canary",
            Key = name,
            DisplayName = name,
            Description = $"{name}-description",
            PropertyType = PropertyType.Text,
            IsRequired = true,
            IsMulti = true,
            IsActive = true,
            SortOrder = 1,
            ExposureLevel = ExposureLevel.Public,
            IsSearchable = true,
            IsFilterable = true,
            IsExportable = true,
            IsAnalyticsRelevant = true,
            DefaultTextValue = "session-default-canary",
            MinLength = 1,
            MaxLength = 100,
            InstantiatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static EventCustomPropertyValue CreateEventTypedValue(
        EventCustomPropertyDefinition definition,
        Explore.Domain.Event eventEntity,
        int ordinal,
        decimal? number = null,
        bool? boolean = null,
        DateTimeOffset? dateTime = null,
        EventCustomPropertyOption? option = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventCustomPropertyDefinitionId = definition.Id,
            Definition = definition,
            EventId = eventEntity.Id,
            Event = eventEntity,
            TenantId = eventEntity.TenantId,
            Tenant = null!,
            Ordinal = ordinal,
            NumberValue = number,
            BooleanValue = boolean,
            DateTimeValue = dateTime,
            OptionId = option?.Id,
            Option = option,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static EventSessionCustomPropertyValue CreateSessionTypedValue(
        EventSessionCustomPropertyDefinition definition,
        EventSession session,
        int ordinal,
        decimal? number = null,
        bool? boolean = null,
        DateTimeOffset? dateTime = null,
        EventSessionCustomPropertyOption? option = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventSessionCustomPropertyDefinitionId = definition.Id,
            Definition = definition,
            EventSessionId = session.Id,
            EventSession = session,
            TenantId = session.TenantId,
            Tenant = null!,
            Ordinal = ordinal,
            NumberValue = number,
            BooleanValue = boolean,
            DateTimeValue = dateTime,
            OptionId = option?.Id,
            Option = option,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static StorageObject CreatePublicImage(Guid tenantId, string name)
        => new()
        {
            Id = Guid.CreateVersion7(),
            FileTypeId = 1,
            FileType = new FileType { Id = 1, MasterCode = "IMAGE", FullName = "Image" },
            Uri = $"https://cdn.example.test/{name}.png",
            Provider = "test-provider-private-canary",
            FullName = $"{name}.png",
            SafeDisplayName = name,
            Extension = ".png",
            ContentType = "image/png",
            Size = 1234,
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static EventSession CreateSession(Explore.Domain.Event eventEntity)
        => new(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = eventEntity,
            TenantId = eventEntity.TenantId,
            Tenant = null!,
            EventSessionStatus = new EventSessionStatus
            {
                Id = (int)EventSessionStatusEnum.Published,
                MasterCode = "PUBLISHED",
                FullName = "Published"
            },
            Title = "Session",
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
}
