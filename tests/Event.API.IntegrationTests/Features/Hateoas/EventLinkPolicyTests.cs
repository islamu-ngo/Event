// ABOUTME: Unit-level HATEOAS policy tests for event detail affordance metadata.
// ABOUTME: Guards event mutation authorization context and aspect lifecycle affordances.

namespace Event.Api.IntegrationTests.Features.Hateoas;

using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.RegistrationAnalytics;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TUnit.Core;

public sealed class EventLinkPolicyTests
{
    [Test]
    public async Task ManagedCollectionItem_UsesManagementRoutesAndOmitsPublicReports()
    {
        var dto = new EventListDto
        {
            Id = Guid.NewGuid(),
            Title = "Managed event",
            EventTypeFullName = "Conference",
            AudienceGenderFullName = "All",
            AudienceAgeFullName = "All",
            ActorId = Guid.NewGuid(),
            ActorDisplayName = "Organizer",
            ActorTypeFullName = "Organization",
            EventStatusId = (int)EventStatusEnum.Published,
            EventStatusFullName = "Published",
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityTypeFullName = "Public",
            EventFormatFullName = "In person",
            SessionCount = 1,
            IsManagementView = true
        };
        var links = new EventCollectionLinkPolicy()
            .GetItemLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Single(link => link.Rel == LinkRelations.Self).RouteName)
            .IsEqualTo(RouteNames.GetEventManagementDetails);
        await Assert.That(links.Single(link => link.Rel == "sessions").RouteName)
            .IsEqualTo(RouteNames.GetManagedEventSessionsByEvent);
        await Assert.That(links.Any(link => link.Rel == LinkRelations.EventReportOptions)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.ReportEvent)).IsFalse();
    }

    [Test]
    public async Task AspectLinks_TransitionFromCreateToEditWhenAspectExists()
    {
        var dto = CreateEventDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var policy = new EventDetailLinkPolicy();

        var createLinks = policy.GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test"))).ToList();

        var createIslamic = createLinks.Single(link => link.Rel == "islamic-aspect:create");
        var createTech = createLinks.Single(link => link.Rel == "tech-aspect:create");
        await Assert.That(createIslamic.RouteName).IsEqualTo(RouteNames.CreateEventIslamicAspect);
        await Assert.That(createIslamic.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(createTech.RouteName).IsEqualTo(RouteNames.CreateEventTechAspect);
        await Assert.That(createTech.Method).IsEqualTo(HttpMethods.Post);

        dto = dto with { AvailableAspects = ["Islamic", "Tech"] };
        var editLinks = policy.GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test"))).ToList();

        var editIslamic = editLinks.Single(link => link.Rel == "islamic-aspect:edit");
        var editTech = editLinks.Single(link => link.Rel == "tech-aspect:edit");
        await Assert.That(editIslamic.RouteName).IsEqualTo(RouteNames.UpdateEventIslamicAspect);
        await Assert.That(editIslamic.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(editTech.RouteName).IsEqualTo(RouteNames.UpdateEventTechAspect);
        await Assert.That(editTech.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(editLinks.Any(link => link.Rel.EndsWith(":create", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task AddSessionLinks_UseEventSessionPreCreateAuthorizationContext()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var dto = CreateEventDto(eventId, tenantId, Guid.NewGuid());

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        foreach (var rel in new[] { LinkRelations.AddSession, LinkRelations.SessionCreateContext })
        {
            var link = links.Single(definition => definition.Rel == rel);

            await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.EventSession);
            await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Create);
            await Assert.That(link.PermissionResourceId).IsEqualTo(eventId.ToString());
            await Assert.That(link.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString());
            // No session row exists yet, so the parent event is the only authority the create rules can weigh.
            await Assert.That(link.PermissionFacts)
                .IsEqualTo(new PreCreateAuthorizationFacts(tenantId, eventId));
        }
    }

    [Test]
    public async Task ManagedPlatformEvent_WithVerifiedOrganizationOrganizer_AdvertisesAuditedAttendeeExport()
    {
        Guid eventId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid organizerActorId = Guid.NewGuid();
        Guid organizerOrganizationId = Guid.NewGuid();
        EventDto dto = CreateEventDto(eventId, tenantId, organizerOrganizationId, organizerActorId: organizerActorId);
        dto = dto with { OrganizerActorOrganizationId = organizerOrganizationId };
        dto.IsManagementView = true;
        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged
        };

        LinkDefinition export = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(link => link.Rel == LinkRelations.ExportAttendees);

        await Assert.That(export.RouteName).IsEqualTo(RouteNames.ExportOrganizationSharedContacts);
        await Assert.That(export.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(export.PermissionResourceKind).IsEqualTo(ResourceKinds.EventContactShareConsent);
        await Assert.That(export.PermissionAction).IsEqualTo(AuthorizationActions.ExportSharedContacts);
        await Assert.That(export.PermissionResourceId).IsEqualTo(organizerOrganizationId.ToString());
        await Assert.That(export.PermissionFacts)
            .IsEqualTo(new ContactShareAuthorizationFacts(tenantId, organizerOrganizationId));
    }

    [Test]
    public async Task ManagedPlatformEvent_AdvertisesRegistrationAnalyticsWithManageRegistrationAuthorization()
    {
        Guid eventId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        EventDto dto = CreateEventDto(eventId, tenantId, Guid.NewGuid());
        dto.IsManagementView = true;
        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged
        };

        LinkDefinition analytics = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(link => link.Rel == LinkRelations.ViewRegistrationAnalytics);
        var routeValues = new RouteValueDictionary(analytics.RouteValues);

        await Assert.That(analytics.RouteName).IsEqualTo(RouteNames.GetRegistrationAnswerAnalytics);
        await Assert.That(analytics.Method).IsEqualTo(HttpMethods.Get);
        await Assert.That(routeValues["eventId"]).IsEqualTo(eventId);
        await Assert.That(analytics.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrations);
        await Assert.That(analytics.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(analytics.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString());
    }

    [Test]
    public async Task RegistrationAnalyticsResource_UsesExactScopedSelfLink()
    {
        Guid tenantId = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        Guid formId = Guid.NewGuid();
        Guid versionId = Guid.NewGuid();
        var dto = new RegistrationAnswerAnalyticsDto(tenantId, eventId, formId, versionId, 3, []);

        LinkDefinition self = new RegistrationAnswerAnalyticsLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(link => link.Rel == LinkRelations.Self);
        var routeValues = new RouteValueDictionary(self.RouteValues);

        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetRegistrationAnswerAnalytics);
        await Assert.That(self.Method).IsEqualTo(HttpMethods.Get);
        await Assert.That(routeValues["eventId"]).IsEqualTo(eventId);
        await Assert.That(routeValues["formId"]).IsEqualTo(formId);
        await Assert.That(routeValues["formVersionId"]).IsEqualTo(versionId);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrations);
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(self.PermissionResourceId).IsEqualTo(eventId.ToString("D"));
        // Analytics reads authorize against the parent event. The form and version select which answers are
        // summarized and travel in the route, not in the policy facts.
        await Assert.That(self.PermissionFacts)
            .IsEqualTo(new EventScopedAuthorizationFacts(tenantId, eventId));

        // Regression guard: this link previously published Guid.Empty as its tenant. The wire projection drops
        // unset Guids, so the evaluator received no tenantId, could not resolve an event context, and denied the
        // link unconditionally — the affordance never rendered for anyone. Assert a real tenant, not just any value.
        var facts = (EventScopedAuthorizationFacts)self.PermissionFacts!;
        await Assert.That(facts.TenantId).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task DraftEventLifecycleLinks_UseEventAuthorizationContext()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var dto = CreateEventDto(eventId, tenantId, organizationId);

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        foreach (var rel in new[] { LinkRelations.Publish, LinkRelations.Cancel, LinkRelations.Archive })
        {
            var link = links.Single(definition => definition.Rel == rel);

            await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
            await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Update);
            await Assert.That(link.PermissionResourceId).IsEqualTo(eventId.ToString());
            await Assert.That(link.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString());
            // Descriptors that publish typed facts intentionally omit the stringly-typed attribute bag;
            // the HAL evaluator reads facts in preference to attributes, so that is what is asserted.
            var linkFacts = link.PermissionFacts as EventAuthorizationFacts;
            await Assert.That(linkFacts).IsNotNull();
            await Assert.That(linkFacts!.EventId).IsEqualTo(eventId);
            await Assert.That(linkFacts.TenantId).IsEqualTo(tenantId);
            await Assert.That(linkFacts.OrganizationId).IsEqualTo(organizationId);
        }
    }

    [Test]
    public async Task DraftUserOwnedEventLifecycleLinks_IncludeUserOwnerAuthorizationContext()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dto = CreateEventDto(eventId, tenantId, organizationId: null, userId);

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var publish = links.Single(definition => definition.Rel == LinkRelations.Publish);

        // Descriptors that publish typed facts intentionally omit the stringly-typed attribute bag;
        // the HAL evaluator reads facts in preference to attributes, so that is what is asserted.
        var publishFacts = publish.PermissionFacts as EventAuthorizationFacts;
        await Assert.That(publishFacts).IsNotNull();
        await Assert.That(publishFacts!.EventId).IsEqualTo(eventId);
        await Assert.That(publishFacts.TenantId).IsEqualTo(tenantId);
        await Assert.That(publishFacts.UserId).IsEqualTo(userId);
        await Assert.That(publishFacts.OrganizationId).IsNull();
    }

    [Test]
    public async Task LightModerationLink_UsesLightModerationAuthorizationAction()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var dto = CreateEventDto(
            eventId,
            tenantId,
            organizationId,
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var moderate = links.Single(definition => definition.Rel == LinkRelations.ModerateLight);

        await Assert.That(moderate.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(moderate.RouteName).IsEqualTo(RouteNames.ModerateEventLight);
        await Assert.That(moderate.PermissionAction).IsEqualTo(AuthorizationActions.Events.ModerateLight);
        await Assert.That(moderate.PermissionResourceId).IsEqualTo(eventId.ToString());
        // Descriptors that publish typed facts intentionally omit the stringly-typed attribute bag;
        // the HAL evaluator reads facts in preference to attributes, so that is what is asserted.
        var moderateFacts = moderate.PermissionFacts as EventAuthorizationFacts;
        await Assert.That(moderateFacts).IsNotNull();
        await Assert.That(moderateFacts!.EventId).IsEqualTo(eventId);
        await Assert.That(moderateFacts.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task PublishedEvent_AdvertisesHeavyModerationAuthorizationAction()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var dto = CreateEventDto(
            eventId,
            tenantId,
            organizationId,
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var moderate = links.Single(definition => definition.Rel == LinkRelations.ModerateHeavy);

        await Assert.That(moderate.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(moderate.RouteName).IsEqualTo(RouteNames.ModerateEventHeavy);
        await Assert.That(moderate.PermissionAction).IsEqualTo(AuthorizationActions.Events.ModerateHeavy);
        await Assert.That(moderate.PermissionResourceId).IsEqualTo(eventId.ToString());
        // Descriptors that publish typed facts intentionally omit the stringly-typed attribute bag;
        // the HAL evaluator reads facts in preference to attributes, so that is what is asserted.
        var moderateFacts = moderate.PermissionFacts as EventAuthorizationFacts;
        await Assert.That(moderateFacts).IsNotNull();
        await Assert.That(moderateFacts!.EventId).IsEqualTo(eventId);
        await Assert.That(moderateFacts.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task DraftEvent_DoesNotAdvertiseLightOrUnmoderate()
    {
        var dto = CreateEventDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.ModerateLight)).IsFalse();
        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.Unmoderate)).IsFalse();
    }

    [Test]
    public async Task ModeratedEligibleEvent_AdvertisesUnmoderateAuthorizationAction()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var dto = CreateEventDto(
            eventId,
            tenantId,
            organizationId,
            status: EventStatusEnum.Moderated,
            statusName: "Moderated",
            statusCode: "MODERATED");
        dto.IsUnmoderationEligible = true;

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var unmoderate = links.Single(definition => definition.Rel == LinkRelations.Unmoderate);

        await Assert.That(unmoderate.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(unmoderate.RouteName).IsEqualTo(RouteNames.UnmoderateEvent);
        await Assert.That(unmoderate.PermissionAction).IsEqualTo(AuthorizationActions.Events.Unmoderate);
        await Assert.That(unmoderate.PermissionResourceId).IsEqualTo(eventId.ToString());
        // Descriptors that publish typed facts intentionally omit the stringly-typed attribute bag;
        // the HAL evaluator reads facts in preference to attributes, so that is what is asserted.
        var unmoderateFacts = unmoderate.PermissionFacts as EventAuthorizationFacts;
        await Assert.That(unmoderateFacts).IsNotNull();
        await Assert.That(unmoderateFacts!.EventId).IsEqualTo(eventId);
        await Assert.That(unmoderateFacts.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task ModeratedIneligibleEvent_DoesNotAdvertiseUnmoderate()
    {
        var dto = CreateEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: EventStatusEnum.Moderated,
            statusName: "Moderated",
            statusCode: "MODERATED");
        dto.IsUnmoderationEligible = false;

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.Unmoderate)).IsFalse();
    }

    [Test]
    public async Task IrreversiblyModeratedEvent_DoesNotAdvertiseModerationActions()
    {
        var dto = CreateEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: EventStatusEnum.Moderated,
            statusName: "Moderated",
            statusCode: "MODERATED");
        dto.IsUnmoderationEligible = false;

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.ModerateLight)).IsFalse();
        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.ModerateHeavy)).IsFalse();
        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.Unmoderate)).IsFalse();
    }

    [Test]
    public async Task PublishedEventLifecycleLinks_ExposeCancelButNotDraftActions()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var dto = CreateEventDto(
            eventId,
            tenantId,
            organizationId,
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.Cancel)).IsTrue();
        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.Publish)).IsFalse();
        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.Archive)).IsFalse();
    }

    [Test]
    public async Task PublishedEvent_AdvertisesReporterFacingReportAffordances()
    {
        var eventId = Guid.NewGuid();
        var dto = CreateEventDto(
            eventId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED") with
        {
            IsReportingIntakeEnabled = true
        };

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var options = links.Single(definition => definition.Rel == LinkRelations.EventReportOptions);
        await Assert.That(options.RouteName).IsEqualTo(RouteNames.GetEventReportOptions);
        await Assert.That(new RouteValueDictionary(options.RouteValues)["eventId"]).IsEqualTo(eventId);
        await Assert.That(options.RequiresAuth).IsFalse();

        var submit = links.Single(definition => definition.Rel == LinkRelations.ReportEvent);
        await Assert.That(submit.RouteName).IsEqualTo(RouteNames.SubmitEventReport);
        await Assert.That(submit.Method).IsEqualTo("POST");
        await Assert.That(submit.RequiresAuth).IsTrue();
        await Assert.That(submit.AdvertiseWhenAnonymous).IsTrue();

        var claim = links.Single(definition => definition.Rel == LinkRelations.ClaimEvent);
        await Assert.That(claim.PermissionResourceKind).IsEqualTo(ResourceKinds.EventOrganizerClaim);
        await Assert.That(claim.PermissionResourceId).IsEqualTo(eventId.ToString());
        var claimFacts = claim.PermissionFacts as EventAuthorizationFacts;

        await Assert.That(claimFacts).IsNotNull();

        await Assert.That(claimFacts!.EventId).IsEqualTo(eventId);

        await Assert.That(claimFacts.TenantId).IsEqualTo(dto.TenantId);

        var claims = links.Single(definition => definition.Rel == LinkRelations.OrganizerClaims);
        await Assert.That(claims.PermissionResourceKind).IsEqualTo(ResourceKinds.EventOrganizerClaim);
    }

    [Test]
    public async Task DraftEvent_DoesNotAdvertiseReportAffordances()
    {
        var dto = CreateEventDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.EventReportOptions)).IsFalse();
        await Assert.That(links.Any(definition => definition.Rel == LinkRelations.ReportEvent)).IsFalse();
    }

    [Test]
    public async Task PublishedNonPublicEvent_DoesNotAdvertisePublicActionOrClaimAffordances()
    {
        var dto = CreateEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");
        dto = dto with
        {
            VisibilityTypeId = (int)VisibilityTypeEnum.Unlisted,
            VisibilityTypeFullName = "Unlisted",
            VisibilityTypeMasterCode = "UNLISTED"
        };

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        foreach (var relation in new[]
        {
            LinkRelations.PublicActions,
            LinkRelations.ClaimEvent,
            LinkRelations.SuggestCorrection,
            LinkRelations.ReportExternalLink
        })
        {
            await Assert.That(links.Any(definition => definition.Rel == relation)).IsFalse();
        }
    }

    [Test]
    public async Task IneligibleManagementEvent_UsesManagementSelfAndOmitsPublicAffordances()
    {
        var dto = CreateEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: EventStatusEnum.Moderated,
            statusName: "Moderated",
            statusCode: "MODERATED");
        dto.IsPubliclyEligible = false;
        dto.IsManagementView = true;
        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged
        };

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetEventManagementDetails);

        foreach (var relation in new[]
        {
            LinkRelations.Collection,
            LinkRelations.Sessions,
            LinkRelations.Program,
            LinkRelations.ProgramSummary,
            LinkRelations.SessionGroups,
            LinkRelations.PublicActions,
            LinkRelations.EventReportOptions,
            LinkRelations.ReportEvent,
            LinkRelations.SuggestCorrection,
            LinkRelations.ReportExternalLink,
            LinkRelations.ClaimEvent,
            LinkRelations.OrganizerClaims,
            LinkRelations.StartRegistration,
            LinkRelations.SignInToRegister,
            LinkRelations.ExternalRegistration,
            "actor",
            "organizer-subscription",
            "subscribe-organizer"
        })
        {
            await Assert.That(links.Any(link => link.Rel == relation)).IsFalse();
        }

        foreach (var relation in new[]
        {
            LinkRelations.Edit,
            LinkRelations.ModerationHistory,
            LinkRelations.ModerationReports,
            LinkRelations.ManagePublicActions,
            LinkRelations.ConfigureParticipation,
            "delete"
        })
        {
            await Assert.That(links.Any(link => link.Rel == relation)).IsTrue();
        }
    }

    [Test]
    public async Task ParticipationConfigurationLinks_UseModeSpecificAffordances()
    {
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var organizerActorId = Guid.NewGuid();
        var organizerUserId = Guid.NewGuid();
        var baseDto = CreateEventDto(
            eventId,
            tenantId,
            Guid.NewGuid(),
            provenanceTypeId: (int)EventProvenanceTypeEnum.OrganizerCreated,
            provenanceTypeCode: "ORGANIZER_CREATED",
            organizerActorId: organizerActorId,
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");
        baseDto = baseDto with { OrganizerActorUserId = organizerUserId };

        baseDto.ParticipationConfiguration = new EventParticipationConfigurationDto { ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly };
        var configuredLinks = new EventDetailLinkPolicy()
            .GetLinks(baseDto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .ToList();

        var configure = configuredLinks.Single(link => link.Rel == LinkRelations.ConfigureParticipation);
        await Assert.That(configure.RouteName).IsEqualTo(RouteNames.ConfigureEventParticipation);
        await Assert.That(configure.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(configure.RequiresAuth).IsTrue();
        await Assert.That(configure.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrations);
        await Assert.That(configure.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        var configureFacts = configure.PermissionFacts as EventAuthorizationFacts;
        await Assert.That(configureFacts).IsNotNull();
        await Assert.That(configureFacts!.ActorId).IsEqualTo(baseDto.ActorId);
        await Assert.That(configureFacts.OrganizerActorId).IsEqualTo(organizerActorId);
        await Assert.That(new RouteValueDictionary(configure.RouteValues)["eventId"]).IsEqualTo(eventId);

        foreach (var mode in new[] { ParticipationHandlingModeEnum.InformationOnly, ParticipationHandlingModeEnum.WalkIn })
        {
            baseDto.ParticipationConfiguration = new EventParticipationConfigurationDto { ParticipationHandlingModeId = (int)mode };
            baseDto = baseDto with { PublicActions = [] };

            var links = new EventDetailLinkPolicy()
                .GetLinks(baseDto, new ClaimsPrincipal(new ClaimsIdentity("test")))
                .ToList();

            await Assert.That(links.Any(link => link.Rel is LinkRelations.ExternalRegistration or LinkRelations.StartRegistration)).IsFalse();
        }

        baseDto.ParticipationConfiguration = new EventParticipationConfigurationDto { ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.ExternalManaged };
        baseDto = baseDto with
        {
            PublicActions =
            [
            new EventPublicActionDto
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                EventId = eventId,
                KindId = (int)EventPublicActionKindEnum.ExternalRegistration,
                Url = "https://example.com/secondary",
                DestinationDomain = "example.com",
                SortOrder = 20,
                IsPrimary = false
            },
            new EventPublicActionDto
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                EventId = eventId,
                KindId = (int)EventPublicActionKindEnum.ExternalRegistration,
                Url = "https://example.com/primary",
                DestinationDomain = "example.com",
                SortOrder = 10,
                IsPrimary = true
            },
            new EventPublicActionDto
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                EventId = eventId,
                KindId = (int)EventPublicActionKindEnum.Livestream,
                Url = "https://example.com/live",
                DestinationDomain = "example.com",
                SortOrder = 1,
                IsPrimary = true
            }
            ]
        };

        var externalLinks = new EventDetailLinkPolicy()
            .GetLinks(baseDto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Where(link => link.Rel == LinkRelations.ExternalRegistration)
            .ToList();

        await Assert.That(externalLinks.Count).IsEqualTo(1);
        var external = externalLinks.Single();
        await Assert.That(external.RouteName).IsEqualTo(RouteNames.RedirectEventPublicAction);
        await Assert.That(external.Method).IsEqualTo(HttpMethods.Get);
        await Assert.That(external.Title).IsEqualTo("Register on organizer website");
        var externalRouteValues = new RouteValueDictionary(external.RouteValues);
        await Assert.That(externalRouteValues["eventId"]).IsEqualTo(eventId);
        await Assert.That(externalRouteValues["actionId"]).IsEqualTo(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        await Assert.That(externalRouteValues["surface"]).IsEqualTo("event_detail");

        baseDto = baseDto with
        {
            ProvenanceTypeId = (int)EventProvenanceTypeEnum.Imported,
            ProvenanceTypeCode = "IMPORTED",
            OrganizerActorId = null
        };

        var importedLinks = new EventDetailLinkPolicy()
            .GetLinks(baseDto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Where(link => link.Rel == LinkRelations.ExternalRegistration)
            .ToList();

        await Assert.That(importedLinks.Single().Title).IsEqualTo("View original event page");

        baseDto = baseDto with { OrganizerActorId = organizerActorId };
        var verifiedImportedLink = new EventDetailLinkPolicy()
            .GetLinks(baseDto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(link => link.Rel == LinkRelations.ExternalRegistration);

        await Assert.That(verifiedImportedLink.Title).IsEqualTo("Register on organizer website");

        baseDto.ParticipationConfiguration = new EventParticipationConfigurationDto { ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged };
        baseDto = baseDto with { PublicActions = [] };

        var platformLinks = new EventDetailLinkPolicy()
            .GetLinks(baseDto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Where(link => link.Rel is LinkRelations.StartRegistration or LinkRelations.SignInToRegister)
            .ToList();

        await Assert.That(platformLinks.Count).IsEqualTo(1);
        var platform = platformLinks.Single();
        await Assert.That(platform.Rel).IsEqualTo(LinkRelations.StartRegistration);
        await Assert.That(platform.RouteName).IsEqualTo(RouteNames.StartAuthenticatedRegistrationOrder);
        await Assert.That(platform.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(platform.RequiresAuth).IsTrue();
        await Assert.That(platform.PermissionAction).IsEqualTo(AuthorizationActions.Create);
        await Assert.That(platform.PermissionResourceKind).IsEqualTo(ResourceKinds.RegistrationOrder);
        await Assert.That(platform.PermissionResourceId).IsEqualTo(eventId.ToString());
        await Assert.That(platform.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString());
        await Assert.That(platform.PermissionFacts).IsTypeOf<EventAuthorizationFacts>();
        var facts = (EventAuthorizationFacts)platform.PermissionFacts!;
        await Assert.That(facts.EventId).IsEqualTo(eventId);
        await Assert.That(facts.TenantId).IsEqualTo(tenantId);
        await Assert.That(facts.OrganizerUserId).IsEqualTo(organizerUserId);
        await Assert.That(platformLinks.Any(link => link.Rel == LinkRelations.SignInToRegister)).IsFalse();
    }

    [Test]
    public async Task PlatformManagedRegistrationStart_UsesTypedEventFacts()
    {
        Guid eventId = Guid.NewGuid();
        Guid tenantId = Guid.NewGuid();
        Guid organizerUserId = Guid.NewGuid();
        var dto = CreateEventDto(
            eventId,
            tenantId,
            Guid.NewGuid(),
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");
        dto = dto with { OrganizerActorUserId = organizerUserId };
        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged
        };

        LinkDefinition link = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(candidate => candidate.Rel == LinkRelations.StartRegistration);

        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.RegistrationOrder);
        await Assert.That(link.PermissionFacts).IsTypeOf<EventAuthorizationFacts>();
        var facts = (EventAuthorizationFacts)link.PermissionFacts!;
        await Assert.That(facts.EventId).IsEqualTo(eventId);
        await Assert.That(facts.TenantId).IsEqualTo(tenantId);
        await Assert.That(facts.OrganizerUserId).IsEqualTo(organizerUserId);
    }

    [Test]
    [Category(TestCategories.Phase43Ticketing)]
    public async Task TicketManagementLinks_ArePlatformManagedAndSeparate()
    {
        var dto = CreateEventDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var policy = new EventDetailLinkPolicy();

        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.ExternalManaged
        };
        dto.IsManagementView = true;
        var externalLinks = policy.GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test"))).ToList();
        await Assert.That(externalLinks.Any(link => link.Rel == LinkRelations.ManageTicketTypes || link.Rel == LinkRelations.ManageCapacityPools)).IsFalse();
        await Assert.That(externalLinks.Any(link => link.Rel == LinkRelations.ViewRegistrationOrders)).IsFalse();

        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly
        };
        var listingOnlyLinks = policy.GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test"))).ToList();
        await Assert.That(listingOnlyLinks.Any(link => link.Rel == LinkRelations.ManageTicketTypes || link.Rel == LinkRelations.ManageCapacityPools)).IsFalse();

        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged
        };
        var platformLinks = policy.GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test"))).ToList();
        foreach (var rel in new[] { LinkRelations.ManageTicketTypes, LinkRelations.ManageCapacityPools })
        {
            var link = platformLinks.Single(link => link.Rel == rel);
            await Assert.That(link.RouteName).IsEqualTo(RouteNames.GetEventTicketCatalogManagement);
            await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageTickets);
            await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
            await Assert.That(link.PermissionResourceId).IsEqualTo(dto.Id.ToString());
            await Assert.That(link.PermissionScope?.TenantId).IsEqualTo(dto.TenantId.ToString());
            var linkFacts = link.PermissionFacts as EventAuthorizationFacts;

            await Assert.That(linkFacts).IsNotNull();

            await Assert.That(linkFacts!.EventId).IsEqualTo(dto.Id);

            await Assert.That(linkFacts.TenantId).IsEqualTo(dto.TenantId);
        }

        var orders = platformLinks.Single(link => link.Rel == LinkRelations.ViewRegistrationOrders);
        await Assert.That(orders.RouteName).IsEqualTo(RouteNames.GetEventRegistrationOrders);
        await Assert.That(new RouteValueDictionary(orders.RouteValues)["eventId"]).IsEqualTo(dto.Id);
        await Assert.That(orders.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrations);
        await Assert.That(orders.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
        var participants = platformLinks.Single(link => link.Rel == LinkRelations.ViewParticipants);
        await Assert.That(participants.RouteName).IsEqualTo(RouteNames.GetEventRegistrationOrders);
        await Assert.That(participants.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrations);
    }

    [Test]
    public async Task RegistrationOrderLink_UsesTheEventScopedOrganizerCollectionRoute()
    {
        var dto = CreateEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");
        dto.IsManagementView = true;
        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged
        };

        var link = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
            .Single(definition => definition.Rel == LinkRelations.ViewRegistrationOrders);

        await Assert.That(link.RouteName).IsEqualTo("GetEventRegistrationOrders");
        var routeValues = new RouteValueDictionary(link.RouteValues);
        await Assert.That(routeValues["eventId"]).IsEqualTo(dto.Id);
        await Assert.That(routeValues.ContainsKey("actorId")).IsFalse();
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Events.ManageRegistrations);
    }

    [Test]
    public async Task PlatformManagedParticipation_AnonymousUsesSignInDiscoveryOnProtectedRegistrationRoute()
    {
        var eventId = Guid.NewGuid();
        var dto = CreateEventDto(
            eventId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");
        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged
        };

        var participationLinks = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity()))
            .Where(link => link.Rel is LinkRelations.StartRegistration or LinkRelations.SignInToRegister)
            .ToList();

        var signIn = participationLinks.Single();
        await Assert.That(signIn.Rel).IsEqualTo(LinkRelations.SignInToRegister);
        await Assert.That(signIn.RouteName).IsEqualTo(RouteNames.StartAuthenticatedRegistrationOrder);
        await Assert.That(signIn.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(signIn.RequiresAuth).IsTrue();
        await Assert.That(signIn.AdvertiseWhenAnonymous).IsTrue();
        await Assert.That(signIn.PermissionResourceKind).IsNull();
        await Assert.That(participationLinks.Any(link => link.Rel == LinkRelations.StartRegistration)).IsFalse();
    }

    [Test]
    public async Task PlatformManagedGuestMode_UsesGuestOrderRouteWithoutAnAccountCapability()
    {
        var dto = CreateEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: EventStatusEnum.Published,
            statusName: "Published",
            statusCode: "PUBLISHED");
        dto.ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged,
            IdentityAccessModeId = (int)IdentityAccessModeEnum.GuestAllowed
        };

        var links = new EventDetailLinkPolicy()
            .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity()))
            .ToList();

        var guestStart = links.Single(link => link.Rel == LinkRelations.StartGuestRegistration);
        await Assert.That(guestStart.RouteName).IsEqualTo(RouteNames.StartGuestRegistrationOrder);
        await Assert.That(guestStart.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(guestStart.RequiresAuth).IsFalse();
        await Assert.That(guestStart.PermissionResourceKind).IsNull();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.SignInToRegister)).IsFalse();
    }

    [Test]
    [Arguments(EventStatusEnum.Draft, VisibilityTypeEnum.Public)]
    [Arguments(EventStatusEnum.Published, VisibilityTypeEnum.Private)]
    [Arguments(EventStatusEnum.Published, VisibilityTypeEnum.Unlisted)]
    [Arguments(EventStatusEnum.Moderated, VisibilityTypeEnum.Public)]
    public async Task NonPublicParticipationState_DoesNotAdvertiseParticipationActions(
        EventStatusEnum status,
        VisibilityTypeEnum visibility)
    {
        var dto = CreateEventDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            status: status,
            statusName: status.ToString(),
            statusCode: status.ToString().ToUpperInvariant());
        dto = dto with
        {
            VisibilityTypeId = (int)visibility,
            PublicActions =
            [
            new EventPublicActionDto
            {
                Id = Guid.NewGuid(),
                EventId = dto.Id,
                KindId = (int)EventPublicActionKindEnum.ExternalRegistration,
                Url = "https://example.com/register",
                DestinationDomain = "example.com",
                IsPrimary = true
            }
            ]
        };

        foreach (var mode in new[]
        {
            ParticipationHandlingModeEnum.PlatformManaged,
            ParticipationHandlingModeEnum.ExternalManaged
        })
        {
            dto.ParticipationConfiguration = new EventParticipationConfigurationDto
            {
                ParticipationHandlingModeId = (int)mode
            };
            var links = new EventDetailLinkPolicy()
                .GetLinks(dto, new ClaimsPrincipal(new ClaimsIdentity("test")))
                .ToList();

            foreach (var relation in new[]
            {
                LinkRelations.StartRegistration,
                LinkRelations.SignInToRegister,
                LinkRelations.ExternalRegistration
            })
            {
                await Assert.That(links.Any(link => link.Rel == relation)).IsFalse();
            }
        }
    }

    private static EventDto CreateEventDto(
        Guid eventId,
        Guid tenantId,
        Guid? organizationId,
        Guid? userId = null,
        int provenanceTypeId = 0,
        string? provenanceTypeCode = null,
        Guid? organizerActorId = null,
        EventStatusEnum status = EventStatusEnum.Draft,
        string statusName = "Draft",
        string statusCode = "DRAFT") => new()
        {
            Id = eventId,
            TenantId = tenantId,
            VisitorAccess = Explore.Application.DTOs.PublicExperience.VisitorAccessCapabilityDto.From(
                Explore.Application.Services.VisitorAccessCapabilityResolver.EvaluateProposedState(new(
                    VisitorAccessMode.FullRegistrationAndAuth,
                    [new(AuthenticationProviderKind.Keycloak, true, true, PublicOnboardingPolicy.Allowed, "https://accounts.example.test/register")]))),
            Title = "Program launch",
            ActorId = Guid.NewGuid(),
            ActorDisplayName = "ISLAMU",
            ActorTypeId = userId.HasValue ? (int)ActorTypeEnum.User : (int)ActorTypeEnum.Organization,
            ActorTypeFullName = userId.HasValue ? "User" : "Organization",
            ActorUserId = userId,
            ActorOrganizationId = organizationId,
            ProvenanceTypeId = provenanceTypeId,
            ProvenanceTypeCode = provenanceTypeCode,
            OrganizerActorId = organizerActorId,
            EventStatusId = (int)status,
            EventStatusFullName = statusName,
            EventStatusMasterCode = statusCode,
            IsPubliclyEligible = true,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormatFullName = "In person",
            EventFormatMasterCode = "IN_PERSON"
        };
}
